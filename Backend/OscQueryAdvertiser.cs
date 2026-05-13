using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VRC.OSCQuery;

internal sealed class OscQueryAdvertiser : IDisposable {
    private const int trackingRefreshDelayMs = 300;

    private readonly object trackingLock = new object();
    private readonly OSCQueryService service;
    private readonly TimeSpan scanInterval;
    private readonly int lostScanThreshold;
    private CancellationTokenSource? trackingCancellationTokenSource;
    private Task? trackingTask;
    private int isDisposed;
    private readonly IPAddress hostIP;
    private readonly int oscPort;
    public IPAddress HostIP => hostIP;
    public int OscPort => oscPort;

    public event Action<OscRemoteServiceInfo>? ServiceDiscovered;
    public event Action<OscRemoteServiceInfo>? ServiceLost;

    private OscQueryAdvertiser(OSCQueryService service, TimeSpan scanInterval, int lostScanThreshold) {
        this.service = service;
        this.scanInterval = scanInterval;
        this.lostScanThreshold = lostScanThreshold;
        this.hostIP = service.HostIP;
        this.oscPort = service.OscPort;
    }

    public static OscQueryAdvertiser Start(IPAddress? hostIP = null, int udpPort = 0, TimeSpan? scanInterval = null, int lostScanThreshold = 2) {
        if (lostScanThreshold < 1) lostScanThreshold = 1;
        var resolvedHostIP = hostIP ?? IPAddress.Loopback;

        var service = new OSCQueryServiceBuilder()
            .WithServiceName("OSCAvatarRecord")
            .WithHostIP(resolvedHostIP)
            .WithOscIP(resolvedHostIP)
            .WithTcpPort(Extensions.GetAvailableTcpPort())
            .WithUdpPort(udpPort > 0 ? udpPort : Extensions.GetAvailableUdpPort())
            .WithDefaults()
            .Build();

        service.AddEndpoint<string>("/avatar/change", Attributes.AccessValues.WriteOnly, description: "Active avatar ID in the format of avtr_{GUID}");
        service.AddEndpoint<int>("/avatar/parameters/*", Attributes.AccessValues.ReadWrite, description: "Avatar parameter with a 32-bit primitive value. The parameter name is determined by the suffix of the address.");
        service.AddEndpoint<bool>("/avatar/parameters/*", Attributes.AccessValues.ReadWrite, description: "Avatar parameter with a 32-bit primitive value. The parameter name is determined by the suffix of the address.");
        service.AddEndpoint<float>("/avatar/parameters/*", Attributes.AccessValues.ReadWrite, description: "Avatar parameter with a 32-bit primitive value. The parameter name is determined by the suffix of the address.");

        return new OscQueryAdvertiser(service, scanInterval ?? TimeSpan.FromSeconds(5), lostScanThreshold);
    }

    public Task StartTrackingAsync(CancellationToken cancellationToken) {
        if (Volatile.Read(ref isDisposed) != 0) return Task.CompletedTask;

        lock (trackingLock) {
            if (trackingTask != null && !trackingTask.IsCompleted) return Task.CompletedTask;

            trackingCancellationTokenSource?.Dispose();
            trackingCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            trackingTask = Task.Run(() => TrackServicesLoopAsync(trackingCancellationTokenSource.Token), cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task StopTrackingAsync() {
        Task? trackingTask;
        lock (trackingLock) {
            trackingTask = this.trackingTask;
            trackingCancellationTokenSource?.Cancel();
        }

        if (trackingTask != null)
            try {
                await trackingTask;
            } catch (OperationCanceledException) { }

        lock (trackingLock) {
            this.trackingTask = null;
            trackingCancellationTokenSource?.Dispose();
            trackingCancellationTokenSource = null;
        }
    }

    private async Task TrackServicesLoopAsync(CancellationToken cancellationToken) {
        var activeServices = new Dictionary<string, OscRemoteServiceInfo>(StringComparer.OrdinalIgnoreCase);
        var missedScanCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        while (!cancellationToken.IsCancellationRequested) {
            try {
                service.RefreshServices();
                await Task.Delay(TimeSpan.FromMilliseconds(trackingRefreshDelayMs), cancellationToken);

                var services = service.GetOSCServices();
                var seenServiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var serviceProfile in services) {
                    if (!TryCreateRemoteServiceInfo(serviceProfile, out var serviceInfo)) continue;

                    seenServiceIds.Add(serviceInfo.id);
                    missedScanCounts[serviceInfo.id] = 0;

                    bool shouldPublishDiscovery = true;
                    if (activeServices.TryGetValue(serviceInfo.id, out var existingInfo)) {
                        shouldPublishDiscovery = !existingInfo.address.Equals(serviceInfo.address) || existingInfo.port != serviceInfo.port;
                    }

                    activeServices[serviceInfo.id] = serviceInfo;
                    if (shouldPublishDiscovery) {
                        Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] OSC remote service discovered: {serviceInfo.name} ({serviceInfo.address}:{serviceInfo.port})");
                        ServiceDiscovered?.Invoke(serviceInfo);
                    }
                }

                if (activeServices.Count != 0) {
                    var staleServiceIds = new List<string>();
                    foreach (var activeService in activeServices) {
                        string serviceId = activeService.Key;
                        if (seenServiceIds.Contains(serviceId)) continue;

                        int missedCount = 0;
                        missedScanCounts.TryGetValue(serviceId, out missedCount);
                        missedCount++;
                        missedScanCounts[serviceId] = missedCount;
                        if (missedCount >= lostScanThreshold) {
                            staleServiceIds.Add(serviceId);
                        }
                    }

                    for (int index = 0, staleCount = staleServiceIds.Count; index < staleCount; index++) {
                        string staleServiceId = staleServiceIds[index];
                        if (!activeServices.TryGetValue(staleServiceId, out var staleServiceInfo)) continue;

                        activeServices.Remove(staleServiceId);
                        missedScanCounts.Remove(staleServiceId);
                        Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] OSC remote service lost: {staleServiceInfo.name} ({staleServiceInfo.address}:{staleServiceInfo.port})");
                        ServiceLost?.Invoke(staleServiceInfo);
                    }
                }

                await Task.Delay(scanInterval, cancellationToken);
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] OSCQuery tracking loop error: {ex.Message}");
                await Task.Delay(scanInterval, cancellationToken);
            }
        }
    }

    private static bool TryCreateRemoteServiceInfo(OSCQueryServiceProfile? serviceProfile, out OscRemoteServiceInfo serviceInfo) {
        serviceInfo = default;
        if (serviceProfile == null ||
            !serviceProfile.name.StartsWith("VRChat-Client-", StringComparison.OrdinalIgnoreCase) ||
            serviceProfile.serviceType != OSCQueryServiceProfile.ServiceType.OSC)
            return false;
        var address = serviceProfile.address;
        if (address == null || serviceProfile.port <= 0) return false;
        string id = $"{serviceProfile.name}@{address}:{serviceProfile.port}";
        serviceInfo = new OscRemoteServiceInfo(id, serviceProfile.name, address, serviceProfile.port);
        return true;
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0) return;
        try {
            StopTrackingAsync().GetAwaiter().GetResult();
        } finally {
            service.Dispose();
        }
    }
}



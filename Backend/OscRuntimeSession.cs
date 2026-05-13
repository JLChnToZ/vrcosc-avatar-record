using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using OscCore;

internal sealed class OscRuntimeSession : IAsyncDisposable {
    private const int upsertDebounceMs = 100;
    private const int uiSignalDebounceMs = 60;

    private readonly AvatarStateRepository repository;
    private OscQueryAdvertiser? advertiser;
    private readonly bool ownsAdvertiser;
    private readonly SemaphoreSlim connectionLock = new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim upsertFlushLock = new SemaphoreSlim(1, 1);
    private readonly object liveStateLock = new object();
    private readonly object remoteSendersLock = new object();
    private readonly ConcurrentDictionary<AvatarParameterKey, AvatarParameterStateEntry> pendingUpserts =
        new ConcurrentDictionary<AvatarParameterKey, AvatarParameterStateEntry>();
    private readonly Dictionary<AvatarParameterKey, AvatarParameterStateEntry> liveStates =
        new Dictionary<AvatarParameterKey, AvatarParameterStateEntry>();
    private readonly Dictionary<string, OscIO> remoteSenders =
        new Dictionary<string, OscIO>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<OscIO, Guid> remoteSenderSelectedAvatars =
        new Dictionary<OscIO, Guid>();
    private readonly List<StateChangeNotification> changeNotificationQueue =
        new List<StateChangeNotification>();
    private readonly Timer upsertDebounceTimer;
    private readonly Timer uiSignalDebounceTimer;
    private int uiSignalPending;
    private readonly object avatarIdLock = new object();
    private Guid avatarId = Guid.Empty;
    private CancellationTokenSource? runtimeCancellationTokenSource;
    private OscIO? receiverOscIO;
    private Task? receiverTask;
    private int hasSeenIncomingOsc;
    private int droppedParameterBeforeAvatarCount;
    private int isDisposed;

    public OscRuntimeSession(string databasePath, OscQueryAdvertiser? advertiser = null) {
        repository = new AvatarStateRepository(databasePath);
        this.advertiser = advertiser;
        ownsAdvertiser = advertiser == null;
        if (advertiser != null) {
            advertiser.ServiceDiscovered += OnRemoteServiceDiscovered;
            advertiser.ServiceLost += OnRemoteServiceLost;
        }
        upsertDebounceTimer = new Timer(OnUpsertDebounceTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        uiSignalDebounceTimer = new Timer(OnUiSignalDebounceTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<StateChangeBatchEventArgs>? StateChanged;
    public event Action<OscRemoteServiceInfo>? RemoteServiceDiscovered;
    public event Action<OscRemoteServiceInfo>? RemoteServiceLost;
    public event Action? IncomingOscObserved;

    public bool IsConnected => receiverTask != null && !receiverTask.IsCompleted;
    public bool HasSeenIncomingOsc => Volatile.Read(ref hasSeenIncomingOsc) != 0;
    public int DroppedParametersBeforeAvatar => Volatile.Read(ref droppedParameterBeforeAvatarCount);

    public async Task InitializeAsync(CancellationToken cancellationToken) {
        await repository.InitializeAsync(cancellationToken);
        var states = await repository.GetAllStatesAsync(cancellationToken);

        lock (liveStateLock) {
            liveStates.Clear();
            for (int index = 0, stateCount = states.Count; index < stateCount; index++) {
                var state = states[index];
                liveStates[state.GetKey()] = state;
            }
        }
    }

    public async Task ConnectAsync(IPAddress? oscIP = null, int oscPort = 0, CancellationToken cancellationToken = default) {
        await connectionLock.WaitAsync(cancellationToken);
        try {
            if (IsConnected) return;

            // Use provided IP or default to loopback
            var localIP = oscIP ?? IPAddress.Loopback;
            int localPort = oscPort > 0 ? oscPort : 9001;

            runtimeCancellationTokenSource = new CancellationTokenSource();
            var runtimeToken = runtimeCancellationTokenSource.Token;

            // Create receiver with configured IP and port
            receiverOscIO = new OscIO(localIP, localPort);
            Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Starting OSC receiver on {localIP}:{localPort}");
            receiverTask = receiverOscIO.RunAsync(this, runtimeToken);
            _ = receiverTask.ContinueWith(task => {
                if (task.Exception == null) return;
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] OSC receiver task faulted: {task.Exception.GetBaseException().Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);

            // Create/update the owned advertiser on connect so mDNS host/port always
            // match the active listen endpoint and reconnect changes are reflected.
            if (ownsAdvertiser) {
                if (advertiser != null) {
                    advertiser.ServiceDiscovered -= OnRemoteServiceDiscovered;
                    advertiser.ServiceLost -= OnRemoteServiceLost;
                    advertiser.Dispose();
                }

                var newAdvertiser = OscQueryAdvertiser.Start(hostIP: localIP, udpPort: localPort);
                newAdvertiser.ServiceDiscovered += OnRemoteServiceDiscovered;
                newAdvertiser.ServiceLost += OnRemoteServiceLost;
                advertiser = newAdvertiser;
            } else if (advertiser == null) {
                throw new InvalidOperationException("External OSC advertiser is not available.");
            }
            await advertiser.StartTrackingAsync(runtimeToken);
        } finally {
            connectionLock.Release();
        }
    }

    public async Task DisconnectAsync() {
        await connectionLock.WaitAsync();
        try {
            if (runtimeCancellationTokenSource == null) return;

            runtimeCancellationTokenSource.Cancel();

            if (advertiser != null) await advertiser.StopTrackingAsync();

            if (receiverTask != null) {
                try {
                    await receiverTask;
                } catch (OperationCanceledException) {
                }
            }

            await FlushPendingUpsertsAsync();

            receiverOscIO?.Dispose();
            receiverOscIO = null;
            receiverTask = null;

            ClearRemoteSenders();

            runtimeCancellationTokenSource.Dispose();
            runtimeCancellationTokenSource = null;
        } finally {
            connectionLock.Release();
        }
    }

    public IReadOnlyList<AvatarParameterStateEntry> GetAllStates() {
        AvatarParameterStateEntry[] snapshot;
        lock (liveStateLock) {
            snapshot = new AvatarParameterStateEntry[liveStates.Count];
            liveStates.Values.CopyTo(snapshot, 0);
        }
        return snapshot;
    }

    public Task SetSynchronizationEnabledAsync(AvatarParameterKey key, bool enabled, CancellationToken cancellationToken) {
        bool updated = false;
        AvatarParameterStateEntry newState = default;
        lock (liveStateLock) {
            if (liveStates.TryGetValue(key, out var existing)) {
                newState = new AvatarParameterStateEntry(existing, enabled);
                liveStates[key] = newState;
                updated = true;
            }
        }

        if (!enabled) pendingUpserts.TryRemove(key, out _);

        if (updated) {
            lock (liveStateLock)
                changeNotificationQueue.Add(
                    new StateChangeNotification(key, newState.value, newState.syncEnabled)
                );
            ScheduleUiSignal();
            return repository.UpsertSynchronizationStateAsync(newState, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> UpdateParameterValueAsync(AvatarParameterKey key, Primitive32 value, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        bool shouldForceSync = false;
        AvatarParameterStateEntry newState = default;

        lock (liveStateLock) {
            if (!liveStates.TryGetValue(key, out var existing)) return false;
            newState = new AvatarParameterStateEntry(existing, value, now);
            liveStates[key] = newState;
            shouldForceSync = existing.syncEnabled;
        }

        pendingUpserts.TryRemove(key, out _);

        await repository.UpsertParameterStateAsync(key.avatarId, key.parameterName, value, now, cancellationToken);

        lock (liveStateLock) {
            changeNotificationQueue.Add(new StateChangeNotification(key, newState.value, newState.syncEnabled));
        }
        ScheduleUiSignal();

        if (shouldForceSync && TryGetActiveAvatarId(out var activeAvatarId) && activeAvatarId.Equals(key.avatarId))
            await ForceSyncParameterAsync(key.avatarId, key.parameterName, cancellationToken);

        return true;
    }

    public async Task DeleteParameterStateAsync(AvatarParameterKey parameterKey, CancellationToken cancellationToken) {
        lock (liveStateLock) {
            liveStates.Remove(parameterKey);
            changeNotificationQueue.Add(StateChangeNotification.ParameterRemoved(parameterKey.avatarId, parameterKey.parameterName));
        }
        ScheduleUiSignal();

        await repository.DeleteParameterStateAsync(parameterKey.avatarId, parameterKey.parameterName, cancellationToken);
    }

    public async Task DeleteAvatarStatesAsync(Guid avatarId, CancellationToken cancellationToken) {
        lock (liveStateLock) {
            var removeKeys = new List<AvatarParameterKey>();
            foreach (var kv in liveStates)
                if (kv.Value.avatarId.Equals(avatarId))
                    removeKeys.Add(kv.Key);

            for (int i = 0, removeCount = removeKeys.Count; i < removeCount; i++)
                liveStates.Remove(removeKeys[i]);

            changeNotificationQueue.Add(StateChangeNotification.AvatarRemoved(avatarId));
        }
        ScheduleUiSignal();

        await repository.DeleteAvatarStatesAsync(avatarId, cancellationToken);
    }

    public Task SendEnabledParametersBackForAvatarIfActiveAsync(Guid avatarId) {
        if (!TryGetActiveAvatarId(out var activeAvatarId) || !activeAvatarId.Equals(avatarId))
            return Task.CompletedTask;

        return SendParametersBackForAvatarAsync(avatarId, true);
    }

    public bool TryGetActiveAvatarId(out Guid avatarId) {
        lock (avatarIdLock) {
            var currentAvatarId = this.avatarId;
            if (currentAvatarId == Guid.Empty) {
                avatarId = Guid.Empty;
                return false;
            }
            avatarId = currentAvatarId;
            return true;
        }
    }

    internal void SetAvatar(Guid avatarId) {
        lock (avatarIdLock) this.avatarId = avatarId;
    }

    internal void NotifyIncomingOscPacket(IPEndPoint remoteEndPoint, int payloadSize) {
        if (Interlocked.Exchange(ref hasSeenIncomingOsc, 1) != 0) return;
        Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] First incoming OSC packet from {remoteEndPoint} ({payloadSize} bytes)");
        IncomingOscObserved?.Invoke();
    }

    internal void NotifyParameterDroppedBeforeAvatar(string parameterName) {
        int droppedCount = Interlocked.Increment(ref droppedParameterBeforeAvatarCount);
        if (droppedCount <= 3 || droppedCount % 50 == 0) {
            Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Ignored parameter {parameterName}: avatar not known yet (count={droppedCount})");
        }
    }

    internal Task SendEnabledParametersForAvatarAsync(Guid avatarId) => SendParametersBackForAvatarAsync(avatarId, true);

    public async Task<bool> ForceSyncAvatarIfActiveAsync(Guid avatarId) =>
        TryGetActiveAvatarId(out var activeAvatarId) && activeAvatarId.Equals(avatarId) &&
        (await SendParametersBackForAvatarAsync(avatarId, true)) > 0;

    public async Task<bool> ForceSyncParameterAsync(Guid avatarId, string parameterName, CancellationToken cancellationToken) {
        var senders = GetRemoteSendersSnapshot();
        if (senders.Length == 0) return false;

        AvatarParameterStateEntry state = default;
        lock (liveStateLock) liveStates.TryGetValue(new AvatarParameterKey(avatarId, parameterName), out state);

        if (!state.value.IsValid) return false;

        var message = state.GetOscMessage();
        int delivered = await Task.Run(() => BroadcastPacket(message, senders), cancellationToken);
        return delivered > 0;
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0) return;

        if (advertiser != null) {
            advertiser.ServiceDiscovered -= OnRemoteServiceDiscovered;
            advertiser.ServiceLost -= OnRemoteServiceLost;
        }

        await DisconnectAsync();
        upsertDebounceTimer.Dispose();
        uiSignalDebounceTimer.Dispose();
        connectionLock.Dispose();
        upsertFlushLock.Dispose();
        if (ownsAdvertiser) advertiser?.Dispose();
        await repository.DisposeAsync();
    }

    internal void QueueParameterUpsert(AvatarParameterKey key, Primitive32 value, DateTimeOffset observedAtUtc) {
        if (Volatile.Read(ref isDisposed) != 0) return;

        bool syncEnabled = true; // default for new parameters
        AvatarParameterStateEntry newState;

        lock (liveStateLock) {
            if (liveStates.TryGetValue(key, out var existing)) {
                if (!existing.syncEnabled) return;
                syncEnabled = existing.syncEnabled;
            }
            newState = new AvatarParameterStateEntry(key, value, syncEnabled, observedAtUtc);
            liveStates[key] = newState;
            changeNotificationQueue.Add(new StateChangeNotification(key, newState.value, newState.syncEnabled));
        }

        pendingUpserts[key] = newState;

        ScheduleUiSignal();
        upsertDebounceTimer.Change(upsertDebounceMs, Timeout.Infinite);
    }

    private void OnUpsertDebounceTimerTick(object? state) => _ = FlushPendingUpsertsAsync();

    private async Task FlushPendingUpsertsAsync() {
        if (Volatile.Read(ref isDisposed) != 0) return;

        await upsertFlushLock.WaitAsync();
        try {
            var batch = DrainPendingUpserts();
            if (batch.Count == 0) return;

            await repository.UpsertParameterStatesAsync(batch, CancellationToken.None);
        } finally {
            upsertFlushLock.Release();
        }
    }

    private void OnUiSignalDebounceTimerTick(object? state) {
        Interlocked.Exchange(ref uiSignalPending, 0);
        List<StateChangeNotification> changesBatch;
        lock (liveStateLock) {
            if (changeNotificationQueue.Count == 0) return;
            changesBatch = new List<StateChangeNotification>(changeNotificationQueue);
            changeNotificationQueue.Clear();
        }
        StateChanged?.Invoke(this, new StateChangeBatchEventArgs(changesBatch));
    }

    private void ScheduleUiSignal() {
        if (Volatile.Read(ref isDisposed) != 0) return;

        Interlocked.Exchange(ref uiSignalPending, 1);
        uiSignalDebounceTimer.Change(uiSignalDebounceMs, Timeout.Infinite);
    }

    private List<AvatarParameterStateEntry> DrainPendingUpserts() {
        var batch = new List<AvatarParameterStateEntry>(pendingUpserts.Count);
        foreach (var key in pendingUpserts.Keys)
            if (pendingUpserts.TryRemove(key, out var entry))
                batch.Add(entry);
        return batch;
    }

    private Task<int> SendParametersBackForAvatarAsync(Guid avatarId, bool enabledOnly) {
        var senders = GetRemoteSendersSnapshot();
        if (senders.Length == 0) return Task.FromResult(0);

        var statesToSend = new List<AvatarParameterStateEntry>();
        lock (liveStateLock)
            foreach (var state in liveStates.Values)
                if (state.avatarId.Equals(avatarId) &&
                    (!enabledOnly || state.syncEnabled) &&
                    state.value.IsValid)
                    statesToSend.Add(state);

        return statesToSend.Count == 0 ? Task.FromResult(0) : Task.Run(() => {
            int deliveredStates = 0;
            for (int index = 0, sendCount = statesToSend.Count; index < sendCount; index++) {
                int delivered = BroadcastPacket(statesToSend[index].GetOscMessage(), senders);
                if (delivered > 0) deliveredStates++;
            }
            return deliveredStates;
        });
    }

    private OscIO[] GetRemoteSendersSnapshot() {
        lock (remoteSendersLock) {
            var senders = new OscIO[remoteSenders.Count];
            int index = 0;
            foreach (var sender in remoteSenders.Values) {
                senders[index] = sender;
                index++;
            }
            return senders;
        }
    }

    private void OnRemoteServiceDiscovered(OscRemoteServiceInfo serviceInfo) {
        if (Volatile.Read(ref isDisposed) != 0) return;
        if (advertiser == null) return;

        var sender = new OscIO(advertiser.HostIP, advertiser.OscPort);
        sender.SetSendEndPoint(serviceInfo.address, serviceInfo.port);

        OscIO? replacedSender = null;
        lock (remoteSendersLock) {
            if (remoteSenders.TryGetValue(serviceInfo.id, out var existing))
                replacedSender = existing;
            remoteSenders[serviceInfo.id] = sender;
        }

        replacedSender?.Dispose();
        
        // Notify UI about discovered service
        RemoteServiceDiscovered?.Invoke(serviceInfo);
    }

    private void OnRemoteServiceLost(OscRemoteServiceInfo serviceInfo) {
        OscIO? removedSender = null;
        lock (remoteSendersLock) {
            if (remoteSenders.TryGetValue(serviceInfo.id, out var existing)) {
                removedSender = existing;
                remoteSenders.Remove(serviceInfo.id);
            }
        }

        SetAvatar(Guid.Empty);

        removedSender?.Dispose();
        
        // Notify UI about lost service
        RemoteServiceLost?.Invoke(serviceInfo);
    }

    private void ClearRemoteSenders() {
        OscIO[] disposedSenders;
        lock (remoteSendersLock) {
            disposedSenders = new OscIO[remoteSenders.Count];
            remoteSenders.Values.CopyTo(disposedSenders, 0);
            remoteSenders.Clear();
        }

        for (int index = 0, senderCount = disposedSenders.Length; index < senderCount; index++)
            disposedSenders[index].Dispose();
    }

    private static int BroadcastPacket(OscPacket packet, OscIO[] senders) {
        int delivered = 0;
        for (int index = 0, senderCount = senders.Length; index < senderCount; index++) {
            try {
                senders[index].Send(packet);
                delivered++;
            } catch (Exception ex) {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] OSC send failed: {ex.Message}");
            }
        }
        return delivered;
    }
}

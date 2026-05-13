using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OscCore;
using OscCore.LowLevel;

internal sealed class OscIO : IDisposable {
    private readonly IPEndPoint listenEndPoint;
    private IPEndPoint? sendEndPoint;
    private readonly MemoryStream bufferStream;
    private readonly OscWriter oscWriter;
    private readonly UdpClient senderClient;
    private readonly SemaphoreSlim sendLock;

    public OscIO(IPAddress ipAddress, int listenPort) {
        listenEndPoint = new IPEndPoint(ipAddress, listenPort);
        senderClient = new UdpClient();
        bufferStream = new MemoryStream();
        oscWriter = new OscWriter(bufferStream);
        sendLock = new SemaphoreSlim(1, 1);
    }

    public void SetSendPort(int sendPort) {
        SetSendEndPoint(listenEndPoint.Address, sendPort);
    }

    public void SetSendEndPoint(IPAddress ipAddress, int sendPort) {
        sendLock.Wait();
        try {
            sendEndPoint = new IPEndPoint(ipAddress, sendPort);
        } finally {
            sendLock.Release();
        }
    }

    public async Task RunAsync(OscRuntimeSession session, CancellationToken cancellationToken) {
        using var receiverClient = new UdpClient(listenEndPoint);
        Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] OSC receiver active on {listenEndPoint}");

        while (!cancellationToken.IsCancellationRequested) {
            UdpReceiveResult received;
            try {
                received = await receiverClient.ReceiveAsync(cancellationToken);
            } catch (OperationCanceledException) {
                break;
            }

            var ep = received.RemoteEndPoint;
            session.NotifyIncomingOscPacket(ep, received.Buffer.Length);
            string sourceUriText = $"udp://{ep.Address}:{ep.Port}";
            var sourceUri = new Uri(sourceUriText);
            OscPacket packet;
            try {
                packet = OscPacket.Read(received.Buffer, 0, received.Buffer.Length, sourceUri, null);
            } catch (Exception ex) {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] OSC parse failed from {ep}: {ex.Message}");
                continue;
            }

            if (packet is OscMessage message) {
                await OscMessageHandler.HandleAsync(message, session, cancellationToken);
                continue;
            }

            if (packet is IEnumerable<OscMessage> bundle)
                foreach (var m in bundle)
                    await OscMessageHandler.HandleAsync(m, session, cancellationToken);
        }
    }

    public void Send(OscPacket packet) {
        sendLock.Wait();
        var pool = ArrayPool<byte>.Shared;
        byte[]? buffer = null;
        try {
            if (sendEndPoint == null) return;
            packet.Write(oscWriter);
            int payloadLength = (int)bufferStream.Length;
            buffer = pool.Rent(payloadLength);
            bufferStream.Position = 0;
            bufferStream.Read(buffer, 0, payloadLength);
            senderClient.Send(buffer, payloadLength, sendEndPoint);
        } finally {
            if (buffer != null) pool.Return(buffer);
            bufferStream.SetLength(0);
            sendLock.Release();
        }
    }

    public void Dispose() {
        senderClient.Dispose();
        bufferStream.Dispose();
        sendLock.Dispose();
    }
}


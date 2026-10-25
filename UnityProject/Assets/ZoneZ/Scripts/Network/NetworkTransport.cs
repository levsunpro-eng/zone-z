using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZoneZ
{
    public enum TransportEventKind { Connected, Data, Disconnected, Error }

    public struct TransportEvent
    {
        public TransportEventKind Kind;
        public int PeerId;
        public string Payload;
    }

    /// <summary>
    /// Single-use, thread-safe TCP transport. Call TryPoll on the game thread.
    /// Server peer IDs are positive; a client's server has ID zero.
    /// Workers never use Unity APIs, and Send only queues a bounded message.
    /// </summary>
    public sealed class NetworkTransport : IDisposable
    {
        public const int MaxPayloadBytes = 512 * 1024;
        public const int MaxConnections = 24;
        const int MaxQueuedData = 512;
        const int MaxQueuedDataBytes = 8 * 1024 * 1024;
        const int MaxPeerIncoming = 64;
        const int MaxPeerIncomingBytes = 1024 * 1024;
        const int MaxPeerOutgoing = 128;
        const int MaxPeerOutgoingBytes = 2 * 1024 * 1024;
        const int ConnectTimeoutMs = 8000;

        sealed class Peer
        {
            public int Id;
            public TcpClient Client;
            public Stream Stream;
            public bool Closed;
            public readonly Queue<byte[]> Outgoing = new Queue<byte[]>();
            public readonly SemaphoreSlim SendReady = new SemaphoreSlim(0);
            public int OutgoingBytes;
            public int IncomingCount;
            public int IncomingBytes;
        }

        struct QueuedEvent
        {
            public TransportEvent Event;
            public int Bytes;
        }

        readonly object gate = new object();
        readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        readonly Queue<QueuedEvent> events = new Queue<QueuedEvent>();
        readonly HashSet<Task> workers = new HashSet<Task>();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly CancellationToken lifetimeToken;
        readonly UTF8Encoding utf8 = new UTF8Encoding(false, true);
        TcpListener listener;
        TcpClient connectingClient;
        bool started;
        bool disposed;
        int nextPeerId = 1;
        int queuedData;
        int queuedDataBytes;

        public NetworkTransport() { lifetimeToken = lifetime.Token; }

        public void Listen(int port)
        {
            ValidatePort(port);
            lock (gate)
            {
                BeginLocked();
                try
                {
                    listener = new TcpListener(IPAddress.Any, port);
                    listener.Start(MaxConnections);
                    RunWorkerLocked(AcceptLoop);
                }
                catch (SocketException ex)
                {
                    if (listener != null) listener.Stop();
                    listener = null;
                    QueueControlLocked(TransportEventKind.Error, -1, "Cannot listen: " + ex.Message);
                }
            }
        }

        public void Connect(string host, int port)
        {
            Connect(host, port, false);
        }

        public void Connect(string host, int port, bool useTls)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", "host");
            ValidatePort(port);
            lock (gate)
            {
                BeginLocked();
                connectingClient = new TcpClient();
                TcpClient client = connectingClient;
                RunWorkerLocked(() => ConnectWorker(client, host.Trim(), port, useTls));
            }
        }

        public void Send(int peerId, string payload)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            if (payload.Length > MaxPayloadBytes || utf8.GetByteCount(payload) > MaxPayloadBytes)
                throw new ArgumentException("Message exceeds the transport size limit.", "payload");
            byte[] data = utf8.GetBytes(payload);
            Peer slowPeer = null;
            lock (gate)
            {
                Peer peer;
                if (disposed || !peers.TryGetValue(peerId, out peer) || peer.Closed) return;
                if (peer.Outgoing.Count >= MaxPeerOutgoing || peer.OutgoingBytes + data.Length > MaxPeerOutgoingBytes)
                    slowPeer = peer;
                else
                {
                    peer.Outgoing.Enqueue(data);
                    peer.OutgoingBytes += data.Length;
                    peer.SendReady.Release();
                }
            }
            if (slowPeer != null) ClosePeer(slowPeer, "Outgoing queue exceeded; connection is too slow.");
        }

        public bool TryPoll(out TransportEvent evt)
        {
            lock (gate)
            {
                if (events.Count == 0) { evt = default(TransportEvent); return false; }
                QueuedEvent queued = events.Dequeue();
                evt = queued.Event;
                Peer peer;
                if (evt.Kind == TransportEventKind.Data)
                {
                    queuedData--;
                    queuedDataBytes -= queued.Bytes;
                    if (peers.TryGetValue(evt.PeerId, out peer))
                    {
                        peer.IncomingCount--;
                        peer.IncomingBytes -= queued.Bytes;
                    }
                }
                // Retaining a closed peer until this event is consumed bounds
                // lifecycle events too, even during repeated connection churn.
                else if (evt.Kind == TransportEventKind.Disconnected) peers.Remove(evt.PeerId);
                return true;
            }
        }

        public void Disconnect(int peerId)
        {
            Peer peer;
            lock (gate) { if (!peers.TryGetValue(peerId, out peer)) return; }
            ClosePeer(peer, "Disconnected.");
        }

        public void Dispose()
        {
            TcpListener oldListener;
            TcpClient pending;
            Peer[] oldPeers;
            Task[] oldWorkers;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                oldListener = listener;
                listener = null;
                pending = connectingClient;
                connectingClient = null;
                oldPeers = new Peer[peers.Count];
                peers.Values.CopyTo(oldPeers, 0);
                foreach (Peer peer in oldPeers)
                {
                    peer.Closed = true;
                    peer.Outgoing.Clear();
                    peer.OutgoingBytes = 0;
                    peer.SendReady.Release();
                }
                peers.Clear();
                events.Clear();
                queuedData = queuedDataBytes = 0;
                oldWorkers = new Task[workers.Count];
                workers.CopyTo(oldWorkers);
            }
            // Closing sockets interrupts pending reads/writes/accepts without
            // waiting for any network operation on the game thread.
            if (oldListener != null) oldListener.Stop();
            if (pending != null) pending.Close();
            foreach (Peer peer in oldPeers) { peer.Client.Close(); peer.Stream.Dispose(); }
            lifetime.Cancel();
            Task.WhenAll(oldWorkers).ContinueWith(t =>
            {
                // Observe unexpected worker failures as well as releasing resources.
                if (t.IsFaulted) { var ignored = t.Exception; }
                lifetime.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        void BeginLocked()
        {
            if (disposed) throw new ObjectDisposedException("NetworkTransport");
            if (started) throw new InvalidOperationException("Use a new transport for each connection or listener.");
            started = true;
        }

        static void ValidatePort(int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
        }

        void RunWorkerLocked(Func<Task> action)
        {
            Task worker = Task.Run(action);
            workers.Add(worker);
            worker.ContinueWith(t =>
            {
                if (t.IsFaulted) { var ignored = t.Exception; }
                lock (gate) workers.Remove(t);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        async Task AcceptLoop()
        {
            while (!lifetimeToken.IsCancellationRequested)
            {
                TcpListener current;
                lock (gate) { if (disposed) return; current = listener; }
                try
                {
                    TcpClient client = await current.AcceptTcpClientAsync().ConfigureAwait(false);
                    lock (gate)
                    {
                        if (disposed || peers.Count >= MaxConnections) client.Close();
                        else AddPeerLocked(client, client.GetStream(), nextPeerId++);
                    }
                }
                catch (Exception ex)
                {
                    lock (gate)
                        if (!disposed) QueueControlLocked(TransportEventKind.Error, -1, "Listener stopped: " + ex.Message);
                    return;
                }
            }
        }

        async Task ConnectWorker(TcpClient client, string host, int port, bool useTls)
        {
            try
            {
                Stream stream = null;
                Task connect = ConnectAndAuthenticate(client, host, port, useTls, connected => stream = connected);
                Task deadline = Task.Delay(ConnectTimeoutMs, lifetimeToken);
                if (await Task.WhenAny(connect, deadline).ConfigureAwait(false) != connect)
                {
                    // DNS/connect may finish after cancellation on older runtimes.
                    // Closing the socket prevents a late connection being adopted.
                    _ = connect.ContinueWith(t => { var ignored = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    lifetimeToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("Connection timed out after 8 seconds.");
                }
                await connect.ConfigureAwait(false);
                lock (gate)
                {
                    connectingClient = null;
                    if (disposed) { client.Close(); if (stream != null) stream.Dispose(); }
                    else AddPeerLocked(client, stream, 0);
                }
            }
            catch (Exception ex)
            {
                client.Close();
                lock (gate)
                {
                    connectingClient = null;
                    if (!disposed) QueueControlLocked(TransportEventKind.Error, 0, "Cannot connect: " + ex.Message);
                }
            }
        }

        static async Task ConnectAndAuthenticate(TcpClient client, string host, int port, bool useTls, Action<Stream> connected)
        {
            await client.ConnectAsync(host, port).ConfigureAwait(false);
            Stream stream = client.GetStream();
            if (useTls)
            {
                // Default validation checks OS trust and the requested hostname.
                // TLS 1.2 is available on every supported Unity desktop runtime.
                var secure = new SslStream(stream, false);
                try
                {
                    await secure.AuthenticateAsClientAsync(host, null, SslProtocols.Tls12, true).ConfigureAwait(false);
                    stream = secure;
                }
                catch { secure.Dispose(); throw; }
            }
            connected(stream);
        }

        void AddPeerLocked(TcpClient client, Stream stream, int id)
        {
            client.NoDelay = true;
            var peer = new Peer { Id = id, Client = client, Stream = stream };
            peers.Add(id, peer);
            QueueControlLocked(TransportEventKind.Connected, id, "");
            RunWorkerLocked(() => ReadLoop(peer));
            RunWorkerLocked(() => WriteLoop(peer));
        }

        async Task ReadLoop(Peer peer)
        {
            string reason = "Remote connection closed.";
            try
            {
                var header = new byte[4];
                while (await ReadExactly(peer.Stream, header).ConfigureAwait(false))
                {
                    int size = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
                    if (size < 0 || size > MaxPayloadBytes) { reason = "Invalid message length."; break; }
                    var data = new byte[size];
                    if (!await ReadExactly(peer.Stream, data).ConfigureAwait(false)) break;
                    string payload = utf8.GetString(data);
                    lock (gate)
                    {
                        if (disposed || peer.Closed) return;
                        if (queuedData >= MaxQueuedData || queuedDataBytes + size > MaxQueuedDataBytes ||
                            peer.IncomingCount >= MaxPeerIncoming || peer.IncomingBytes + size > MaxPeerIncomingBytes)
                        {
                            reason = "Incoming queue exceeded; peer is sending too quickly.";
                            break;
                        }
                        events.Enqueue(new QueuedEvent
                        {
                            Event = new TransportEvent { Kind = TransportEventKind.Data, PeerId = peer.Id, Payload = payload },
                            Bytes = size
                        });
                        queuedData++;
                        queuedDataBytes += size;
                        peer.IncomingCount++;
                        peer.IncomingBytes += size;
                    }
                }
            }
            catch (DecoderFallbackException) { reason = "Invalid UTF-8 message."; }
            catch (Exception ex) { reason = "Connection read failed: " + ex.Message; }
            finally { ClosePeer(peer, reason); }
        }

        async Task<bool> ReadExactly(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset, lifetimeToken).ConfigureAwait(false);
                if (count == 0) return false;
                offset += count;
            }
            return true;
        }

        async Task WriteLoop(Peer peer)
        {
            string reason = "Connection writer stopped.";
            try
            {
                var header = new byte[4];
                while (true)
                {
                    await peer.SendReady.WaitAsync(lifetimeToken).ConfigureAwait(false);
                    byte[] data;
                    lock (gate)
                    {
                        if (disposed || peer.Closed) return;
                        if (peer.Outgoing.Count == 0) continue;
                        data = peer.Outgoing.Dequeue();
                        peer.OutgoingBytes -= data.Length;
                    }
                    int size = data.Length;
                    header[0] = (byte)size;
                    header[1] = (byte)(size >> 8);
                    header[2] = (byte)(size >> 16);
                    header[3] = (byte)(size >> 24);
                    await peer.Stream.WriteAsync(header, 0, header.Length, lifetimeToken).ConfigureAwait(false);
                    if (size > 0) await peer.Stream.WriteAsync(data, 0, size, lifetimeToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { reason = "Connection write failed: " + ex.Message; }
            finally { ClosePeer(peer, reason); }
        }

        void ClosePeer(Peer peer, string reason)
        {
            lock (gate)
            {
                if (peer.Closed) return;
                peer.Closed = true;
                peer.Outgoing.Clear();
                peer.OutgoingBytes = 0;
                peer.SendReady.Release();
                if (!disposed) QueueControlLocked(TransportEventKind.Disconnected, peer.Id, reason);
            }
            peer.Client.Close();
            peer.Stream.Dispose();
        }

        void QueueControlLocked(TransportEventKind kind, int peerId, string payload)
        {
            events.Enqueue(new QueuedEvent { Event = new TransportEvent { Kind = kind, PeerId = peerId, Payload = payload } });
        }
    }
}

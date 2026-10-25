using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZoneZ;

public static class NetworkTransportSelfTest
{
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static string Run()
    {
        var passed = new List<string>();
        RoutingAndFraming(); passed.Add("two-client routing, UTF-8, ordered delivery, empty and maximum frames");
        InvalidFramesAndFlood(); passed.Add("fragmented reads, oversized/negative lengths, invalid UTF-8, bounded incoming flood");
        SlowReceiver(); passed.Add("bounded outgoing queue disconnects a stalled receiver");
        CapacityAndReuse(); passed.Add("24-peer limit, slot recovery, disposal and immediate port reuse");
        ConnectionFailureAndCancellation(); passed.Add("connection refusal and dispose/connect race");
        RejectUntrustedTls(); passed.Add("TLS rejects an untrusted localhost certificate");
        TlsFailureAndCancellation(); passed.Add("TLS rejects plaintext, times out stalled handshakes, and closes cancelled connections without fallback");
        return "PASS: " + string.Join("\nPASS: ", passed);
    }

    static void RoutingAndFraming()
    {
        int port = FreePort();
        using (var server = new NetworkTransport())
        using (var first = new NetworkTransport())
        using (var second = new NetworkTransport())
        {
            server.Listen(port);
            first.Connect("127.0.0.1", port);
            Assert(Next(first, TransportEventKind.Connected).PeerId == 0, "Client server ID");
            int firstId = Next(server, TransportEventKind.Connected).PeerId;
            second.Connect("localhost", port);
            Next(second, TransportEventKind.Connected);
            int secondId = Next(server, TransportEventKind.Connected).PeerId;
            Assert(firstId > 0 && secondId > 0 && firstId != secondId, "Unique server IDs");
            first.Send(0, "Привет, пацаны! 🎮");
            TransportEvent received = Next(server, TransportEventKind.Data);
            Assert(received.PeerId == firstId && received.Payload == "Привет, пацаны! 🎮", "UTF-8 inbound routing");
            server.Send(secondId, "only second");
            Assert(Next(second, TransportEventKind.Data).Payload == "only second", "Outbound peer routing");
            TransportEvent unexpected;
            Assert(!first.TryPoll(out unexpected), "No cross-client delivery");
            for (int i = 0; i < 32; i++) second.Send(0, "ordered-" + i);
            for (int i = 0; i < 32; i++)
            {
                received = Next(server, TransportEventKind.Data);
                Assert(received.PeerId == secondId && received.Payload == "ordered-" + i, "Ordered messages");
            }
            first.Send(0, "");
            Assert(Next(server, TransportEventKind.Data).Payload == "", "Empty message");
            string maximum = new string('x', NetworkTransport.MaxPayloadBytes);
            first.Send(0, maximum);
            Assert(Next(server, TransportEventKind.Data).Payload == maximum, "Maximum-sized message");
            bool rejected = false;
            try { first.Send(0, maximum + "x"); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "Oversized local message rejection");
            server.Disconnect(firstId);
            Assert(Next(server, TransportEventKind.Disconnected).PeerId == firstId, "Explicit server disconnect");
            Next(first, TransportEventKind.Disconnected);
            second.Send(0, "still alive");
            Assert(Next(server, TransportEventKind.Data).Payload == "still alive", "Other peer survives disconnect");
        }
    }

    static void InvalidFramesAndFlood()
    {
        int port = FreePort();
        using (var server = new NetworkTransport())
        {
            server.Listen(port);
            using (TcpClient raw = RawConnect(port))
            {
                int id = Next(server, TransportEventKind.Connected).PeerId;
                byte[] bytes = Frame(Utf8.GetBytes("fragmented привет"));
                foreach (byte b in bytes) raw.GetStream().WriteByte(b);
                Assert(Next(server, TransportEventKind.Data).Payload == "fragmented привет", "Partial header and payload reads");
                raw.GetStream().Write(Header(NetworkTransport.MaxPayloadBytes + 1), 0, 4);
                TransportEvent rejected = Next(server, TransportEventKind.Disconnected);
                Assert(rejected.PeerId == id && rejected.Payload.Contains("length"), "Oversized remote frame");
            }
            using (TcpClient raw = RawConnect(port))
            {
                Next(server, TransportEventKind.Connected);
                raw.GetStream().Write(Header(-1), 0, 4);
                Assert(Next(server, TransportEventKind.Disconnected).Payload.Contains("length"), "Negative frame size");
            }
            using (TcpClient raw = RawConnect(port))
            {
                Next(server, TransportEventKind.Connected);
                byte[] bytes = Frame(new byte[] { 0xc3, 0x28 });
                raw.GetStream().Write(bytes, 0, bytes.Length);
                Assert(Next(server, TransportEventKind.Disconnected).Payload.Contains("UTF-8"), "Malformed UTF-8 rejection");
            }
            using (TcpClient raw = RawConnect(port))
            {
                Next(server, TransportEventKind.Connected);
                var flood = new byte[65 * 5];
                for (int i = 0; i < 65; i++) { flood[i * 5] = 1; flood[i * 5 + 4] = 65; }
                raw.GetStream().Write(flood, 0, flood.Length);
                WaitClosed(raw);
                int dataEvents = 0;
                TransportEvent evt;
                bool disconnected = false;
                while (server.TryPoll(out evt))
                {
                    if (evt.Kind == TransportEventKind.Data) dataEvents++;
                    else if (evt.Kind == TransportEventKind.Disconnected)
                    {
                        Assert(evt.Payload.Contains("Incoming queue"), "Flood reason");
                        disconnected = true;
                    }
                    else throw new Exception("Unexpected flood event " + evt.Kind);
                }
                Assert(dataEvents == 64 && disconnected, "Incoming cap preserves a disconnect notification");
            }
        }
    }

    static void SlowReceiver()
    {
        int port = FreePort();
        using (var server = new NetworkTransport())
        {
            server.Listen(port);
            using (TcpClient raw = RawConnect(port))
            {
                raw.ReceiveBufferSize = 1024;
                int id = Next(server, TransportEventKind.Connected).PeerId;
                string large = new string('q', NetworkTransport.MaxPayloadBytes);
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) server.Send(id, large);
                Assert(clock.ElapsedMilliseconds < 4000, "Send must not wait on socket writes");
                TransportEvent evt = Next(server, TransportEventKind.Disconnected);
                Assert(evt.Payload.Contains("Outgoing queue"), "Stalled receiver is removed when the queue fills");
            }
        }
    }

    static void CapacityAndReuse()
    {
        int port = FreePort();
        var clients = new List<TcpClient>();
        var server = new NetworkTransport();
        try
        {
            server.Listen(port);
            for (int i = 0; i < NetworkTransport.MaxConnections; i++)
            {
                clients.Add(RawConnect(port));
                Next(server, TransportEventKind.Connected);
            }
            using (TcpClient refused = RawConnect(port)) WaitClosed(refused);
            clients[0].Close();
            Next(server, TransportEventKind.Disconnected);
            clients.Add(RawConnect(port));
            Next(server, TransportEventKind.Connected);
            server.Dispose();
            server.Dispose();
            for (int i = 1; i < clients.Count; i++) WaitClosed(clients[i]);
            using (var replacement = new NetworkTransport())
            {
                replacement.Listen(port);
                using (TcpClient raw = RawConnect(port)) Next(replacement, TransportEventKind.Connected);
            }
        }
        finally
        {
            server.Dispose();
            foreach (TcpClient client in clients) client.Close();
        }
    }

    static void ConnectionFailureAndCancellation()
    {
        using (var missing = new NetworkTransport())
        {
            missing.Connect("127.0.0.1", FreePort());
            Assert(Next(missing, TransportEventKind.Error).PeerId == 0, "Connection failure event");
        }
        int port = FreePort();
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            for (int i = 0; i < 12; i++)
            {
                var pending = new NetworkTransport();
                pending.Connect("127.0.0.1", port);
                pending.Dispose();
                TransportEvent evt;
                Assert(!pending.TryPoll(out evt), "Disposed transport has no stale events");
            }
        }
        finally { listener.Stop(); }
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    static void RejectUntrustedTls()
    {
        using (RSA key = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using (X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10)))
            {
                int port = FreePort();
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                Task server = Task.Run(async () =>
                {
                    using (TcpClient raw = await listener.AcceptTcpClientAsync())
                    using (var secure = new SslStream(raw.GetStream(), false))
                    {
                        try { await secure.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false); }
                        catch (Exception) { } // The client intentionally rejects this certificate.
                    }
                });
                try
                {
                    using (var client = new NetworkTransport())
                    {
                        client.Connect("localhost", port, true);
                        TransportEvent evt = Next(client, TransportEventKind.Error);
                        Assert(evt.Payload.Contains("connect"), "TLS trust rejection is reported as a connection error");
                    }
                    Assert(server.Wait(5000), "TLS rejection closes the server handshake");
                }
                finally { listener.Stop(); }
            }
        }
    }

    static void TlsFailureAndCancellation()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            using (var client = new NetworkTransport())
            {
                client.Connect("localhost", port, true);
                using (TcpClient raw = AcceptWithin(listener))
                {
                    ReadClientHello(raw);
                    byte[] plaintext = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n");
                    raw.GetStream().Write(plaintext, 0, plaintext.Length);
                    Assert(Next(client, TransportEventKind.Error).Payload.Contains("connect"), "Plaintext is never accepted as TLS");
                    WaitClosed(raw);
                }
            }

            using (var client = new NetworkTransport())
            {
                var elapsed = Stopwatch.StartNew();
                client.Connect("localhost", port, true);
                using (TcpClient raw = AcceptWithin(listener))
                {
                    ReadClientHello(raw);
                    TransportEvent error = Next(client, TransportEventKind.Error, 12000);
                    Assert(error.Payload.Contains("timed out"), "A stalled TLS handshake reports timeout");
                    Assert(elapsed.ElapsedMilliseconds >= 7000 && elapsed.ElapsedMilliseconds < 12000, "TLS obeys the connection deadline");
                    WaitClosed(raw);
                }
            }

            using (var client = new NetworkTransport())
            {
                client.Connect("localhost", port, true);
                using (TcpClient raw = AcceptWithin(listener))
                {
                    ReadClientHello(raw);
                    var elapsed = Stopwatch.StartNew();
                    client.Dispose();
                    Assert(elapsed.ElapsedMilliseconds < 1000, "Cancelling TLS never blocks the game thread");
                    WaitClosed(raw);
                    TransportEvent evt;
                    Assert(!client.TryPoll(out evt), "Cancelled TLS has no stale connection or error event");
                }
            }
            Assert(!listener.Pending(), "TLS errors must not reconnect in plaintext");
        }
        finally { listener.Stop(); }
    }

    static TcpClient AcceptWithin(TcpListener listener)
    {
        Task<TcpClient> pending = listener.AcceptTcpClientAsync();
        Assert(pending.Wait(5000), "Loopback TLS connection is accepted");
        return pending.Result;
    }

    static void ReadClientHello(TcpClient raw)
    {
        raw.ReceiveTimeout = 5000;
        NetworkStream stream = raw.GetStream();
        var header = new byte[5];
        ReadExactly(stream, header);
        Assert(header[0] == 22 && header[1] == 3, "First record is a TLS handshake");
        int length = (header[3] << 8) | header[4];
        Assert(length > 0 && length <= 16384, "Valid TLS handshake record length");
        var payload = new byte[length];
        ReadExactly(stream, payload);
        Assert(payload[0] == 1, "First TLS message is ClientHello");
    }

    static void ReadExactly(NetworkStream stream, byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            Assert(read > 0, "Complete TLS record");
            offset += read;
        }
    }

    static TcpClient RawConnect(int port)
    {
        var client = new TcpClient();
        client.NoDelay = true;
        client.Connect(IPAddress.Loopback, port);
        return client;
    }

    static TransportEvent Next(NetworkTransport transport, TransportEventKind expected, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        TransportEvent evt;
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (transport.TryPoll(out evt))
            {
                Assert(evt.Kind == expected, "Expected " + expected + ", got " + evt.Kind + ": " + evt.Payload);
                return evt;
            }
            Thread.Sleep(5);
        }
        throw new Exception("Timed out waiting for " + expected);
    }

    static void WaitClosed(TcpClient client)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 5000)
        {
            if (client.Client.Poll(1000, SelectMode.SelectRead) && client.Available == 0) return;
            Thread.Sleep(5);
        }
        throw new Exception("Peer socket did not close.");
    }

    static byte[] Header(int size)
    {
        return new byte[] { (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24) };
    }

    static byte[] Frame(byte[] payload)
    {
        var bytes = new byte[payload.Length + 4];
        Buffer.BlockCopy(Header(payload.Length), 0, bytes, 0, 4);
        Buffer.BlockCopy(payload, 0, bytes, 4, payload.Length);
        return bytes;
    }

    static void Assert(bool value, string description)
    {
        if (!value) throw new Exception("Transport test failed: " + description);
    }
}

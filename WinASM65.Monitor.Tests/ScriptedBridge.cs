using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace WinASM65.Monitor.Tests
{
    /// <summary>
    /// A bridge on a real socket whose answers are scripted and which can send
    /// unsolicited PUSH lines, the one thing <see cref="Protocol.ProtocolServer"/>
    /// cannot do. Unscripted commands are answered by <see cref="Answer"/>, which
    /// defaults to a Mesen2-shaped host that knows PING and STATUS.
    /// </summary>
    public sealed class ScriptedBridge : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private readonly object _sendLock = new object();
        private TcpClient _client;
        private StreamWriter _writer;
        private readonly ManualResetEventSlim _connected = new ManualResetEventSlim();
        private volatile bool _disposed;

        /// <summary>Every command line received, in order.</summary>
        public BlockingCollection<string> Received { get; } = new BlockingCollection<string>();

        /// <summary>Answers a command; null closes nothing and sends "ERR unknown command".</summary>
        public Func<string, string> Answer { get; set; }

        public string Status { get; set; } = "OK running";

        public ScriptedBridge()
        {
            Answer = DefaultAnswer;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _thread = new Thread(Serve) { IsBackground = true, Name = "scripted-bridge" };
            _thread.Start();
        }

        public int Port
        {
            get { return ((IPEndPoint)_listener.LocalEndpoint).Port; }
        }

        private string DefaultAnswer(string line)
        {
            string verb = line.Split(' ')[0].ToUpperInvariant();
            switch (verb)
            {
                case "PING": return "OK Mesen2 2.1.1";
                case "STATUS": return Status;
                case "CPU": return "OK pc=$C000 a=00 x=00 y=00 sp=FD ps=24 cycles=7";
                case "READ":
                    {
                        string[] parts = line.Split(' ');
                        int length = int.Parse(parts[2]);
                        return "OK " + new string('0', length * 2);
                    }
                default: return "OK";
            }
        }

        /// <summary>Sends one unsolicited line, without the PUSH prefix.</summary>
        public void Push(string text)
        {
            Assert(_connected.Wait(5000), "no monitor connected to push to");
            lock (_sendLock)
                _writer.Write("PUSH " + text + "\n");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private void Serve()
        {
            try
            {
                _client = _listener.AcceptTcpClient();
                NetworkStream stream = _client.GetStream();
                StreamReader reader = new StreamReader(stream, Encoding.ASCII);
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                _connected.Set();

                string line;
                while (!_disposed && (line = reader.ReadLine()) != null)
                {
                    Received.Add(line);
                    string reply = Answer(line) ?? "ERR unknown command: " + line;
                    lock (_sendLock)
                        _writer.Write(reply + "\n");
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        /// <summary>Waits for a received command that starts with <paramref name="prefix"/>.</summary>
        public string WaitFor(string prefix, int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                string line;
                if (left <= 0 || !Received.TryTake(out line, left))
                    return null;
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            try { _client?.Close(); } catch { }
            try { _listener.Stop(); } catch { }
            _connected.Dispose();
        }
    }
}

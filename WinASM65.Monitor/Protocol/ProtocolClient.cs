using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinASM65.Monitor.Protocol
{
    /// <summary>
    /// One side of the bridge protocol: a socket to a monitor host, and the
    /// discipline that keeps its two directions from colliding.
    ///
    /// The protocol is request-response on one line, but the host also sends
    /// unsolicited <c>PUSH RAM</c> lines while the machine runs. A client that
    /// read replies in the same thread that sends would interleave the two:
    /// a push arriving between a request and its answer would be mistaken for
    /// the answer, and the next request would be answered with the push after
    /// it. The split here is the answer to that: one thread reads everything
    /// the host sends and sorts it into replies and pushes, and the thread
    /// that sends waits on the queue for the reply it asked for.
    ///
    /// The receiver thread is the only one that touches the socket, so a
    /// <see cref="NetworkStream"/> is never read by two threads at once and
    /// a half-written line cannot be left in the buffer for the next read to
    /// find. The sender writes directly; a TCP socket is full-duplex and the
    /// host answers in order, so a send cannot corrupt a reply.
    /// </summary>
    public sealed class ProtocolClient : IDisposable
    {
        private const int ReadTimeoutMs = 30000;

        private readonly NetworkStream _stream;
        private readonly StreamReader _reader;
        private readonly object _queueLock = new object();
        private readonly Queue<string> _responses = new Queue<string>();
        private readonly Thread _receiver;
        private volatile bool _disposed;
        private volatile bool _receiverExited;

        /// <summary>
        /// Fired on the receiver thread for every unsolicited <c>PUSH RAM</c>
        /// line. The payload is the part after the verb, and the caller
        /// decides what to do with it — applying it to a cached dump, or
        /// dropping it when the machine is stopped.
        /// </summary>
        public event Action<string> PushReceived;

        /// <summary>
        /// True when the socket is still open and the receiver thread has not
        /// exited. Read by the backend so a dead bridge shows as
        /// "disconnected" in the status line rather than as a host that
        /// never existed.
        /// </summary>
        public bool IsConnected
        {
            get { return !_disposed && !_receiverExited; }
        }

        private ProtocolClient(NetworkStream stream)
        {
            _stream = stream;
            _stream.ReadTimeout = ReadTimeoutMs;
            _reader = new StreamReader(_stream, Encoding.ASCII, leaveOpen: true);

            _receiver = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "monitor-receiver"
            };
            _receiver.Start();
        }

        /// <summary>
        /// Opens a client against a host already listening on the given port.
        /// </summary>
        public static ProtocolClient Connect(int port)
        {
            return Connect(port, 5000);
        }

        /// <summary>
        /// Opens a client against a host already listening on the given port,
        /// failing after <paramref name="timeoutMs"/> if it does not accept.
        /// </summary>
        public static ProtocolClient Connect(int port, int timeoutMs)
        {
            TcpClient client = new TcpClient();
            try
            {
                Task connectTask = client.ConnectAsync("127.0.0.1", port);
                try
                {
                    if (!connectTask.Wait(timeoutMs))
                        throw new MonitorException("the bridge did not accept within " + timeoutMs + " ms");
                }
                catch (AggregateException ex)
                {
                    // ConnectAsync wraps a refused connection in an
                    // AggregateException, where the APM pair this replaced
                    // threw the SocketException itself. Unwrapped, the caller
                    // retries on a SocketException and names the endpoint
                    // when its deadline passes; wrapped, the same refusal
                    // reaches the user as "One or more errors occurred." —
                    // a message that names neither the bridge nor the port.
                    throw ex.InnerException ?? ex;
                }

                return new ProtocolClient(client.GetStream());
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Sends a command line and blocks for the reply.
        ///
        /// The host answers in order, so the reply is the first line the
        /// receiver has set aside for this client. A push that arrived in
        /// between is fired as <see cref="PushReceived"/> and is not the
        /// reply, even though it arrived first.
        /// </summary>
        public string Send(string line)
        {
            if (_disposed)
                throw new MonitorException("the bridge is closed");

            byte[] bytes = Encoding.ASCII.GetBytes(line + "\n");
            _stream.Write(bytes, 0, bytes.Length);

            string reply;
            lock (_queueLock)
            {
                while (_responses.Count == 0 && !_receiverExited)
                    System.Threading.Monitor.Wait(_queueLock);

                if (_responses.Count == 0)
                {
                    throw new MonitorException(
                        "the bridge closed before answering " + line);
                }

                reply = _responses.Dequeue();
            }

            return reply;
        }

        /// <summary>
        /// Reads everything the host sends and sorts it into replies and
        /// pushes. Runs on its own thread for the life of the client.
        /// </summary>
        private void ReceiveLoop()
        {
            try
            {
                while (!_disposed)
                {
                    string line = _reader.ReadLine();
                    if (line == null)
                        break;

                    // The host sends exactly one line per command, in order.
                    // Anything else is a push, and a push is never an answer.
                    if (line.StartsWith(MonitorProtocol.PrefixPush, StringComparison.Ordinal))
                    {
                        string payload = line.Substring(MonitorProtocol.PrefixPush.Length).Trim();
                        PushReceived?.Invoke(payload);
                        continue;
                    }

                    lock (_queueLock)
                    {
                        _responses.Enqueue(line);
                        System.Threading.Monitor.Pulse(_queueLock);
                    }
                }
            }
            catch (IOException)
            {
                // The host went away. A pending send will observe the closed
                // socket; nothing here has to do except stop reading.
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _receiverExited = true;
            }
        }

        /// <summary>
        /// The part of a reply after the verb, or the empty string when the
        /// reply is a bare <c>OK</c>.
        /// </summary>
        public static string PayloadOf(string response)
        {
            if (response == null)
                return string.Empty;

            int space = response.IndexOf(' ');
            if (space < 0)
                return string.Empty;

            return response.Substring(space + 1);
        }

        /// <summary>
        /// True when the reply is an <c>OK</c>, with or without a payload.
        ///
        /// A bare <c>OK</c> and <c>OK MesenCE 0.0.0</c> are both successes; the
        /// payload is what follows the verb. The check is a prefix rather than
        /// an equality, because the verb is the only part the caller controls
        /// and the payload is the host's business.
        /// </summary>
        public static bool IsOk(string response)
        {
            return response != null
                && response.StartsWith(MonitorProtocol.OkPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The message after an <c>ERR</c> prefix, or null when the reply is
        /// not an error.
        /// </summary>
        public static string ErrorOf(string response)
        {
            if (response == null)
                return null;

            if (!response.StartsWith(MonitorProtocol.ErrPrefix, StringComparison.Ordinal))
                return null;

            return response.Substring(MonitorProtocol.ErrPrefix.Length).Trim();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Wake a sender that is waiting on the queue so it sees the
            // closure rather than blocking forever.
            lock (_queueLock)
            {
                System.Threading.Monitor.PulseAll(_queueLock);
            }

            _receiver.Join(2000);
            _reader.Dispose();
            _stream.Dispose();
        }
    }
}
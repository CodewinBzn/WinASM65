using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace WinASM65.Monitor.Protocol
{
    /// <summary>
    /// Client side of the protocol, on the monitor. Sends one line, reads one
    /// response.
    ///
    /// Deliberately synchronous: a monitor is a dialogue tool, not a server. The
    /// only possible concurrency is with the emulated machine, and that is the
    /// backend's job, not the transport's.
    /// </summary>
    public sealed class ProtocolClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private ProtocolClient(TcpClient client, StreamReader reader, StreamWriter writer)
        {
            _client = client;
            _reader = reader;
            _writer = writer;
        }

        /// <summary>Connects to the bridge and waits for its banner.</summary>
        public static ProtocolClient Connect(int port, int timeoutMs = 5000)
        {
            TcpClient client = new TcpClient();
            IAsyncResult pending = client.BeginConnect("127.0.0.1", port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                client.Close();
                throw new MonitorException("Bridge connection refused or too slow (port " + port + ").");
            }
            client.EndConnect(pending);
            client.NoDelay = true;

            NetworkStream stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            return new ProtocolClient(
                client,
                new StreamReader(stream, Encoding.ASCII),
                new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true });
        }

        /// <summary>Sends a raw command and returns the response line.</summary>
        public string Send(string command)
        {
            _writer.WriteLine(command);
            string response = _reader.ReadLine();
            if (response == null)
                throw new MonitorException("The bridge closed the connection on: " + command);
            return response;
        }

        public bool IsOk(string response)
        {
            // A bare "OK" is a valid success: a command with no payload (PAUSE,
            // WRITE) returns nothing more. Testing for an "OK " prefix would report
            // those responses as failures.
            return response == MonitorProtocol.OkPrefix
                || (response != null && response.StartsWith(MonitorProtocol.OkPrefix + " "));
        }

        /// <summary>Error message extracted from a response, or null if it succeeded.</summary>
        public static string ErrorOf(string response)
        {
            if (response == null)
                return "null response";
            if (response.StartsWith(MonitorProtocol.ErrPrefix + " "))
                return response.Substring(MonitorProtocol.ErrPrefix.Length + 1);
            return null;
        }

        /// <summary>Payload of a successful response, without the prefix.</summary>
        public static string PayloadOf(string response)
        {
            if (response == MonitorProtocol.OkPrefix)
                return string.Empty;
            if (response != null && response.StartsWith(MonitorProtocol.OkPrefix + " "))
                return response.Substring(MonitorProtocol.OkPrefix.Length + 1);
            return response == null ? string.Empty : response;
        }

        public void Dispose()
        {
            _writer.Dispose();
            _reader.Dispose();
            _client.Close();
        }
    }
}
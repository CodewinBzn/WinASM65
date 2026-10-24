using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor
{
    /// <summary>Connects to a bridge that may still be starting.</summary>
    public static class BridgeConnector
    {
        public const int DefaultPort = 45678;
        public const int DefaultTimeoutMs = 15000;
        private const int RetryMs = 250;

        /// <summary>
        /// Retries the connection: the bridge needs a moment after the ROM loads
        /// before it listens, and failing on the first refused attempt would make
        /// launching Mesen from here unusable.
        /// </summary>
        public static BridgeMemoryBackend Connect(int port, int timeoutMs = DefaultTimeoutMs, bool poll = true)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                try
                {
                    return new BridgeMemoryBackend(ProtocolClient.Connect(port), port, poll);
                }
                catch (MonitorException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new MonitorException(
                            "No bridge answered on 127.0.0.1:" + port + " within "
                            + (timeoutMs / 1000) + "s.");
                }
                catch (SocketException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new MonitorException(
                            "No bridge answered on 127.0.0.1:" + port + " within "
                            + (timeoutMs / 1000) + "s.");
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new MonitorException(
                            "The bridge on 127.0.0.1:" + port + " closed the connection during startup.");
                }

                Thread.Sleep(RetryMs);
            }
        }
        /// <summary>
        /// Connects, or returns null so the monitor can start without a bridge.
        /// The reason is still written to stderr: a monitor that opens in degraded
        /// mode without saying which address it tried leaves the user guessing.
        /// </summary>
        public static IMemoryBackend TryConnect(int port)
        {
            try
            {
                return Connect(port);
            }
            catch (MonitorException e)
            {
                Console.Error.WriteLine(e.Message + " Starting without a bridge.");
                return null;
            }
            catch (SocketException e)
            {
                Console.Error.WriteLine("No bridge on 127.0.0.1:" + port + ": " + e.Message
                    + " Starting without a bridge.");
                return null;
            }
        }
    }
}

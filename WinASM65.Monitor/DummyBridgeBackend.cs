using System;
using System.Collections.Generic;
namespace WinASM65.Monitor
{
    /// <summary>
    /// Minimal fallback backend used when no bridge is available.
    /// Implements the same contract as the real BridgeMemoryBackend but provides
    /// no operational functionality. It allows the UI to start in a degraded
    /// mode so that the monitor can be launched without a bridge.
    /// </summary>
    public class DummyBridgeBackend : IMemoryBackend, IReconnectable
    {
        public string EmulatorName => "Dummy";
        public string EmulatorVersion => "0.0";
        public bool IsRunning => false;
        public bool IsConnected => false;
        public string RamRefreshMode => null;
        public event Action<IReadOnlyList<(int Address, byte Value)>> RamChanged;

        public void Dispose() { /* nothing to dispose */ }
        public byte[] Read(int address, int length)
        {
            // Return zero-filled buffer; no bounds checking needed for dummy.
            return new byte[length];
        }
        public void Write(int address, byte[] bytes) { /* ignore */ }
        public void Pause() { }
        public void Resume() { }
        public void Step() { }
        public void Reset() { }
        public void AddBreakpoint(int address, string kind) { }
        public void ClearBreakpoints() { }
        public byte[] SaveState() => Array.Empty<byte>();
        public void LoadState(byte[] state) { /* ignore */ }
        public void Reconnect() { /* dummy does not reconnect */ }
    }
}


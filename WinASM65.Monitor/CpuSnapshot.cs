using System;
using System.Globalization;

namespace WinASM65.Monitor
{
    /// <summary>
    /// CPU registers and the cycle counter, as read from the emulator.
    ///
    /// This type exists for one reason: to tell a cold machine from a broken read.
    ///
    /// MesenCE 2.2.1 exposes no way to advance the emulated CPU from a Lua script.
    /// The emu table holds 64 entries and none of them runs, ticks or steps the
    /// machine, so a bridge that holds the execution slot freezes emulation. The
    /// observable consequence is a cycle count that never moves and a program
    /// counter stuck at the reset vector, with all of RAM reading as zero.
    ///
    /// That state is indistinguishable from a broken bridge if all you can see is
    /// memory. Seeing the cycle counter makes it distinguishable, which is the whole
    /// point: the monitor can state the limitation instead of looking for a fault.
    /// </summary>
    public sealed class CpuSnapshot
    {
        public int Pc { get; private set; }
        public int A { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Sp { get; private set; }
        public int Ps { get; private set; }
        public long CycleCount { get; private set; }

        public CpuSnapshot(int pc, int a, int x, int y, int sp, int ps, long cycleCount)
        {
            Pc = pc;
            A = a;
            X = x;
            Y = y;
            Sp = sp;
            Ps = ps;
            CycleCount = cycleCount;
        }

        /// <summary>
        /// A machine left at the reset vector, within a handful of cycles of power on,
        /// is not running. Reporting it as such is more useful than reporting a
        /// plausible sounding value, because the user's next question is always
        /// "why is RAM empty" and the answer has to be visible here.
        /// </summary>
        public bool LooksPoweredDown
        {
            get { return CycleCount <= 16 && Pc == 0x8000; }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "PC=${0:X4}  A={1:X2}  X={2:X2}  Y={3:X2}  SP={4:X2}  PS={5:X2}  cycles={6}",
                Pc, A, X, Y, Sp, Ps, CycleCount);
        }
    }

    /// <summary>
    /// Implemented by backends that can report the CPU state.
    ///
    /// Declared separately from <see cref="IMemoryBackend"/> on purpose: adding a
    /// member to that interface would force every backend, including the fake one the
    /// tests run against, to pretend it can observe a CPU it does not have. A test
    /// backend has no processor, and claiming otherwise would be exactly the kind of
    /// fiction this project refuses.
    /// </summary>
    public interface ICpuStateSource
    {
        CpuSnapshot ReadCpuState();
    }

    /// <summary>
    /// Implemented by backends that can describe the loaded cartridge.
    ///
    /// The mapper decides how the address space is decoded, so a monitor reading
    /// $8000 without knowing the mapper is reading a window that may not be the
    /// one the mapper selects. Reporting it is what makes a raw memory view
    /// trustworthy.
    /// </summary>
    public interface IRomInfoSource
    {
        string ReadRomInfo();
    }
}
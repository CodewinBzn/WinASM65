using System;
using System.Collections.Generic;
using System.IO;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Monitor.Protocol;

namespace WinASM65.Monitor
{
    /// <summary>
    /// The units assembled and kept, with the commands that act on them.
    ///
    /// Kept apart from the machine on purpose. A machine changes — a bridge, a fake,
    /// a second emulator — and a unit does not: once a source has been assembled,
    /// the modules that come out of it are the same wherever they are placed. Putting
    /// the registry here means both ways of driving the monitor — the TCP server and
    /// the interactive session — place the same units, with one implementation.
    ///
    /// The commands answer in protocol form because that is what a user sees either
    /// way: the same <c>OK</c> or named <c>ERR</c>, whether it arrived over a socket
    /// or was typed.
    /// </summary>
    public sealed class UnitLibrary
    {
        private readonly Dictionary<string, RelocatableUnit> _units =
            new Dictionary<string, RelocatableUnit>(StringComparer.OrdinalIgnoreCase);
        private readonly IMemoryBackend _machine;
        private readonly string _directory;

        /// <summary>
        /// <paramref name="directory"/> is where a relative source path is resolved.
        /// It defaults to the current directory, so the monitor can be started from
        /// the project it watches.
        /// </summary>
        public UnitLibrary(IMemoryBackend machine, string directory)
        {
            _machine = machine;
            _directory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
        }

        public IReadOnlyList<RelocatableUnit> Units
        {
            get { return new List<RelocatableUnit>(_units.Values); }
        }

        /// <summary>
        /// Assembles a source once and keeps it. Nothing is written to the machine:
        /// what is kept is the set of modules, which is what makes the next placement
        /// possible without assembling again.
        /// </summary>
        public string Assemble(string source)
        {
            if (string.IsNullOrEmpty(source))
                return MonitorProtocol.ErrPrefix + " " + ("ASSEMBLE expects <source file>");

            string path = Path.IsPathRooted(source) ? source : Path.Combine(_directory, source);

            AssemblerOptions options = new AssemblerOptions();
            options.Cpu = CpuFactory.Create("6502");
            // A typo in a label is named now, while the user is looking at the file,
            // instead of becoming a relocation nothing can resolve later.
            options.ReportUndefinedSymbols = true;

            try
            {
                RelocatableUnit unit = RelocatableUnit.FromSource(path, options);
                _units[unit.Name] = unit;
// Same shape as Describe, so a user reading UNITS after ASSEMBLE sees the same
                // two fields rather than one spelled with a word and the other without.
                return MonitorProtocol.OkPrefix + " " + (unit.Name
                    + " $" + unit.NaturalOrigin.ToString("X4")
                    + " " + unit.Length + " octet(s)");
            }
            catch (MonitorException ex)
            {
                return MonitorProtocol.ErrPrefix + " " + (ex.Message);
            }
        }

        /// <summary>
        /// Relinks a kept unit at an address, writes it, and reads it back.
        ///
        /// The read-back is the point. A bridge that writes less than it says, or
        /// writes somewhere else, still answers OK; comparing what the machine holds
        /// with what the linker produced is the only evidence available that the
        /// routine really is at the address the caller named.
        /// </summary>
        public string Load(string name, int address)
        {
            RelocatableUnit unit;
            if (!_units.TryGetValue(name, out unit))
                return MonitorProtocol.ErrPrefix + " " + ("unknown unit: " + name + ". ASSEMBLE it first.");

            if (unit.Length > MonitorProtocol.MaxLoadLength)
                return MonitorProtocol.ErrPrefix + " " + ("unit too large for one load (max "
                    + MonitorProtocol.MaxLoadLength + "): " + unit.Length);

            LoadedBlock block;
            try
            {
                block = unit.PlaceAt(address);
            }
            catch (MonitorException ex)
            {
                return MonitorProtocol.ErrPrefix + " " + (ex.Message);
            }

            if (block.Address + block.Bytes.Length > AddressSpace.MaxAddress + 1)
                return MonitorProtocol.ErrPrefix + " " + ("block at $" + block.Address.ToString("X4")
                    + " + " + block.Bytes.Length + " runs past the address space");

            byte[] readBack;
            try
            {
                _machine.Write(block.Address, block.Bytes);
                readBack = _machine.Read(block.Address, block.Bytes.Length);
            }
            catch (MonitorException ex)
            {
                return MonitorProtocol.ErrPrefix + " " + (ex.Message);
            }

            for (int i = 0; i < readBack.Length; i++)
            {
                if (readBack[i] == block.Bytes[i])
                    continue;
                return MonitorProtocol.ErrPrefix + " " + ("read-back mismatch at $"
                    + (block.Address + i).ToString("X4") + ": wrote "
                    + block.Bytes[i].ToString("X2") + ", read " + readBack[i].ToString("X2"));
            }

            return MonitorProtocol.OkPrefix + " " + ("$" + block.Address.ToString("X4")
                + " " + block.Bytes.Length + " octet(s), " + block.Sites.Count + " site(s)");
        }

        public string Describe()
        {
            if (_units.Count == 0)
                return MonitorProtocol.OkPrefix + " " + ("no unit");

            List<string> lines = new List<string>();
            foreach (RelocatableUnit unit in _units.Values)
                lines.Add(unit.Name + " $" + unit.NaturalOrigin.ToString("X4") + " " + unit.Length + "o");
            lines.Sort(StringComparer.Ordinal);
            return MonitorProtocol.OkPrefix + " " + (string.Join(", ", lines.ToArray()));
        }
    }
}

// Abdelghani BOUZIANE / Refactored to Pure OOP
// WinASM65 - Binary Emitter and Memory Buffer (Pure OOP, SOLID)

using System;
using System.Collections.Generic;
using System.IO;

namespace WinASM65.Output
{
    public interface IBinaryEmitter
    {
        ushort CurrentAddress { get; set; }
        ushort OriginAddress { get; set; }
        int Length { get; }
        byte[] ToArray();
        void EmitByte(byte b);
        void EmitWord(ushort w);
        void EmitBytes(byte[] bytes);
        void PatchByte(int position, byte b);
        void PatchWord(int position, ushort w);
        void PatchBytes(int position, byte[] bytes);
        void SaveToFile(string filePath);
        void Reset();

        /// <summary>Relocation sites recorded so far, in emission order.</summary>
        IReadOnlyList<RelocationRecord> Relocations { get; }

        /// <summary>Index of the segment currently being emitted. Direct-burn mode only ever has one.</summary>
        int SegmentIndex { get; set; }

        /// <summary>Name of the segment currently being emitted. Empty in direct-burn mode.</summary>
        string SegmentName { get; set; }

        /// <summary>
        /// Where each <c>.org</c> landed, as (buffer offset, address) pairs in emission
        /// order. Direct-burn mode ignores this entirely; it exists so a unit with
        /// several <c>.org</c> can be cut back into one segment per origin instead of
        /// being published as a single buffer with a single, wrong, origin.
        /// </summary>
        IReadOnlyList<EmitterOrigin> Origins { get; }

        /// <summary>Records a site whose emitted value depends on a symbol.</summary>
        void RecordRelocation(RelocationRecord record);

        /// <summary>Looks a recorded site up by its segment and segment-relative offset.</summary>
        bool TryGetRelocation(int segmentIndex, int offset, out RelocationRecord record);

        /// <summary>Marks a recorded site as resolved with the value written into it.</summary>
        void ResolveRelocation(int segmentIndex, int offset, long value);
    }

    /// <summary>
    /// Where a <c>.org</c> put the emission cursor: the buffer offset it started from,
    /// and the address it set. Kept as a pair because neither half is enough -- the
    /// offset says which bytes belong to that origin, the address says what they are
    /// addresses of.
    /// </summary>
    public struct EmitterOrigin
    {
        public int BufferOffset { get; private set; }
        public ushort Address { get; private set; }

        public EmitterOrigin(int bufferOffset, ushort address)
        {
            BufferOffset = bufferOffset;
            Address = address;
        }
    }

    public class BinaryEmitter : IBinaryEmitter
    {
        private readonly List<byte> _buffer = new List<byte>();
        private readonly List<RelocationRecord> _relocations = new List<RelocationRecord>();
        private readonly List<EmitterOrigin> _origins = new List<EmitterOrigin>();

        private ushort _currentAddress;
        private ushort _originAddress;

        public ushort CurrentAddress
        {
            get { return _currentAddress; }
            set { _currentAddress = value; }
        }

        /// <summary>
        /// Setting the origin opens a new block. Recording it here rather than in the
        /// <c>.org</c> directive means every path that moves the origin is recorded,
        /// including the default origin applied before the first line.
        /// </summary>
        public ushort OriginAddress
        {
            get { return _originAddress; }
            set
            {
                _originAddress = value;
                _origins.Add(new EmitterOrigin(_buffer.Count, value));
            }
        }

        public IReadOnlyList<EmitterOrigin> Origins
        {
            get { return _origins; }
        }

        public int SegmentIndex { get; set; }
        public string SegmentName { get; set; }

        public IReadOnlyList<RelocationRecord> Relocations
        {
            get { return _relocations; }
        }

        public int Length
        {
            get { return _buffer.Count; }
        }

        public byte[] ToArray()
        {
            return _buffer.ToArray();
        }

        public void EmitByte(byte b)
        {
            _buffer.Add(b);
            CurrentAddress++;
        }

        public void EmitWord(ushort w)
        {
            _buffer.Add((byte)(w & 0xFF));
            _buffer.Add((byte)((w >> 8) & 0xFF));
            CurrentAddress += 2;
        }

        public void EmitBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return;
            _buffer.AddRange(bytes);
            CurrentAddress += (ushort)bytes.Length;
        }

        public void PatchByte(int position, byte b)
        {
            if (position >= 0 && position < _buffer.Count)
            {
                _buffer[position] = b;
            }
        }

        public void PatchWord(int position, ushort w)
        {
            PatchBytes(position, new byte[] { (byte)(w & 0xFF), (byte)((w >> 8) & 0xFF) });
        }

        public void PatchBytes(int position, byte[] bytes)
        {
            if (bytes == null || position < 0 || position + bytes.Length > _buffer.Count)
                return;

            _buffer.RemoveRange(position, bytes.Length);
            _buffer.InsertRange(position, bytes);
        }

        public void SaveToFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(filePath, _buffer.ToArray());
        }

        public void Reset()
        {
            _buffer.Clear();
            _relocations.Clear();
            _origins.Clear();
            CurrentAddress = 0;
            _originAddress = 0;
            SegmentIndex = 0;
            SegmentName = string.Empty;
        }

        public void RecordRelocation(RelocationRecord record)
        {
            if (record == null)
                return;
            _relocations.Add(record);
        }

        public bool TryGetRelocation(int segmentIndex, int offset, out RelocationRecord record)
        {
            for (int i = 0; i < _relocations.Count; i++)
            {
                if (_relocations[i].SegmentIndex == segmentIndex && _relocations[i].Offset == offset)
                {
                    record = _relocations[i];
                    return true;
                }
            }
            record = null;
            return false;
        }

        public void ResolveRelocation(int segmentIndex, int offset, long value)
        {
            RelocationRecord record;
            if (TryGetRelocation(segmentIndex, offset, out record))
                record.MarkResolved(value);
        }
    }
}

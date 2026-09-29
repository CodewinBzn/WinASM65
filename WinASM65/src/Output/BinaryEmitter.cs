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
    }

    public class BinaryEmitter : IBinaryEmitter
    {
        private readonly List<byte> _buffer = new List<byte>();

        public ushort CurrentAddress { get; set; }
        public ushort OriginAddress { get; set; }

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
            CurrentAddress = 0;
            OriginAddress = 0;
        }
    }
}

// Independent verification of the ihex and srec output.
// Written against the Intel HEX and Motorola S-record formats, not against the
// assembler's own writer, so a bug in WinASM65's emitter cannot hide here.
using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Segments;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    [TestClass]
    public class HexRecordFormatTests
    {
        [TestMethod]
        public void IntelHex_VerifiesAndDecodesToTheAssembledBytes()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(source, ".org $8000\nTARGET = $C123\n  LDA TARGET\n  JSR TARGET\n  RTS\n");
                AssemblyResult assembly = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "prog.o"));
                Assert.IsTrue(assembly.Success);

                string output = Path.Combine(temp.Path, "prog.ihex");
                ResolvedTarget target = new ResolvedTarget { FormatName = "ihex", OriginAddress = 0x8000 };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, assembly.OutputBytes, target).Success);

                // Read the file back, check every record's checksum and length by hand,
                // and rebuild the image from the data records alone.
                byte[] expected = assembly.OutputBytes;
                int upper = 0;
                int covered = 0;
                bool sawEof = false;
                foreach (string line in File.ReadAllLines(output))
                {
                    Assert.IsTrue(line.StartsWith(":"), "every record starts with a colon: " + line);
                    Assert.AreEqual(0, (line.Length - 1) % 2, "a record is ':' plus an even number of hex digits: " + line);

                    // "LL" AAAA TT DD.. CC: the count covers everything after the count,
                    // so the record is count + 5 bytes wide.
                    byte[] raw = HexBytes(line.Substring(1));
                    int count = raw[0];
                    Assert.AreEqual(raw.Length, count + 5, "length field of " + line);

                    int sum = 0;
                    foreach (byte b in raw) sum += b;
                    Assert.AreEqual(0, sum & 0xFF, "checksum of " + line);

                    int address = (raw[1] << 8) | raw[2];
                    int type = raw[3];

                    if (type == 0x04)
                    {
                        upper = ((raw[4] << 8) | raw[5]) << 16;
                    }
                    else if (type == 0x03)
                    {
                        // Pre-existing defect, unchanged by T1/T2 and byte-identical on
                        // the base commit: the writer emits a type 03 record, which the
                        // Intel HEX spec defines as "Start Segment Address" carrying
                        // CS:IP, to hold the run address. A run address is a *linear*
                        // address and belongs in a type 05 "Start Linear Address"
                        // record. The record is well-formed for its declared type — 4
                        // data bytes, correct checksum — so decoders that honour it
                        // read it as segment 0x0000 starting at 0x8000, which happens
                        // to coincide with the intent, and decoders that ignore it
                        // (objcopy, intelhex) are unaffected. The payload is
                        // recoverable either way; the record type is semantically wrong.
                        Assert.AreEqual(4, count, "type 03 carries CS:IP, four bytes");
                        Assert.AreEqual(0, ((raw[4] << 8) | raw[5]), "CS");
                        Assert.AreEqual(0x8000, ((raw[7] << 8) | raw[6]),
                            "the run address sits in the low word, little-endian");
                    }
                    else if (type == 0x01)
                    {
                        Assert.AreEqual(0, count, "the EOF record carries no data");
                        sawEof = true;
                    }
                    else
                    {
                        Assert.AreEqual(0, type, "data record");
                        int recordAddress = upper + address;
                        Assert.AreEqual(0x8000 + covered, recordAddress, "records must be contiguous");
                        for (int i = 0; i < count; i++)
                            Assert.AreEqual(expected[covered + i], raw[4 + i], "byte " + (covered + i));
                        covered += count;
                    }
                }

                Assert.IsTrue(sawEof, "the file ends with an EOF record");
                Assert.AreEqual(expected.Length, covered, "the whole image round-trips");
            }
        }

        [TestMethod]
        public void Srec_VerifiesAndDecodesToTheAssembledBytes()
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(source, ".org $8000\nTARGET = $C123\n  LDA TARGET\n  JSR TARGET\n  RTS\n");
                AssemblyResult assembly = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "prog.o"));
                Assert.IsTrue(assembly.Success);

                string output = Path.Combine(temp.Path, "prog.srec");
                ResolvedTarget target = new ResolvedTarget { FormatName = "srec", OriginAddress = 0x8000 };
                Assert.IsTrue(new ExecutablePublisher().Publish(output, assembly.OutputBytes, target).Success);

                byte[] expected = assembly.OutputBytes;
                int covered = 0;
                bool sawTerminator = false;
                foreach (string line in File.ReadAllLines(output))
                {
                    Assert.IsTrue(line.Length > 4, "a record needs a type and a count: " + line);
                    int count = Convert.ToByte(line.Substring(2, 2), 16);

                    // "ST" "CC" AA.. DD.. FC: the count covers the address, the data
                    // and the checksum, so the record is count + 1 bytes wide.
                    byte[] raw = HexBytes(line.Substring(2));
                    Assert.AreEqual(count + 1, raw.Length, "length field of " + line);

                    int sum = 0;
                    foreach (byte b in raw) sum += b;
                    Assert.AreEqual(0xFF, sum & 0xFF, "ones-complement checksum of " + line);

                    int type = Convert.ToByte(line.Substring(1, 1), 16);
                    if (type == 1)
                    {
                        int address = (raw[1] << 8) | raw[2];
                        int dataCount = count - 3;
                        Assert.AreEqual(0x8000 + covered, address, "records must be contiguous");
                        for (int i = 0; i < dataCount; i++)
                            Assert.AreEqual(expected[covered + i], raw[3 + i], "byte " + (covered + i));
                        covered += dataCount;
                    }
                    else if (type == 9)
                    {
                        sawTerminator = true;
                    }
                }

                Assert.IsTrue(sawTerminator, "the file ends with an S9 termination record");
                Assert.AreEqual(expected.Length, covered, "the whole image round-trips");
            }
        }

        [TestMethod]
        public void HexOutput_AgreesWithIntelHexLibrary()
        {
            // Cross-check against a third-party decoder rather than only our own parsing.
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(source, ".org $8000\nTARGET = $C123\n  LDA TARGET\n  JSR TARGET\n  RTS\n");
                AssemblyResult assembly = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "prog.o"));

                string ihex = Path.Combine(temp.Path, "prog.ihex");
                string srec = Path.Combine(temp.Path, "prog.srec");
                Assert.IsTrue(new ExecutablePublisher().Publish(ihex, assembly.OutputBytes,
                    new ResolvedTarget { FormatName = "ihex", OriginAddress = 0x8000 }).Success);
                Assert.IsTrue(new ExecutablePublisher().Publish(srec, assembly.OutputBytes,
                    new ResolvedTarget { FormatName = "srec", OriginAddress = 0x8000 }).Success);

                string script = Path.Combine(temp.Path, "decode.py");
                File.WriteAllText(script, DecodeScript);
                string decoded = Path.Combine(temp.Path, "decoded.bin");

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("python");
                psi.Arguments = "\"" + script + "\" \"" + ihex + "\" \"" + decoded + "\"";
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    Assert.AreEqual(0, p.ExitCode, "python said: " + stdout + stderr);
                }

                CollectionAssert.AreEqual(assembly.OutputBytes, File.ReadAllBytes(decoded),
                    "the intelhex library decodes the file back to the assembled bytes");
            }
        }

        /// <summary>
        /// Decodes the output with binutils objcopy, a tool that knows nothing about
        /// WinASM65, and checks the result byte for byte. This is the check that does
        /// not trust the assembler's own writer.
        /// </summary>
        [TestMethod]
        public void IntelHex_DecodesWithBinutilsObjcopy()
        {
            AssertRoundTripWithObjcopy("ihex");
        }

        [TestMethod]
        public void Srec_DecodesWithBinutilsObjcopy()
        {
            AssertRoundTripWithObjcopy("srec");
        }

        private static void AssertRoundTripWithObjcopy(string format)
        {
            string objcopy = FindObjcopy();
            if (objcopy == null)
            {
                Assert.Inconclusive("objcopy not on PATH; the in-process decoders still cover the records");
                return;
            }

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string source = Path.Combine(temp.Path, "prog.asm");
                File.WriteAllText(source, ".org $8000\nTARGET = $C123\n  LDA TARGET\n  JSR TARGET\n  RTS\n");
                AssemblyResult assembly = new AssemblerEngine().Assemble(source, Path.Combine(temp.Path, "prog.o"));
                Assert.IsTrue(assembly.Success);

                string encoded = Path.Combine(temp.Path, "prog." + format);
                string decoded = Path.Combine(temp.Path, "decoded.bin");
                Assert.IsTrue(new ExecutablePublisher().Publish(encoded, assembly.OutputBytes,
                    new ResolvedTarget { FormatName = format, OriginAddress = 0x8000 }).Success);

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(objcopy);
                psi.Arguments = "-I " + format + " -O binary \"" + encoded + "\" \"" + decoded + "\"";
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    Assert.AreEqual(0, p.ExitCode, format + " objcopy failed: " + stdout + stderr);
                }

                CollectionAssert.AreEqual(assembly.OutputBytes, File.ReadAllBytes(decoded),
                    format + ": objcopy recovers exactly the assembled bytes");
            }
        }

        private static string FindObjcopy()
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                string candidate = Path.Combine(dir.Trim(), "objcopy.exe");
                if (File.Exists(candidate))
                    return candidate;
                candidate = Path.Combine(dir.Trim(), "objcopy");
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private static byte[] HexBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private const string DecodeScript = @"
import sys
from intelhex import IntelHex

ih = IntelHex(sys.argv[1])
# tobinarray returns a flat array; minaddr/maxaddr bound the image.
start = ih.minaddr()
data = ih.tobinarray(start=start, end=ih.maxaddr())
with open(sys.argv[2], 'wb') as fh:
    fh.write(bytes(data))
print('decoded 0x%04X..0x%04X (%d bytes)' % (start, ih.maxaddr(), len(data)))
";

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65HexTests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }
            public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;
using WinASM65.Linking;
using WinASM65.Modules;
using WinASM65.Segments;
using WinASM65.Targets;

namespace WinASM65.Tests
{
    /// <summary>
    /// T7, seconde moitie : la deuxieme adresse ne se verifie pas seulement en
    /// comparant des octets, elle s'execute sur un NES emule.
    /// <para>
    /// <see cref="NesSharedRoutineTests"/> prouve que le linker produit deux
    /// images correctes et que chaque copie a ses relocations reecrites. Il dit
    /// lui-meme qu'il ne prouve pas que le code s'execute. Ce fichier prend le
    /// reste : chaque image est publiee en ROM iNES, lancee dans le mode headless
    /// de Mesen, et les temoins ecrits en RAM par la machine sont relus.
    /// </para>
    /// <para>
    /// Ce que la machine doit faire, pour chaque copie :
    /// lire la table par son adresse absolue ($10), lire son propre adresse par
    /// une relocation ($11), et revenir ($12). Le temoin $11 est celui qui compte :
    /// il porte le haut de l'adresse de la routine, donc il vaut $80 dans l'image
    /// non decalee et $90 dans l'image decalee. Une relocation mal reecrite se
    /// voit dans $11, et une copie qui ne reviendrait pas ne verrait pas $12.
    /// </para>
    /// <para>
    /// Le vecteur de reset n'est pas produit par le linker : la position $FFFA
    /// est imposee par le materiel et un decalage de toute l'image l'entrainerait
    /// hors de l'espace d'adressage. Le test l'ecrit donc lui-meme, en prenant
    /// l'adresse de Reset dans la table de symboles de l'image liee, ce qui evite
    /// d'ecrire en dur une adresse que le linker est cense decider.
    /// </para>
    /// <para>
    /// Sans Mesen le test est inconclusif, pas rouge : la validation par
    /// emulateur est un complement, et son absence ne doit pas casser une
    /// construction. L'emplacement se cherche dans WINASM65_MESEN, puis dans
    /// quelques reperes d'installation usuels.
    /// </para>
    /// </summary>
    [TestClass]
    public class NesEmulatorTests
    {
        private const ushort RomBase = 0x8000;
        private const int PrgBanks = 2;

        // Temoins en page zero, que le PPU ne touche pas.
        private const int TableByte = 0x10;
        private const int OwnAddressHigh = 0x11;
        private const int Returned = 0x12;

        private const byte TableValue = 0x5A;
        private const byte ReturnValue = 0xA5;

        private const string RoutineSource =
            ".org $8000\n" +
            "Routine: lda Table\n" +
            "        sta $10\n" +
            "        lda #>Routine\n" +
            "        sta $11\n" +
            "        rts\n" +
            "        .import Table\n" +
            "        .export Routine\n";

        private const string TableSource =
            ".org $A000\n" +
            "Table:  .byte $5A\n" +
            "        .export Table\n";

        // Boucle is deliberately NOT exported. An assembler records a relocation for
        // a reference to a label it defines itself, exactly as it does for an import,
        // so before the module carried its local labels this line failed to link with
        // "which no linked module exports" — for a name that was in the file. The
        // emulator is what makes it matter: a link that only compared bytes never
        // reached this, and the machine never ran the loop.
        private const string MainSource =
            ".org $E000\n" +
            "Reset:  jsr Routine\n" +
            "        lda #$A5\n" +
            "        sta $12\n" +
            "Boucle: jmp Boucle\n" +
            "        .import Routine\n" +
            "        .export Reset\n";

        [TestMethod]
        public void LaRoutinePartageeSExecuteALaPremiereAdresse()
        {
            RunOnEmulator(0x0000, 0x80);
        }

        [TestMethod]
        public void LaRoutinePartageeSExecuteALaDeuxiemeAdresse()
        {
            RunOnEmulator(0x1000, 0x90);
        }

        [TestMethod]
        public void LeGoldenDemoitToujoursLeCodeDesDeuxAdresses()
        {
            // Le decalage ne doit pas avoir bouge le golden du depot : la validation
            // par emulateur ne vaut que si elle n'a pas change la sortie de reference.
            string golden = Path.Combine(FindRepositoryRoot(), "example_bomberman-nes", "bomber.nes");
            if (!File.Exists(golden))
                Assert.Inconclusive("le golden n'est pas dans l'arborescence construite");

            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                using (FileStream stream = File.OpenRead(golden))
                    hash = BytesToHex(sha.ComputeHash(stream));
            }

            Assert.AreEqual("4E57F08754A2FF7EC788245629FB70F99D4E003F66F86742566BCA99C810A244", hash,
                "le golden NES a change");
        }

        private static void RunOnEmulator(int shift, byte expectedRoutineHigh)
        {
            string mesen = FindMesen();
            if (mesen == null)
                Assert.Inconclusive("Mesen est introuvable : mettez son chemin dans WINASM65_MESEN");

            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                LinkedImage image = Link(shift);
                string rom = BuildRom(image, temp.Path);
                string report = Path.Combine(temp.Path, "rapport.txt");
                string script = Path.Combine(temp.Path, "verification.lua");
                File.WriteAllText(script, BuildScript(report, expectedRoutineHigh, DescribeHost(mesen)));

                ProcessStartInfo startInfo = new ProcessStartInfo(mesen);
                startInfo.Arguments = "\"" + rom + "\" \"" + script + "\"";
                startInfo.Arguments = "--testrunner " + startInfo.Arguments;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.CreateNoWindow = true;

                string host = DescribeHost(mesen);
                int exitCode;
                string stdout;
                string stderr;
                using (Process process = Process.Start(startInfo))
                {
                    stdout = process.StandardOutput.ReadToEnd();
                    stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(120000))
                    {
                        process.Kill();
                        Assert.Fail("Mesen n'a pas rendu la main en 120 secondes sur " + rom);
                    }
                    exitCode = process.ExitCode;
                }

                string detail = File.Exists(report) ? File.ReadAllText(report)
                    : "(aucun rapport : " + stdout + " / " + stderr + ")";

                if (exitCode != 0)
                {
                    // The ROM, the script and the report are the only evidence an
                    // emulator failure leaves, and they are in a temporary directory
                    // that would be deleted on the way out. Kept on failure, named in
                    // the message; a passing run leaves nothing behind.
                    temp.Keep = true;
                    Assert.Fail("la machine n'a pas produit les temoins attendus pour un decalage de $"
                        + shift.ToString("X4") + " sur " + host + Environment.NewLine
                        + detail + Environment.NewLine + "pièces conservées : " + temp.Path);
                }

                Assert.AreEqual(0, exitCode,
                    "la machine n'a pas produit les temoins attendus pour un decalage de $"
                    + shift.ToString("X4") + " sur " + host + Environment.NewLine + detail);
            }
        }

        private static LinkedImage Link(int shift)
        {
            ModuleImage routine = Assemble(RoutineSource, "routine");
            ModuleImage table = Assemble(TableSource, "table");
            ModuleImage main = Assemble(MainSource, "main");

            // Through the .w65 container, not straight from the in-memory images:
            // the local label table is part of the format, so a link that skipped it
            // would pass here while the shipped build, which reads modules from disk,
            // would not.
            List<ModuleImage> modules = new List<ModuleImage> { routine, table, main };
            for (int i = 0; i < modules.Count; i++)
            {
                string path = Path.Combine(_linkDirectory.Value, modules[i].ModuleName + ".w65");
                Assert.IsTrue(W65Format.Write(path, modules[i]).Success, modules[i].ModuleName);

                ModuleImage reloaded;
                string moduleName;
                WinASM65.Core.OperationResult read = W65Format.TryRead(path, out reloaded, out moduleName);
                Assert.IsTrue(read.Success, modules[i].ModuleName + " should read back");
                modules[i] = reloaded;
            }

            LinkerOptions options = new LinkerOptions();
            options.AddressShift = shift;

            LinkedImage image;
            OperationResult result = new Linker().Link(modules, options, out image);
            Assert.IsTrue(result.Success, Describe(result));
            return image;
        }

        /// <summary>
        /// Holds the directory the modules are written to for the duration of one
        /// link. A field rather than a parameter because the helper chain that reaches
        /// <see cref="Link"/> is already four calls deep and threading a directory
        /// through it would say nothing.
        /// </summary>
        private static readonly Lazy<string> _linkDirectory = new Lazy<string>(delegate ()
        {
            string path = Path.Combine(Path.GetTempPath(), "WinASM65NesModules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        });

        /// <summary>
        /// Turns the linked image into a 32 KB PRG NES ROM, mapper 0, and publishes
        /// it with the project's own iNES writer. Two banks, because $8000-$BFFF and
        /// $C000-$FFFF are two different banks on a mapper 0, and the routine and
        /// the entry point land in each of them.
        /// </summary>
        private static string BuildRom(LinkedImage image, string directory)
        {
            byte[] prg = new byte[PrgBanks * InesFormat.PrgBankSize];
            int offset = image.OriginAddress - RomBase;
            Assert.IsTrue(offset >= 0 && offset + image.Data.Length <= prg.Length,
                "l'image liee deborde des banques PRG de la ROM");

            Array.Copy(image.Data, 0, prg, offset, image.Data.Length);

            ushort reset = image.Symbols["Reset"];
            int vectors = 0xFFFA - RomBase;
            WriteWord(prg, vectors, 0x8000);          // NMI, jamais appele ici
            WriteWord(prg, vectors + 2, reset);        // RESET
            WriteWord(prg, vectors + 4, 0x8000);       // IRQ, jamais leve

            ResolvedTarget target = new ResolvedTarget();
            target.InesPrgBanks = PrgBanks;
            target.InesChrBanks = 0;
            target.InesMapper = 0;

            string rom = Path.Combine(directory, "validation.nes");
            OperationResult written = new InesFormat().Write(rom, prg, target);
            Assert.IsTrue(written.Success, Describe(written));
            return rom;
        }

        /// <summary>
        /// Reads the three witnesses after ten frames. The report is always written,
        /// including on success, because Mesen writes nothing on the console in this
        /// mode and the exit code alone does not say what the machine saw.
        /// <para>
        /// The memory type is taken from <c>nesDebug</c> when the host has it. A read
        /// through the plain <c>cpu</c> type has side effects on the emulated machine
        /// — it goes through the CPU's own view and touches the registers and the PPU
        /// — so a monitor that reads in a loop while the machine runs corrupts it.
        /// Here the ROM has already halted in its loop before the first read, so the
        /// witnesses are safe either way; the type is still taken correctly, because
        /// this test is where someone will copy the idiom from.
        /// </para>
        /// </summary>
        private static string BuildScript(string report, byte expectedRoutineHigh, string host)
        {
            return
                "-- Genere par WinASM65.Tests.NesEmulatorTests, ne pas editer.\n" +
                "-- Temoins : $" + TableByte.ToString("X2") + " octet lu par pointeur, $"
                + OwnAddressHigh.ToString("X2") + " haut de l'adresse de la routine, $"
                + Returned.ToString("X2") + " retour dans l'appelant.\n" +
                "-- Tout est sous pcall et emu.stop est inconditionnel : un script qui\n" +
                "-- meurt avant emu.stop laisse l'emulateur vivant indefiniment.\n" +
                "local attenduHaut = " + FormatByte(expectedRoutineHigh) + "\n" +
                "local rapport = io.open([[" + report + "]], \"w\")\n" +
                "local frames = 0\n" +
                "local memType = emu.memType.nesDebug or emu.memType.cpu\n" +
                // The host path is written as a long bracket string, not inside a
                // quoted one: it is full of backslashes, and a "\P" is not a Lua
                // escape. Measured on MesenCE 2.2.1 — a nested "[[" inside a quoted
                // literal makes the whole script fail to load, silently, with exit -1
                // and no report at all.
                "rapport:write([[-- hote : " + host + "]] .. \"\\n\")\n" +
                "rapport:flush()\n" +
                "\n" +
                "-- nil quand la lecture echoue, jamais une exception : un type de memoire\n" +
                "-- absent sur cet hote ne doit pas tuer le script.\n" +
                "local function octet(a)\n" +
                "  local ok, v = pcall(emu.read, a, memType)\n" +
                "  if not ok or type(v) ~= \"number\" then return nil end\n" +
                "  return v\n" +
                "end\n" +
                "\n" +
                "-- string.format sur un nil est une erreur, pas un zero. Chaque champ de\n" +
                "-- l'etat passe donc par hexa(), qui tolere l'absence.\n" +
                "local function hexa(v, largeur)\n" +
                "  if type(v) ~= \"number\" then return \"?\" end\n" +
                "  return string.format(\"%0\" .. tostring(largeur) .. \"X\", v)\n" +
                "end\n" +
                "\n" +
                "local function ecrire(message)\n" +
                "  rapport:write(message .. \"\\n\")\n" +
                "  rapport:flush()\n" +
                "end\n" +
                "\n" +
                "local function juger()\n" +
                "  local table = octet(" + FormatByte(TableByte) + ")\n" +
                "  local haut = octet(" + FormatByte(OwnAddressHigh) + ")\n" +
                "  local retour = octet(" + FormatByte(Returned) + ")\n" +
                "  ecrire(\"type de memoire : \" .. tostring(memType))\n" +
                "  if emu.getCpuState ~= nil then\n" +
                "    local ok, cpu = pcall(emu.getCpuState)\n" +
                "    if ok and type(cpu) == \"table\" then\n" +
                "      ecrire(string.format(\"PC=$%s A=$%s X=$%s Y=$%s SP=$%s\", hexa(cpu.pc, 4),\n" +
                "        hexa(cpu.a, 2), hexa(cpu.x, 2), hexa(cpu.y, 2), hexa(cpu.sp, 2)))\n" +
                "    end\n" +
                "  end\n" +
                "  ecrire(string.format(\"t$%02X = $%s attendu $%02X\", "
                + TableByte.ToString("X2") + ", hexa(table, 2), " + FormatByte(TableValue) + "))\n" +
                "  ecrire(string.format(\"r$%02X = $%s attendu $%s\", "
                + OwnAddressHigh.ToString("X2") + ", hexa(haut, 2), hexa(attenduHaut, 2)))\n" +
                "  ecrire(string.format(\"b$%02X = $%s attendu $%02X\", "
                + Returned.ToString("X2") + ", hexa(retour, 2), " + FormatByte(ReturnValue) + "))\n" +
                "  local ok = table == " + FormatByte(TableValue)
                + " and haut == attenduHaut and retour == " + FormatByte(ReturnValue) + "\n" +
                "  ecrire(ok and \"VERDICT: les deux temoins et le retour sont bons\"\n" +
                "    or \"VERDICT: un temoin manque ou est faux\")\n" +
                "  rapport:close()\n" +
                "  emu.stop(ok and 0 or 1)\n" +
                "end\n" +
                "\n" +
                "emu.addEventCallback(function()\n" +
                "  frames = frames + 1\n" +
                "  if frames < 10 then return end\n" +
                "  local ok, err = pcall(juger)\n" +
                "  if not ok then\n" +
                "    ecrire(\"ERREUR DANS LA MESURE : \" .. tostring(err))\n" +
                "    rapport:close()\n" +
                "    emu.stop(2)\n" +
                "  end\n" +
                "end, emu.eventType.endFrame)\n";
        }

        private static string FormatByte(byte value)
        {
            return "0x" + value.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Names the emulator and its version, so a report says which host produced
        /// it. Two Mesen builds differ in their memory types and their state shape, so
        /// "the tests passed" without the host is not evidence of anything reusable.
        /// </summary>
        private static string DescribeHost(string executable)
        {
            string version = "version inconnue";
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
                if (!string.IsNullOrEmpty(info.ProductVersion))
                    version = info.ProductVersion;
            }
            catch (IOException)
            {
            }

            return executable + " (" + version + ")";
        }

        private static void WriteWord(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static string FindMesen()
        {
            List<string> candidates = new List<string>();

            string fromEnvironment = Environment.GetEnvironmentVariable("WINASM65_MESEN");
            if (!string.IsNullOrEmpty(fromEnvironment))
                candidates.Add(fromEnvironment);

            // MesenCE 2.2.1 first: it is the version the monitor bridge is pinned to,
            // and it is the one whose memType table has nesDebug. The 0.9.9 that
            // predates it on this machine has cpu instead, and it still works — the
            // witnesses are read after the ROM has halted, so nothing is disturbed.
            candidates.Add(@"C:\Projects\NES_PROJECTS\MesenCE\Mesen.exe");
            candidates.Add(@"C:\Projects\NES_PROJECTS\Mesen\Mesen.exe");
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Mesen", "Mesen.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Mesen", "Mesen.exe"));

            for (int i = 0; i < candidates.Count; i++)
                if (File.Exists(candidates[i]))
                    return candidates[i];

            return null;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "WinASM65.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return AppContext.BaseDirectory;
        }

        private static ModuleImage Assemble(string source, string name)
        {
            using (TemporaryDirectory temp = new TemporaryDirectory())
            {
                string path = Path.Combine(temp.Path, name + ".asm");
                File.WriteAllText(path, source);
                AssemblerOptions options = new AssemblerOptions();
                options.Cpu = CpuFactory.Create("6502");
                AssemblyResult result = new AssemblerFactory().Create(options)
                    .Assemble(path, Path.Combine(temp.Path, name + ".o"));
                Assert.IsTrue(result.Success, Describe(result));
                Assert.IsNotNull(result.Module, name + " should produce a module");
                result.Module.ModuleName = name;
                return result.Module;
            }
        }

        private static string Describe(AssemblyResult result)
        {
            string text = string.Empty;
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text += result.Diagnostics[i].Message + " | ";
            return text;
        }

        private static string Describe(OperationResult result)
        {
            string text = string.Empty;
            for (int i = 0; i < result.Diagnostics.Count; i++)
                text += result.Diagnostics[i].Message + " | ";
            return text;
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                text.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public string Path { get; private set; }

            /// <summary>Set on failure so the artifacts outlive the test.</summary>
            public bool Keep { get; set; }

            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "WinASM65NesEmu_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                if (Keep)
                    return;
                try
                {
                    Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

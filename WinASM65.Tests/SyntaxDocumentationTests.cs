using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Core;
using WinASM65.Cpu;

namespace WinASM65.Tests
{
    /// <summary>The syntax reference names everything the assembler accepts.</summary>
    [TestClass]
    public class SyntaxDocumentationTests
    {
        private static string Documentation()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinASM65.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "docs", "syntax.html"));
        }

        [TestMethod]
        public void EveryDirectiveIsDocumented()
        {
            string doc = Documentation();
            foreach (string directive in AssemblerEngine.DefaultDirectiveNames)
                StringAssert.Contains(doc, "<code>" + directive, directive + " is missing from docs/syntax.html");
        }

        [TestMethod]
        public void EveryCpuIsDocumented()
        {
            string doc = Documentation();
            foreach (string cpu in CpuFactory.Names)
                StringAssert.Contains(doc, "<code>" + cpu + "</code>", cpu + " is missing from docs/syntax.html");
        }
    }
}

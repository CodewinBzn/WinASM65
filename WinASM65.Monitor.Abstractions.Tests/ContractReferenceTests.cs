using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Monitor.Abstractions.Tests
{
    /// <summary>
    /// The contract references nothing, and this is what holds it to that.
    ///
    /// A reference out of the contract is how a contract stops being one. The moment
    /// the abstractions assembly needs the assembler, a Terminal.Gui type or a package
    /// to say something, a plugin that loads it has to bring all of that too, and the
    /// dependency it shares with the host stops being four interfaces. So the rule is
    /// asserted rather than trusted: only framework assemblies, and no reference in the
    /// project file at all.
    /// </summary>
    [TestClass]
    public class ContractReferenceTests
    {
        [TestMethod]
        public void TheContractAssemblyReferencesOnlyTheFramework()
        {
            foreach (AssemblyName referenced in typeof(IExecutionAdapter).Assembly.GetReferencedAssemblies())
            {
                string name = referenced.Name;

                Assert.IsTrue(name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal),
                    "the contract must reference nothing but the framework, and it references " + name);
            }
        }

        [TestMethod]
        public void TheContractProjectDeclaresNoReferenceAtAll()
        {
            string project = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "WinASM65.Monitor.Abstractions", "WinASM65.Monitor.Abstractions.csproj"));

            Assert.IsFalse(project.Contains("<ProjectReference"),
                "the contract must compile standalone: a project reference would drag the host into the plugin");
            Assert.IsFalse(project.Contains("<PackageReference"),
                "the contract must compile standalone: a package reference would be a dependency every plugin inherits");
        }

        /// <summary>
        /// Walks up from the test assembly until the repository layout is recognisable,
        /// so the project file is found without a hard-coded absolute path.
        /// </summary>
        private static string RepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "WinASM65.sln")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            Assert.Fail("could not locate the repository root above " + AppContext.BaseDirectory);
            return null;
        }
    }
}
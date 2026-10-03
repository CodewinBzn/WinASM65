using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WinASM65.Monitor.Abstractions.Tests
{
    /// <summary>
    /// The load-bearing test: an interface compiled into both the host and a plugin is
    /// two interfaces.
    ///
    /// It fails at runtime and not at compile time. The plugin under test is a second
    /// compilation that references the contract, exactly as
    /// <c>WinASM65.Monitor.Plugins.*</c> will be. Loaded without a resolver it carries
    /// its own copy of the contract, the two copies answer to one name, and casting a
    /// plugin instance to <see cref="IExecutionAdapter"/> throws
    /// <see cref="InvalidCastException"/> from a cast the compiler accepted. Nothing
    /// warns, nothing fails to build, and the failure lands wherever the user happens
    /// to press a key.
    ///
    /// So the host loads each plugin into its own <see cref="AssemblyLoadContext"/> with
    /// an explicit resolver that redirects the contract to the host's own loaded copy.
    /// These tests measure that redirect rather than assert it in a comment: they show
    /// the redirected case unifying the type and working, and they show the
    /// unredirected case failing in the specific way the plan names.
    ///
    /// The plugin is a second copy of this test assembly, loaded from the folder a
    /// plugin would be shipped in. Nothing is compiled at test time: that would need a
    /// compiler this project does not reference, and this assembly already references
    /// the contract the way a plugin does.
    /// </summary>
    [TestClass]
    public class PluginContractLoadingTests
    {
        private const string ContractName = "WinASM65.Monitor.Abstractions";

        private static string PluginFolder
        {
            get { return Path.GetDirectoryName(typeof(FakePlugin).Assembly.Location); }
        }

        private static string PluginAssemblyPath
        {
            get { return typeof(FakePlugin).Assembly.Location; }
        }

        [TestMethod]
        public void TheContractHasTheSimpleNameAResolverCanMatch()
        {
            // Read from the assembly rather than from a constant beside it. A constant
            // can drift away from the assembly it names, and a resolver matching a
            // stale name would silently do nothing.
            Assert.AreEqual(ContractName, typeof(IExecutionAdapter).Assembly.GetName().Name);
        }

        [TestMethod]
        public void TheContractLoadedThroughAPluginContextIsTheHostsOwnAssembly()
        {
            PluginLoadContext plugin = new RedirectingPluginLoadContext(
                typeof(IExecutionAdapter).Assembly, PluginFolder);
            try
            {
                Assembly resolved = plugin.LoadFromAssemblyName(typeof(IExecutionAdapter).Assembly.GetName());

                Assert.AreSame(typeof(IExecutionAdapter).Assembly, resolved,
                    "the resolver must hand back the host's assembly, not a second copy of the contract");

                Assert.AreSame(typeof(IExecutionAdapter),
                    resolved.GetType(typeof(IExecutionAdapter).FullName),
                    "one name, one Type: that is the whole point of the redirect");
            }
            finally
            {
                plugin.Unload();
            }
        }

        [TestMethod]
        public void APluginLoadedWithTheRedirectIsUsableThroughTheContract()
        {
            PluginLoadContext plugin = new RedirectingPluginLoadContext(
                typeof(IExecutionAdapter).Assembly, PluginFolder);
            try
            {
                object instance = InstantiatePlugin(plugin);

                // The interface the object actually implements is the host's Type
                // object, not merely an interface of the same name.
                Assert.AreSame(typeof(IExecutionAdapter),
                    instance.GetType().GetInterface(typeof(IExecutionAdapter).FullName),
                    "the plugin resolved the contract to another Type");

                // And the host can use it through its own interface, with no cast
                // ceremony and no translation layer in between.
                var adapter = (IExecutionAdapter)instance;
                Assert.AreEqual(ExecutionCapability.Memory | ExecutionCapability.CpuState, adapter.Capabilities);

                CpuState cpu = adapter.ReadCpuState();
                Assert.AreEqual(0xCBFD, cpu.Pc);
                Assert.AreEqual(43358022L, cpu.CycleCount);

                // The other three contracts unify the same way, through one resolver.
                Assert.AreSame(typeof(IVideoProvider),
                    instance.GetType().GetInterface(typeof(IVideoProvider).FullName));
                Assert.AreSame(typeof(ISystemProfile),
                    instance.GetType().GetInterface(typeof(ISystemProfile).FullName));
                Assert.AreSame(typeof(IAssistantProvider),
                    instance.GetType().GetInterface(typeof(IAssistantProvider).FullName));
                Assert.AreEqual(256, ((IVideoProvider)instance).Width);
            }
            finally
            {
                plugin.Unload();
            }
        }

        [TestMethod]
        public void WithoutTheRedirectTheContractIsASecondTypeAndTheCastFails()
        {
            PluginLoadContext plugin = new UnredirectedPluginLoadContext(PluginFolder);
            try
            {
                object instance = InstantiatePlugin(plugin);

                // The contract the plugin actually bound to.
                Type shadow = instance.GetType().GetInterface(typeof(IExecutionAdapter).FullName);
                Assert.IsNotNull(shadow, "the plugin does not implement the contract at all");

                // Same name, same version, same bytes on disk. Nothing in the build
                // would have said otherwise: this is the failure with nothing to catch
                // at compile time.
                Assert.AreEqual(typeof(IExecutionAdapter).Assembly.FullName, shadow.Assembly.GetName().FullName,
                    "the two copies are the same contract by name, which is why the failure is invisible");

                Assert.AreNotSame(typeof(IExecutionAdapter), shadow,
                    "a second Type for one contract name is exactly the hazard being tested");

                // The cast the compiler allowed, refused at runtime.
                Assert.ThrowsException<InvalidCastException>(
                    () => { var ignored = (IExecutionAdapter)instance; },
                    "casting to the host's interface must fail without the redirect");
            }
            finally
            {
                plugin.Unload();
            }
        }

        [TestMethod]
        public void AMissingHostContractIsNamedRatherThanArrivingAsACastFailure()
        {
            // The half-finished state a loading bug actually produces: a resolver that
            // was meant to redirect the contract but has no host copy to give. It has
            // to say so where the mistake is. A bare InvalidCastException several calls
            // later, naming nothing, is what makes this class of bug expensive.
            PluginLoadContext plugin = new RedirectingPluginLoadContext(null, PluginFolder);
            try
            {
                Exception thrown = null;
                try
                {
                    InstantiatePlugin(plugin);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }

                Assert.IsNotNull(thrown, "a resolver with no host copy must refuse the plugin, not load it");

                Exception named = FindInChain(thrown, typeof(PluginContractLoadException));
                Assert.IsNotNull(named, "the failure must be named; the chain was: " + thrown);
                StringAssert.Contains(named.Message, ContractName);
                StringAssert.Contains(named.Message, PluginFolder);

                Assert.IsNull(FindInChain(thrown, typeof(InvalidCastException)),
                    "a misconfigured resolver must not degrade into a cast failure");
            }
            finally
            {
                plugin.Unload();
            }
        }

        /// <summary>
        /// Loads the plugin into the given context and builds it. Instantiating is what
        /// forces the contract to be resolved: an assembly loads happily and fails only
        /// when a type that references it is first used.
        /// </summary>
        private static object InstantiatePlugin(PluginLoadContext plugin)
        {
            Assembly assembly = plugin.LoadFromAssemblyPath(PluginAssemblyPath);
            Type type = assembly.GetType(typeof(FakePlugin).FullName);
            Assert.IsNotNull(type, "the plugin type was not found in the loaded copy");
            return Activator.CreateInstance(type);
        }

        /// <summary>
        /// Walks the inner exceptions. The runtime is free to wrap what a resolver
        /// throws, so the test looks for the named failure anywhere on the chain
        /// rather than insisting on the exact exception that came out.
        /// </summary>
        private static Exception FindInChain(Exception exception, Type wanted)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (wanted.IsInstanceOfType(current))
                    return current;
            }

            return null;
        }

        /// <summary>
        /// What a plugin's load context does: resolve from the plugin's own folder.
        /// Shared by both contexts below, so the only difference between them is the
        /// redirect under test and not the probing around it.
        /// </summary>
        private abstract class PluginLoadContext : AssemblyLoadContext
        {
            private readonly string _folder;

            protected PluginLoadContext(string name, string folder)
                : base(name, isCollectible: true)
            {
                _folder = folder;
            }

            protected static bool IsContract(AssemblyName assemblyName)
            {
                return string.Equals(assemblyName.Name, ContractName, StringComparison.OrdinalIgnoreCase);
            }

            /// <summary>
            /// The plugin folder's own copy of a dependency, which is what a plugin
            /// shipped as a folder gets. Null when the folder holds none, which leaves
            /// the runtime to resolve it as it normally would.
            /// </summary>
            protected Assembly FromPluginFolder(AssemblyName assemblyName)
            {
                string candidate = Path.Combine(_folder, assemblyName.Name + ".dll");
                return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
            }
        }

        /// <summary>
        /// The host's resolver: the contract always comes from the host, every other
        /// assembly from the plugin's own folder.
        ///
        /// Returning the host's assembly explicitly, rather than returning null and
        /// trusting the runtime to notice the contract is already loaded. That was
        /// measured while writing this: null happens to reach the host's copy today,
        /// because no other context has one to hand. It is not the case to rely on,
        /// and the case that matters does the opposite — resolving a plugin folder the
        /// natural way, with an <c>AssemblyDependencyResolver</c>, hands back the
        /// sibling <c>WinASM65.Monitor.Abstractions.dll</c> sitting next to the
        /// plugin, which is a second Type and a cast failure. See
        /// <see cref="UnredirectedPluginLoadContext"/>, which is that case.
        /// </summary>
        private sealed class RedirectingPluginLoadContext : PluginLoadContext
        {
            private readonly Assembly _hostContract;

            public RedirectingPluginLoadContext(Assembly hostContract, string folder)
                : base("plugin-redirecting", folder)
            {
                _hostContract = hostContract;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                if (!IsContract(assemblyName))
                    return FromPluginFolder(assemblyName);

                if (_hostContract == null)
                    throw new PluginContractLoadException(assemblyName.Name, PluginFolder);

                return _hostContract;
            }
        }

        /// <summary>
        /// A plugin folder resolved the way a plugin folder normally is, with no
        /// redirect for the contract: it finds the copy shipped beside it and loads
        /// it. That second <see cref="Type"/> is the failure the redirect exists to
        /// prevent, reproduced here rather than described in a comment.
        /// </summary>
        private sealed class UnredirectedPluginLoadContext : PluginLoadContext
        {
            public UnredirectedPluginLoadContext(string folder)
                : base("plugin-unredirected", folder)
            {
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                return FromPluginFolder(assemblyName);
            }
        }
    }

    /// <summary>
    /// A plugin whose resolver has no host copy of the contract to give.
    ///
    /// Declared here rather than in the contract assembly because the contract holds no
    /// behaviour and this exception is raised by a loader. The host owns its own copy
    /// once it starts loading plugins for real; this test needs the same decision made
    /// the same way.
    /// </summary>
    public sealed class PluginContractLoadException : Exception
    {
        public PluginContractLoadException(string contractName, string pluginFolder)
            : base("cannot load the plugin in '" + pluginFolder + "': it needs '" + contractName
                + "', the contract shared between host and plugins. No host copy was available to redirect it"
                + " to, so the plugin would have loaded a second copy of the contract and every cast of it to the"
                + " host's interfaces would fail at runtime.")
        {
            ContractName = contractName;
            PluginFolder = pluginFolder;
        }

        /// <summary>Simple name of the contract that could not be resolved.</summary>
        public string ContractName { get; private set; }

        /// <summary>The plugin folder being loaded when it failed.</summary>
        public string PluginFolder { get; private set; }
    }
}
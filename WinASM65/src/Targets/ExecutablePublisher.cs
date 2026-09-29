using System;
using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.Targets
{
    public interface IExecutablePublisher
    {
        OperationResult Publish(string path, byte[] payload, ResolvedTarget target);
    }

    public class ExecutablePublisher : IExecutablePublisher
    {
        private readonly Dictionary<string, IExecutableFormat> _formats;

        public ExecutablePublisher()
        {
            _formats = new Dictionary<string, IExecutableFormat>(StringComparer.OrdinalIgnoreCase);
            Register(new RawBinaryFormat());
            Register(new InesFormat());
            Register(new CommodorePrgFormat());
            Register(new AtariXexFormat());
            Register(new Apple2BinaryFormat());
            Register(new PaddedRomFormat());
            Register(new O65Format());
            Register(new IntelHexFormat());
            Register(new MotorolaSrecFormat());
        }

        public void Register(IExecutableFormat format)
        {
            if (format == null)
                throw new ArgumentNullException("format");
            _formats[format.Name] = format;
        }

        public OperationResult Publish(string path, byte[] payload, ResolvedTarget target)
        {
            target = target ?? new ResolvedTarget();
            string name = string.IsNullOrWhiteSpace(target.FormatName) ? "bin" : target.FormatName.Trim();
            IExecutableFormat format;
            if (!_formats.TryGetValue(name, out format))
            {
                List<Diagnostic> diagnostics = new List<Diagnostic>();
                diagnostics.Add(new Diagnostic(new SourceLocation(string.Empty, 0),
                    "Unknown output format '" + name + "'. Use bin, ines, prg, xex, a2bin, rom, o65, ihex, or srec."));
                return new OperationResult(false, diagnostics);
            }
            return format.Write(path, payload, target);
        }
    }
}

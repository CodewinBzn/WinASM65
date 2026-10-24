using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace WinASM65.Tooling.Rpc
{
    /// <summary>
    /// The framing DAP and LSP share: a <c>Content-Length</c> header, a blank line,
    /// then exactly that many bytes of UTF-8 JSON.
    ///
    /// Reads byte by byte up to the body, never through a buffered reader: a reader
    /// that buffered past the header would swallow the start of the next message.
    /// </summary>
    public sealed class MessageStream
    {
        private readonly Stream _input;
        private readonly Stream _output;
        private readonly object _writeLock = new object();

        public MessageStream(Stream input, Stream output)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>The next message, or null at the end of the input.</summary>
        public JsonObject Read()
        {
            int length = -1;
            while (true)
            {
                string header = ReadHeaderLine();
                if (header == null)
                    return null;
                if (header.Length == 0)
                {
                    if (length < 0)
                        continue; // stray blank line between messages
                    break;
                }

                int colon = header.IndexOf(':');
                if (colon > 0 && header.Substring(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(header.Substring(colon + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out length)
                        || length < 0)
                        throw new InvalidDataException("invalid Content-Length: " + header);
                }
            }

            byte[] body = new byte[length];
            int read = 0;
            while (read < length)
            {
                int n = _input.Read(body, read, length - read);
                if (n <= 0)
                    return null;
                read += n;
            }

            return JsonNode.Parse(body) as JsonObject
                ?? throw new InvalidDataException("a message body is not a JSON object");
        }

        public void Write(JsonNode message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");
            lock (_writeLock)
            {
                _output.Write(header, 0, header.Length);
                _output.Write(body, 0, body.Length);
                _output.Flush();
            }
        }

        private string ReadHeaderLine()
        {
            StringBuilder line = new StringBuilder();
            while (true)
            {
                int b = _input.ReadByte();
                if (b < 0)
                    return line.Length == 0 ? null : line.ToString();
                if (b == '\n')
                    return line.ToString().TrimEnd('\r');
                line.Append((char)b);
            }
        }
    }
}

using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinASM65.Tooling.Rpc;

namespace WinASM65.Tooling.Tests
{
    [TestClass]
    public class MessageStreamTests
    {
        private static MessageStream Over(string raw, MemoryStream output = null)
        {
            return new MessageStream(new MemoryStream(Encoding.UTF8.GetBytes(raw)), output ?? new MemoryStream());
        }

        private static string Frame(string json)
        {
            return "Content-Length: " + Encoding.UTF8.GetByteCount(json) + "\r\n\r\n" + json;
        }

        [TestMethod]
        public void TwoMessagesBackToBackAreReadApart()
        {
            MessageStream stream = Over(Frame("{\"seq\":1}") + Frame("{\"seq\":2,\"text\":\"é\"}"));
            Assert.AreEqual(1, (int)stream.Read()["seq"]);
            JsonObject second = stream.Read();
            Assert.AreEqual(2, (int)second["seq"]);
            Assert.AreEqual("é", (string)second["text"], "the length counts bytes, not characters");
            Assert.IsNull(stream.Read());
        }

        [TestMethod]
        public void OtherHeadersAreIgnored()
        {
            string json = "{\"seq\":7}";
            MessageStream stream = Over("Content-Type: application/vscode-jsonrpc; charset=utf-8\r\n"
                + "Content-Length: " + json.Length + "\r\n\r\n" + json);
            Assert.AreEqual(7, (int)stream.Read()["seq"]);
        }

        [TestMethod]
        public void AWrittenMessageCarriesItsByteLength()
        {
            MemoryStream output = new MemoryStream();
            Over("", output).Write(new JsonObject { ["text"] = "é" });
            output.Position = 0;
            JsonObject back = new MessageStream(output, new MemoryStream()).Read();
            Assert.AreEqual("é", (string)back["text"]);
        }

        [TestMethod]
        public void ATruncatedBodyEndsTheStream()
        {
            Assert.IsNull(Over("Content-Length: 50\r\n\r\n{\"seq\":1}").Read());
        }
    }
}

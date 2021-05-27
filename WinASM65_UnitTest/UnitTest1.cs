using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using WinASM65;

namespace WinASM65_UnitTest
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            var tokens =  Tokenizer.Tokenize("]posx % (1 +$2)");
            Console.WriteLine(tokens);
        }
    }
}

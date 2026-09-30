// WinASM65 - the dialects, by the name a person gives them
//
// There are four BASICs in scope and three of them are reachable from a command
// line, so somebody has to say which one a source is written in. That mapping
// lives here rather than in the command line, because a test that wants to check
// it wants the same answer the command line gets.

using System;
using System.Collections.Generic;

namespace WinASM65.TextFormat
{
    public static class BasicDialects
    {
        /// <summary>
        /// The dialect a name asks for, or null when the name is not one of
        /// ours. The names are the ones a person is likely to type, and a name
        /// that matches two dialects is an error rather than a choice: `c64` is
        /// a machine, and a machine says nothing about which BASIC is on it.
        /// </summary>
        public static BasicDialect ByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            switch (name.Trim().ToLowerInvariant())
            {
                case "applesoft":
                case "apple2":
                    return ApplesoftDialect.Create();
                case "waterloo":
                case "cbm-basic-v2":
                    return WaterlooDialect.Create();
                case "bbc":
                case "bbc-basic-v":
                    return BbcBasicDialect.Create();
                default:
                    return null;
            }
        }

        public static IEnumerable<string> Names
        {
            get
            {
                return new string[] { "applesoft", "waterloo", "bbc" };
            }
        }

        /// <summary>The names, for a message that has to list what is on offer.</summary>
        public static string NameList()
        {
            List<string> names = new List<string>(Names);
            return string.Join(", ", names.ToArray());
        }
    }
}

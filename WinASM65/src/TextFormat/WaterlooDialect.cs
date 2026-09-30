// WinASM65 - Waterloo Structured BASIC
//
// Waterloo Structured BASIC is not a BASIC of its own. It is a cartridge, sold
// by Waterloo MicroSystems and by CBM Canada, that sits on top of the BASIC the
// machine already has, and the only trace of it in a saved program is the words
// it added. Everything else — the line numbers, the variables, the arithmetic —
// is the host's, and the host's program is the container the interpreter reads
// when it is told to RUN one.
//
// On the Commodore machines — PET, 8032, VIC-20, C64 — that host is Commodore
// BASIC V2, and the cartridge's own words are written after a two byte marker,
// $FF $FF. A Waterloo program is therefore a Commodore program with a handful
// of keywords in a space the host leaves alone, and this dialect is the host's
// table with those twelve words added. The line layout is the one the host
// already uses and the one this repository already writes for every Microsoft
// derived BASIC: two bytes of the address of the next line, two of the line
// number, the tokens, and a $00.
//
// Three things about the cartridge are worth writing down, because a table
// cannot express any of them and pretending otherwise would be a lie:
//
//   - The cartridge re-tokenises a statement that opens a line. An IF that
//     starts a line is the structured one, $F3, not the host's $8B, and the
//     choice depends on the position of the word rather than on the word. So
//     the host's IF is left out of the table below: one word has one byte, and
//     the one that belongs to a structured program is the cartridge's. A file
//     that still holds the host's $8B is read back as \x8B, which the encoder
//     writes out again unchanged, so the round trip stays exact.
//   - RENUMBER, DELETE and AUTO work in the direct mode and are not tokenised
//     at all, so they are not in the table.
//   - Only the Commodore versions are described here. Waterloo also sold an
//     Apple II version, whose file layout no reference consulted for this
//     dialect describes; rather than invent a table for it, that machine is
//     left out and named in docs/targets-text.md as a known gap.

using System;
using System.Collections.Generic;
using WinASM65.Core;

namespace WinASM65.TextFormat
{
    public static class WaterlooDialect
    {
        /// <summary>The marker the cartridge writes before each of its own words.</summary>
        public static readonly byte[] Marker = { 0xFF, 0xFF };

        /// <summary>The first byte of the cartridge's own words.</summary>
        public const byte ExtendedFirst = 0xF3;

        /// <summary>
        /// The longest a line may be, header and the $00 that ends it counted.
        /// A line has to fit in the interpreter's page buffer, so the record
        /// cannot reach 256 bytes.
        /// </summary>
        public const int MaxLineLength = 255;

        public static BasicDialect Create()
        {
            Dialect dialect = new Dialect();

            // Commodore BASIC V2, $80 to $CB, in the order the ROM carries them.
            // IF is the one word deliberately absent: the cartridge has its own
            // IF, and a table gives one word one byte.
            string[] host = new string[]
            {
                "END", "FOR", "NEXT", "DATA", "INPUT#", "INPUT", "DIM", "READ",
                "LET", "GOTO", "RUN", null, "RESTORE", "GOSUB", "RETURN", "REM",
                "STOP", "ON", "WAIT", "LOAD", "SAVE", "VERIFY", "DEF", "POKE",
                "PRINT#", "PRINT", "CONT", "LIST", "CLR", "CMD", "SYS", "OPEN",
                "CLOSE", "GET", "NEW", "TAB(", "TO", "FN", "SPC(", "THEN", "NOT",
                "STEP", "+", "-", "*", "/", "^", "AND", "OR", ">", "=", "<",
                "SGN", "INT", "ABS", "USR", "FRE", "POS", "SQR", "RND", "LOG",
                "EXP", "COS", "SIN", "TAN", "ATN", "PEEK", "LEN", "STR$", "VAL",
                "ASC", "CHR$", "LEFT$", "RIGHT$", "MID$", "GO"
            };

            for (int i = 0; i < host.Length; i++)
            {
                if (host[i] != null)
                    dialect.Define((byte)(0x80 + i), host[i]);
            }

            // The cartridge's own words, $F3 to $FE. The list is the one the
            // reverse engineering of the VIC-20 cartridge published and the one
            // the C64 wiki repeats: an IF that opens a line, the loop, and the
            // procedure, each with the keyword that closes it.
            string[] structured = new string[]
            {
                "IF", "CALL", "LOOP", "ENDLOOP", "UNTIL", "WHILE", "ELSEIF",
                "ELSE", "ENDIF", "PROC", "ENDPROC", "QUIT"
            };

            for (int i = 0; i < structured.Length; i++)
                dialect.Define((byte)(ExtendedFirst + i), structured[i], Marker);

            return dialect;
        }

        /// <summary>The dialect itself, with everything that is not the token table.</summary>
        private sealed class Dialect : BasicDialect
        {
            public Dialect()
                : base("Waterloo Structured BASIC", 0x0801, 63999,
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789$")
            {
                // Commodore BASIC keeps the quotes of a string in the line and
                // reads to the closing one, so no length is written.
                StringsCarryLength = false;
            }

            public override int TokenLength(byte token)
            {
                // A marker is $FF, and the cartridge's own words are three bytes.
                return token == Marker[0] ? 3 : 1;
            }

            public override int MaxRecordLength
            {
                get { return MaxLineLength; }
            }
        }
    }
}

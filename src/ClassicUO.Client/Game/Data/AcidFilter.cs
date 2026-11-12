// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ClassicUO.Game.Data
{
    /// <summary>
    /// Ground acid / ooze items the walker can treat as obstacles ("Avoid acid", macro ToggleAcidBlock).
    /// Matched by graphic AND hue, because 0x122A is also plain blood.
    /// The list lives in Data/Client/acid.txt (one "graphic hue" pair per line, decimal or 0x hex).
    /// The server never blocks these; only this client refuses to step onto them.
    /// </summary>
    internal static class AcidFilter
    {
        // TrueUO: PoolOfAcid / AcidSlime 0x122A hue 0x3F; ExplosiveGoo 0x122A hue 1932;
        // InfernalOoze (Interred Grizzle) and StainedOoze (Ilhenir) 0x122A hue 0x95, turning into 0x122B (still damaging).
        // Hue 83 is the acid seen on UO Dreams.
        internal static readonly (ushort Graphic, ushort Hue)[] Defaults =
        {
            (0x122A, 0x003F),
            (0x122A, 83),
            (0x122A, 1932),
            (0x122A, 0x0095),
            (0x122B, 0x0095)
        };

        private static readonly HashSet<uint> _entries = new HashSet<uint>();

        public static int Count => _entries.Count;

        public static bool IsAcid(ushort graphic, ushort hue)
        {
            return _entries.Count != 0 && _entries.Contains(Key(graphic, hue));
        }

        public static void Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                try
                {
                    File.WriteAllText(filePath, DefaultFileText());
                }
                catch
                {
                    // read-only folder: fall back to the built-in list
                }
            }

            string text = File.Exists(filePath) ? File.ReadAllText(filePath) : DefaultFileText();
            LoadFromText(text);
        }

        internal static void LoadFromText(string text)
        {
            _entries.Clear();

            foreach (string rawLine in text.Split('\n'))
            {
                if (TryParseLine(rawLine, out ushort graphic, out ushort hue))
                {
                    _entries.Add(Key(graphic, hue));
                }
            }
        }

        internal static void LoadDefaults()
        {
            _entries.Clear();

            foreach ((ushort graphic, ushort hue) in Defaults)
            {
                _entries.Add(Key(graphic, hue));
            }
        }

        /// <summary>"0x122A 83", "0x122A, 0x53", "4650 83 # comment". Anything else is ignored.</summary>
        internal static bool TryParseLine(string line, out ushort graphic, out ushort hue)
        {
            graphic = 0;
            hue = 0;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            int comment = line.IndexOfAny(new[] { '#', ';' });

            if (comment >= 0)
            {
                line = line.Substring(0, comment);
            }

            string[] parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);

            return parts.Length >= 2 && TryParseNumber(parts[0], out graphic) && TryParseNumber(parts[1], out hue);
        }

        private static bool TryParseNumber(string token, out ushort value)
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(token.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }

            return ushort.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static uint Key(ushort graphic, ushort hue) => ((uint) graphic << 16) | hue;

        internal static string DefaultFileText()
        {
            return
                "# Acid / ooze on the ground that \"Avoid acid\" (macro ToggleAcidBlock) treats as an obstacle.\n" +
                "# One item per line: graphic hue. Decimal or 0x hex. Both must match (0x122A is also plain blood).\n" +
                "# The client shows an item's graphic and hue with the -info command.\n" +
                "0x122A 0x3F   # Pool of Acid (solen etc.) / Acid Slime, TrueUO\n" +
                "0x122A 83     # acid seen on UO Dreams\n" +
                "0x122A 1932   # Explosive Goo\n" +
                "0x122A 0x95   # Infernal Ooze (Interred Grizzle), Stained Ooze (Ilhenir)\n" +
                "0x122B 0x95   # the same ooze after it dries; still damages\n";
        }
    }
}

// SPDX-License-Identifier: BSD-2-Clause
// ReX: per-spell casting ranges from Data/Client/spellranges.txt, for servers that change them
// (UO Dreams fields reach 9 tiles, not TrueUO's 10). Used by the on-cast range ring and the field preview.
// The file is re-read when it changes, so edits apply without restarting the client.
//
// Format, one spell per line:   <spell number or name> = <range>     (# starts a comment)
//   39 = 9
//   Paralyze Field = 9
// Spell numbers: magery 1-64, necromancy 101-117, chivalry 201-210, bushido 401-406, ninjitsu 501-508,
// spellweaving 601-616, mysticism 678-693, masteries 701+.

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Game.Data;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal static class SpellRanges
    {
        private const string FILE_NAME = "spellranges.txt";
        private const uint CHECK_INTERVAL_MS = 2000;

        private static readonly Dictionary<int, int> _ranges = new Dictionary<int, int>();
        private static string _path;
        private static DateTime _loadedWriteTime = DateTime.MinValue;
        private static uint _nextCheck;

        /// <summary>The range set in the file for this spell, if there is one.</summary>
        public static bool TryGetRange(int spellIndex, out int range)
        {
            Refresh();

            return _ranges.TryGetValue(spellIndex, out range);
        }

        public static int GetRange(int spellIndex, int fallback)
        {
            return TryGetRange(spellIndex, out int range) ? range : fallback;
        }

        private static void Refresh()
        {
            if (Time.Ticks < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.Ticks + CHECK_INTERVAL_MS;

            try
            {
                if (_path == null)
                {
                    string dir = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client");
                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, FILE_NAME);

                    if (!File.Exists(_path))
                    {
                        File.WriteAllText(_path, DEFAULT_FILE);
                    }
                }

                DateTime writeTime = File.GetLastWriteTimeUtc(_path);

                if (writeTime == _loadedWriteTime)
                {
                    return;
                }

                _loadedWriteTime = writeTime;
                Parse(File.ReadAllLines(_path));
            }
            catch (Exception ex)
            {
                Log.Warn($"spellranges.txt: {ex.Message}");
            }
        }

        private static void Parse(string[] lines)
        {
            _ranges.Clear();
            Dictionary<string, int> names = null;

            foreach (string raw in lines)
            {
                string line = raw;
                int hash = line.IndexOf('#');

                if (hash >= 0)
                {
                    line = line.Substring(0, hash);
                }

                int eq = line.IndexOf('=');

                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();

                if (!int.TryParse(line.Substring(eq + 1).Trim(), out int range) || range < 0 || range > 30)
                {
                    continue;
                }

                if (!int.TryParse(key, out int spell))
                {
                    names ??= BuildNameMap();

                    if (!names.TryGetValue(key.ToLowerInvariant(), out spell))
                    {
                        Log.Warn($"spellranges.txt: unknown spell '{key}'");
                        continue;
                    }
                }

                _ranges[spell] = range;
            }
        }

        private static Dictionary<string, int> BuildNameMap()
        {
            var map = new Dictionary<string, int>();

            for (int i = 1; i < 800; i++)
            {
                SpellDefinition sd = SpellDefinition.FullIndexGetSpell(i);

                if (sd != null && sd.ID == i && !string.IsNullOrWhiteSpace(sd.Name))
                {
                    map.TryAdd(sd.Name.Trim().ToLowerInvariant(), i);
                }
            }

            return map;
        }

        private const string DEFAULT_FILE =
@"# ReX spell ranges: how far away (in tiles) you can target each spell.
# Used by the range ring shown while casting / holding the spell's target cursor, and by the field preview
# (out of range = every tile shown as skipped). Spells not listed use the range ring slider in Options.
# One per line: <spell number or name> = <range>. The file is re-read a couple of seconds after you save it.
# Spell numbers: magery 1-64, necromancy 101-117, chivalry 201-210, bushido 401-406, ninjitsu 501-508,
# spellweaving 601-616, mysticism 678-693, masteries 701+.

# UO Dreams: fields reach 9 tiles (TrueUO uses 10).
Wall of Stone = 9
Fire Field = 9
Poison Field = 9
Paralyze Field = 9
Energy Field = 9
";
    }
}

// SPDX-License-Identifier: BSD-2-Clause
// ReX: per-spell casting ranges from Data/Client/spellranges.txt, for servers that change them
// (UO Dreams fields reach 9 tiles, not TrueUO's 10). Used by the on-cast range ring and the field preview.
// The file is re-read when it changes, so edits apply without restarting the client.
//
// Format, one spell per line:   <spell number or name> = <range> [circle | radius <r>]     (# starts a comment)
//   39 = 9
//   Paralyze Field = 9 radius 10
//   default = 10
// <range> alone = the stock RunUO/TrueUO square check (|dx| and |dy| both <= range).
// circle = dx² + dy² <= range². radius <r> = the square check AND dx² + dy² <= r² (a square with cut corners).
// Spell numbers: magery 1-64, necromancy 101-117, chivalry 201-210, bushido 401-406, ninjitsu 501-508,
// spellweaving 601-616, mysticism 678-693, masteries 701+.

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal readonly struct SpellRangeRule
    {
        public SpellRangeRule(int range, bool circle) : this(range, circle ? range : 0)
        {
        }

        /// <param name="radius">0 = no round limit (plain square).</param>
        public SpellRangeRule(int range, int radius)
        {
            Range = range;
            Radius = radius;
        }

        public int Range { get; }

        /// <summary>Round limit in tiles (dx² + dy² ≤ Radius²) on top of the square one; 0 = none.</summary>
        public int Radius { get; }

        public bool Circle => Radius > 0;

        public bool Contains(int dx, int dy)
        {
            return Math.Abs(dx) <= Range && Math.Abs(dy) <= Range && (Radius <= 0 || dx * dx + dy * dy <= Radius * Radius);
        }

        public string Describe() => Radius <= 0 ? Range.ToString() : Radius == Range ? $"{Range}, round" : $"{Range}, corners cut at {Radius}";
    }

    internal static class SpellRanges
    {
        private const string FILE_NAME = "spellranges.txt";
        private const uint CHECK_INTERVAL_MS = 2000;

        private static readonly Dictionary<int, SpellRangeRule> _ranges = new Dictionary<int, SpellRangeRule>();
        private static SpellRangeRule? _default;
        private static string _path;
        private static DateTime _loadedWriteTime = DateTime.MinValue;
        private static uint _nextCheck;

        /// <summary>The rule set in the file for this spell (or the file's default line), if there is one.</summary>
        public static bool TryGetRule(int spellIndex, out SpellRangeRule rule)
        {
            Refresh();

            if (_ranges.TryGetValue(spellIndex, out rule))
            {
                return true;
            }

            if (_default.HasValue)
            {
                rule = _default.Value;
                return true;
            }

            return false;
        }

        public static SpellRangeRule GetRule(int spellIndex, int fallbackRange)
        {
            return TryGetRule(spellIndex, out SpellRangeRule rule) ? rule : new SpellRangeRule(fallbackRange, false);
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
            _default = null;
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
                string[] value = line.Substring(eq + 1).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

                if (value.Length == 0 || !int.TryParse(value[0], out int range) || range < 0 || range > 30)
                {
                    continue;
                }

                int radius = 0;

                if (value.Length > 1 && value[1].Equals("circle", StringComparison.OrdinalIgnoreCase))
                {
                    radius = range;
                }
                else if (value.Length > 2 && value[1].Equals("radius", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[2], out int r) && r > 0)
                {
                    radius = r;
                }

                if (key.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    _default = new SpellRangeRule(range, radius);
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

                _ranges[spell] = new SpellRangeRule(range, radius);
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
# Used by the range ring shown while casting / holding the spell's target cursor, the field preview
# (out of range = every tile shown as skipped) and the out-of-range target block.
# One per line: <spell number or name> = <range> [circle | radius <r>]
#   <range> alone: the stock check, |dx| and |dy| both within range (a square ring).
#   circle: dx*dx + dy*dy <= range*range (a round ring).
#   radius <r>: the square check AND dx*dx + dy*dy <= r*r, a square with its corners cut off.
#     '9 radius 10': 9 straight out, up to 4 either side at 9 (81+16 = 97 <= 100; 5 either side is 106), 8 and 6 (100).
# 'default = <range> [shape]' applies to every spell not listed. Without it, unlisted spells use the
# range ring slider in Options and are never blocked.
# The file is re-read a couple of seconds after you save it.
# Spell numbers: magery 1-64, necromancy 101-117, chivalry 201-210, bushido 401-406, ninjitsu 501-508,
# spellweaving 601-616, mysticism 678-693, masteries 701+.

default = 10

# UO Dreams: fields reach 9 straight out and 4 either side at 9, so a square of 9 with the corners cut at 10.
Wall of Stone = 9 radius 10
Fire Field = 9 radius 10
Poison Field = 9 radius 10
Paralyze Field = 9 radius 10
Energy Field = 9 radius 10

# UO Dreams, measured in game
Energy Vortex = 15
Word of Death = 12
";

        // ------- Out-of-range block -------

        /// <summary>
        /// True (and prints a message) when the open cursor is your spell's cursor and (x, y) is outside its range,
        /// so the click should not be sent. Only spells with a rule in the file (or a default line) are checked.
        /// </summary>
        public static bool BlockIfOutOfRange(World world, int x, int y)
        {
            Profile p = ProfileManager.CurrentProfile;

            if (p == null || !p.BlockOutOfRangeSpellTargets || world?.Player == null || !world.TargetManager.IsSpellTarget)
            {
                return false;
            }

            if (!TryGetRule(GameActions.LastSpellIndex, out SpellRangeRule rule))
            {
                return false;
            }

            GetPlayerPosition(world, out int px, out int py);

            if (rule.Contains(x - px, y - py))
            {
                return false;
            }

            GameActions.Print(world, $"Out of range ({rule.Describe()}) - target not sent", 0x0022);

            return true;
        }

        public static bool BlockIfOutOfRange(World world, uint serial)
        {
            if (world?.Player == null || serial == world.Player.Serial || world.Get(serial) is not Entity e || e.IsDestroyed)
            {
                return false;
            }

            int x = e.X, y = e.Y;

            if (e is Mobile m && m.Steps.Count != 0)
            {
                ref Mobile.Step st = ref m.Steps.Back();
                x = st.X;
                y = st.Y;
            }
            else if (e is Item item && !item.OnGround)
            {
                return false; // in a container / worn: the server checks the root, leave it to the server
            }

            return BlockIfOutOfRange(world, x, y);
        }

        public static void GetPlayerPosition(World world, out int x, out int y)
        {
            x = world.Player.X;
            y = world.Player.Y;

            if (world.Player.Steps.Count != 0)
            {
                ref Mobile.Step st = ref world.Player.Steps.Back();
                x = st.X;
                y = st.Y;
            }
        }
    }
}

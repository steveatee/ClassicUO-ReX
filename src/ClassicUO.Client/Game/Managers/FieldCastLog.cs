// SPDX-License-Identifier: BSD-2-Clause
// ReX: checks the field preview against what the server actually creates. When you send a field spell's
// target, the preview's prediction is recorded; ~2 s later the new field items around the target are
// collected and every tile is written to Data/Client/field_casts.csv (predicted vs actual, plus the land
// heights under it), and a one-line verdict is printed in the journal. Used to find the rules the preview
// is still missing.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal static class FieldCastLog
    {
        private const uint COLLECT_AFTER_MS = 2_000;
        private const int SEARCH = 3;

        private static bool _pending;
        private static uint _sentAt;
        private static int _spell, _rules, _tx, _ty, _pz, _px, _py, _pzPlayer, _count;
        private static string _targetKind;
        private static bool _inRange;
        private static readonly (int X, int Y, bool Placed)[] _predicted = new (int, int, bool)[5];
        private static readonly HashSet<uint> _existing = new HashSet<uint>();

        public static bool IsFieldGraphic(ushort g) =>
            g >= 0x0080 && g <= 0x0082 // wall of stone
            || g >= 0x398C && g <= 0x39A0 // fire
            || g >= 0x3914 && g <= 0x3929 // poison
            || g >= 0x3946 && g <= 0x3964 // energy
            || g >= 0x3967 && g <= 0x3989; // paralyze

        /// <summary>Called when a target is sent while your spell's cursor is open.</summary>
        public static void NotifyTargetSent(World world, int x, int y, int z, string targetKind)
        {
            Profile profile = ProfileManager.CurrentProfile;
            int spell = GameActions.LastSpellIndex;

            if (profile == null || !profile.LogFieldCasts || world?.Player == null || world.Map == null || !FieldPlacement.IsFieldSpell(spell))
            {
                return;
            }

            SpellRanges.GetPlayerPosition(world, out int px, out int py);

            _spell = spell;
            _rules = profile.FieldPlacementRules;
            _tx = x;
            _ty = y;
            _pz = FieldPlacement.TryGetHoverTargetZ(x, y, out int hz) ? hz : z;
            _px = px;
            _py = py;
            _pzPlayer = world.Player.Z;
            _targetKind = targetKind;
            _inRange = !SpellRanges.TryGetRule(spell, out SpellRangeRule rule) || rule.Contains(x - px, y - py);
            _count = FieldPlacement.Predict(world, x, y, _pz, px, py, spell, _rules, _predicted);

            _existing.Clear();

            foreach (Item item in world.Items.Values)
            {
                if (item.OnGround && IsFieldGraphic(item.Graphic) && Math.Abs(item.X - x) <= SEARCH && Math.Abs(item.Y - y) <= SEARCH)
                {
                    _existing.Add(item.Serial);
                }
            }

            _sentAt = Time.Ticks;
            _pending = true;
        }

        /// <summary>Called every frame; writes the log once the server has had time to create the field.</summary>
        public static void Update(World world)
        {
            if (!_pending || Time.Ticks - _sentAt < COLLECT_AFTER_MS)
            {
                return;
            }

            _pending = false;

            if (world?.Player == null || world.Map == null)
            {
                return;
            }

            var actual = new List<Item>();

            foreach (Item item in world.Items.Values)
            {
                if (item.OnGround && IsFieldGraphic(item.Graphic) && !_existing.Contains(item.Serial)
                    && Math.Abs(item.X - _tx) <= SEARCH && Math.Abs(item.Y - _ty) <= SEARCH)
                {
                    actual.Add(item);
                }
            }

            var sb = new StringBuilder();
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int mismatches = 0, placedActual = 0;
            var matched = new HashSet<Item>();

            for (int i = 0; i < _count; i++)
            {
                var (x, y, placed) = _predicted[i];
                Item found = actual.Find(a => a.X == x && a.Y == y && !matched.Contains(a));

                if (found != null)
                {
                    matched.Add(found);
                    placedActual++;
                }

                bool isActual = found != null;

                if (isActual != placed)
                {
                    mismatches++;
                }

                AppendRow(world, sb, stamp, i - _count / 2, x, y, placed ? "Y" : "N", found);
            }

            foreach (Item extra in actual)
            {
                if (!matched.Contains(extra))
                {
                    mismatches++;
                    AppendRow(world, sb, stamp, 99, extra.X, extra.Y, "-", extra);
                }
            }

            Write(sb.ToString());

            string verdict = actual.Count == 0
                ? "no field appeared (out of range, fizzle or not the cast?)"
                : mismatches == 0 ? $"preview matched ({placedActual}/{_count} tiles)" : $"preview MISMATCH on {mismatches} tile(s)";

            GameActions.Print(world, $"Field check: {verdict} - logged to field_casts.csv", (ushort)(mismatches == 0 && actual.Count > 0 ? 0x0044 : 0x0022));
        }

        private static void AppendRow(World world, StringBuilder sb, string stamp, int offset, int x, int y, string predicted, Item actual)
        {
            FieldPlacement.GetLandInfo(world, x, y, out int low, out int avg, out int top);
            int statics = 0;
            var staticIds = new StringBuilder();

            for (GameObject o = world.Map.GetTile(x, y, false); o != null; o = o.TNext)
            {
                if (o is Static || o is Multi || o is Item it && !IsFieldGraphic(it.Graphic))
                {
                    statics++;

                    if (staticIds.Length > 0)
                    {
                        staticIds.Append(' ');
                    }

                    staticIds.Append($"0x{o.Graphic:X4}@{o.Z}");
                }
            }

            sb.Append(stamp).Append(',')
              .Append(SpellName(_spell)).Append(',')
              .Append(_rules).Append(',')
              .Append(_px).Append(',').Append(_py).Append(',').Append(_pzPlayer).Append(',')
              .Append(_tx).Append(',').Append(_ty).Append(',').Append(_pz).Append(',')
              .Append(_targetKind).Append(',')
              .Append(_inRange ? "Y" : "N").Append(',')
              .Append(offset == 99 ? "extra" : offset.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(x).Append(',').Append(y).Append(',')
              .Append(low).Append(',').Append(avg).Append(',').Append(top).Append(',')
              .Append(statics).Append(',').Append(staticIds).Append(',')
              .Append(predicted).Append(',')
              .Append(actual != null ? "Y" : "N").Append(',')
              .Append(actual != null ? actual.Z.ToString(CultureInfo.InvariantCulture) : "").Append(',')
              .Append(actual != null ? $"0x{actual.Graphic:X4}" : "")
              .AppendLine();
        }

        private static string SpellName(int spell)
        {
            switch (spell)
            {
                case 24: return "wall_of_stone";
                case 28: return "fire";
                case 39: return "poison";
                case 47: return "paralyze";
                case 50: return "energy";
                default: return spell.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void Write(string rows)
        {
            try
            {
                string dir = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "field_casts.csv");

                if (!File.Exists(path))
                {
                    File.WriteAllText(path, "time,spell,rules,player_x,player_y,player_z,target_x,target_y,target_z,target_kind,in_range,tile,x,y,land_low,land_avg,land_top,objects,object_ids,predicted,actual,actual_z,actual_graphic\n");
                }

                File.AppendAllText(path, rows);
            }
            catch (Exception ex)
            {
                Log.Warn($"field_casts.csv: {ex.Message}");
            }
        }
    }
}

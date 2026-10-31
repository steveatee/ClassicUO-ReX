// SPDX-License-Identifier: BSD-2-Clause
// Client-side friends list for consistent friend / enemy colouring.
//
// ClassicAssist's "rehue friends" rewrites hues inside a few incoming packet types, so any other packet that
// sets a mobile's hue (or a resync) reverts it, and Rehue() is expensive. Here the colour is applied when the
// mobile is drawn, so nothing the server sends can undo it and it costs nothing extra.
// The list is stored per character in the profile folder (friends.txt: "0xSERIAL name" per line).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal static class FriendsManager
    {
        private static readonly Dictionary<uint, string> _friends = new Dictionary<uint, string>();
        private static string _loadedPath;

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return _friends.Count;
            }
        }

        public static IEnumerable<KeyValuePair<uint, string>> All
        {
            get
            {
                EnsureLoaded();
                return _friends;
            }
        }

        public static bool IsFriend(uint serial)
        {
            EnsureLoaded();
            return _friends.ContainsKey(serial);
        }

        public static bool Add(Mobile mobile)
        {
            if (mobile == null)
            {
                return false;
            }

            EnsureLoaded();

            bool added = !_friends.ContainsKey(mobile.Serial);
            _friends[mobile.Serial] = mobile.Name ?? string.Empty;
            Save();

            return added;
        }

        public static bool Remove(uint serial)
        {
            EnsureLoaded();

            if (_friends.Remove(serial))
            {
                Save();
                return true;
            }

            return false;
        }

        /// <summary>
        /// The colour a mobile should be drawn with, or 0 to leave it alone.
        /// Friends: your list, plus party members and guild/alliance allies if enabled.
        /// Everyone else: humanoids only (or all mobiles), never invulnerable NPCs or yourself.
        /// </summary>
        public static ushort GetHue(World world, Mobile mobile)
        {
            Profile p = ProfileManager.CurrentProfile;

            if (p == null || !p.FriendEnemyHues || mobile == null || mobile.IsDead || world.Player == null || mobile.Serial == world.Player.Serial)
            {
                return 0;
            }

            if (IsFriend(mobile.Serial))
            {
                return p.FriendListHue;
            }

            if (p.FriendHueIncludeParty && world.Party.Contains(mobile.Serial))
            {
                return p.FriendListHue;
            }

            if (p.FriendHueIncludeAllies && mobile.NotorietyFlag == NotorietyFlag.Ally)
            {
                return p.FriendListHue;
            }

            if (mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
            {
                return 0;
            }

            if (p.EnemyHueHumanoidsOnly && !mobile.IsHuman)
            {
                return 0;
            }

            return p.EnemyListHue;
        }

        private static string GetPath()
        {
            string dir = ProfileManager.ProfilePath;
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "friends.txt");
        }

        private static void EnsureLoaded()
        {
            string path = GetPath();

            if (path == _loadedPath)
            {
                return;
            }

            _friends.Clear();
            _loadedPath = path;

            if (path == null || !File.Exists(path))
            {
                return;
            }

            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();

                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }

                    int space = line.IndexOf(' ');
                    string token = space > 0 ? line.Substring(0, space) : line;
                    string name = space > 0 ? line.Substring(space + 1).Trim() : string.Empty;

                    uint serial = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToUInt32(token.Substring(2), 16)
                        : uint.Parse(token);

                    _friends[serial] = name;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Friends] could not read {path}: {ex.Message}");
            }
        }

        private static void Save()
        {
            string path = GetPath();

            if (path == null)
            {
                return;
            }

            try
            {
                StringBuilder sb = new StringBuilder("# ReX friends list: 0xSERIAL name\n");

                foreach (KeyValuePair<uint, string> kv in _friends)
                {
                    sb.Append("0x").Append(kv.Key.ToString("X8")).Append(' ').Append(kv.Value).Append('\n');
                }

                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warn($"[Friends] could not save {path}: {ex.Message}");
            }
        }
    }
}

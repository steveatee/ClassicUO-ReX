// SPDX-License-Identifier: BSD-2-Clause
// ReX: remembers player names by serial across sessions, so party / guild / alliance dots on the world map
// keep their names after a relog instead of showing "<out of range>".
//
// File: Data/Client/known_players_<shard>.json (shard = "uodreams" on UO Dreams, else the login host), the same
// format as Mark's ClassicAssist macro writes ({"version":1,"players":{"0x1a2b":{"name":..,"last_seen":unix}}}).
// On load every Data/Client/known_players_<shard>*.json is merged (newest last_seen wins), so a copy of the
// macro's file can sit next to ReX's own. ReX only ever writes its own file.
//
// Who counts as a player: anything the server reports as a party or guild member, plus mobiles whose
// properties include "Queen's Loyalty" (UO Dreams players have it, NPCs don't) - the macro's rule.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal static class KnownPlayers
    {
        private const uint SCAN_INTERVAL_MS = 1_000;
        private const uint SAVE_INTERVAL_MS = 30_000;
        private const long TOUCH_SECONDS = 60;

        private sealed class Entry
        {
            public string Name;
            public long LastSeen;
        }

        private static readonly Dictionary<uint, Entry> _players = new Dictionary<uint, Entry>();
        private static string _shard;
        private static string _path;
        private static bool _dirty;
        private static uint _nextScan, _nextSave;

        private static readonly Regex _lordLady = new Regex(@"^(Lady|Lord)\s+", RegexOptions.Compiled);
        private static readonly string[] _strippedTags = { "[VvV]" }; // Vice vs Virtue flag; guild tags are kept

        public static bool Enabled => ProfileManager.CurrentProfile?.RememberPlayerNames != false;

        public static bool TryGetName(uint serial, out string name)
        {
            name = null;

            if (!Enabled || !_players.TryGetValue(serial, out Entry e) || string.IsNullOrEmpty(e.Name))
            {
                return false;
            }

            name = e.Name;

            return true;
        }

        /// <summary>Called every frame from the game scene.</summary>
        public static void Update(World world)
        {
            if (!Enabled || world?.Player == null || !world.InGame)
            {
                return;
            }

            EnsureLoaded(world);

            if (Time.Ticks >= _nextScan)
            {
                _nextScan = Time.Ticks + SCAN_INTERVAL_MS;
                Scan(world);
            }

            if (_dirty && Time.Ticks >= _nextSave)
            {
                _nextSave = Time.Ticks + SAVE_INTERVAL_MS;
                Save();
            }
        }

        /// <summary>A name the server tied to a party / guild member (or seen on the map).</summary>
        public static void Remember(uint serial, string name)
        {
            if (!Enabled || _path == null || !SerialHelper.IsMobile(serial))
            {
                return;
            }

            name = Canonical(name);

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (_players.TryGetValue(serial, out Entry e))
            {
                // Keep a guild-tagged form ("Name [TAG]") over the bare name the client sometimes has.
                bool sameButUntagged = e.Name != null && e.Name.StartsWith(name + " [", StringComparison.Ordinal);
                bool changed = !sameButUntagged && e.Name != name;

                if (!changed && now - e.LastSeen < TOUCH_SECONDS)
                {
                    return;
                }

                if (changed)
                {
                    e.Name = name;
                }

                e.LastSeen = now;
            }
            else
            {
                _players[serial] = new Entry { Name = name, LastSeen = now };
            }

            WMapEntity.CacheName(serial, _players[serial].Name);
            _dirty = true;
        }

        /// <summary>Saves now (logout / client close).</summary>
        public static void Flush()
        {
            if (_dirty)
            {
                Save();
            }
        }

        private static void Scan(World world)
        {
            foreach (Mobile m in world.Mobiles.Values)
            {
                if (m == null || m.IsDestroyed || m == world.Player || string.IsNullOrEmpty(m.Name))
                {
                    continue;
                }

                if (PlayerDetection.IsPlayer(world, m))
                {
                    Remember(m.Serial, m.Name);
                }
            }
        }

        private static string Canonical(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string s = _lordLady.Replace(name.Trim(), "");

            foreach (string tag in _strippedTags)
            {
                s = s.Replace(tag, "");
            }

            s = s.Trim();

            return s.Length == 0 || s == "None" ? null : s;
        }

        private static string ShardId(World world)
        {
            if (OplRequestPacing.IsUoDreams(world))
            {
                return "uodreams";
            }

            string host = Settings.GlobalSettings?.IP ?? world.ServerName ?? "shard";
            var sb = new StringBuilder();

            foreach (char c in host.ToLowerInvariant())
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return sb.Length > 0 ? sb.ToString() : "shard";
        }

        private static void EnsureLoaded(World world)
        {
            string shard = ShardId(world);

            if (_path != null && shard == _shard)
            {
                return;
            }

            Flush();
            _players.Clear();
            _shard = shard;

            string dir = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client");
            _path = Path.Combine(dir, $"known_players_{shard}.json");

            try
            {
                Directory.CreateDirectory(dir);

                // Our own file last, so on equal last_seen our name wins.
                var files = new List<string>(Directory.GetFiles(dir, $"known_players_{shard}*.json"));
                files.Remove(_path);

                if (File.Exists(_path))
                {
                    files.Add(_path);
                }

                foreach (string file in files)
                {
                    Merge(file);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"known players: {ex.Message}");
            }

            foreach (KeyValuePair<uint, Entry> kv in _players)
            {
                WMapEntity.CacheName(kv.Key, kv.Value.Name);
            }

            Log.Info($"known players: {_players.Count} names for {shard}");
        }

        private static void Merge(string file)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));

                if (!doc.RootElement.TryGetProperty("players", out JsonElement players) || players.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                foreach (JsonProperty p in players.EnumerateObject())
                {
                    string key = p.Name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? p.Name.Substring(2) : p.Name;

                    if (!uint.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint serial) || !SerialHelper.IsMobile(serial))
                    {
                        continue;
                    }

                    string name = p.Value.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    long seen = p.Value.TryGetProperty("last_seen", out JsonElement ls) && ls.TryGetInt64(out long v) ? v : 0;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (!_players.TryGetValue(serial, out Entry e) || seen >= e.LastSeen)
                    {
                        _players[serial] = new Entry { Name = name.Trim(), LastSeen = seen };
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"known players: couldn't read {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        private static void Save()
        {
            if (_path == null)
            {
                return;
            }

            try
            {
                var keys = new List<uint>(_players.Keys);
                keys.Sort((a, b) => string.CompareOrdinal(Key(a), Key(b)));

                using var stream = new MemoryStream();

                using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    w.WriteStartObject();
                    w.WriteStartObject("players");

                    foreach (uint serial in keys)
                    {
                        Entry e = _players[serial];
                        w.WriteStartObject(Key(serial));
                        w.WriteNumber("last_seen", e.LastSeen);
                        w.WriteString("name", e.Name);
                        w.WriteEndObject();
                    }

                    w.WriteEndObject();
                    w.WriteNumber("version", 1);
                    w.WriteEndObject();
                }

                string tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, stream.ToArray());
                File.Move(tmp, _path, true);
                _dirty = false;
            }
            catch (Exception ex)
            {
                Log.Warn($"known players: couldn't save: {ex.Message}");
            }
        }

        private static string Key(uint serial) => "0x" + serial.ToString("x", CultureInfo.InvariantCulture);
    }
}

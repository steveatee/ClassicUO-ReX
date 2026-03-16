// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClassicUO.Game.Managers
{
    internal sealed class TileMarkerManager
    {
        public static TileMarkerManager Instance { get; } = new TileMarkerManager();

        private readonly Dictionary<long, ushort> _markedTiles = new Dictionary<long, ushort>();
        private bool _isLoaded;

        private TileMarkerManager()
        {
        }

        public void AddTile(int x, int y, int map, ushort hue)
        {
            EnsureLoaded();
            _markedTiles[MakeKey(x, y, map)] = hue;
        }

        public void RemoveTile(int x, int y, int map)
        {
            EnsureLoaded();
            _markedTiles.Remove(MakeKey(x, y, map));
        }

        public bool IsTileMarked(int x, int y, int map, out ushort hue)
        {
            EnsureLoaded();
            return _markedTiles.TryGetValue(MakeKey(x, y, map), out hue);
        }

        public void Save()
        {
            EnsureLoaded();

            try
            {
                string profilePath = ProfileManager.ProfilePath;

                if (string.IsNullOrEmpty(profilePath))
                {
                    return;
                }

                Directory.CreateDirectory(profilePath);
                string json = JsonSerializer.Serialize(_markedTiles);
                File.WriteAllText(GetSavePath(profilePath), json);
            }
            catch
            {
            }
        }

        private void EnsureLoaded()
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;

            try
            {
                string profilePath = ProfileManager.ProfilePath;

                if (string.IsNullOrEmpty(profilePath))
                {
                    return;
                }

                string savePath = GetSavePath(profilePath);

                if (!File.Exists(savePath))
                {
                    return;
                }

                string json = File.ReadAllText(savePath);
                var data = JsonSerializer.Deserialize<Dictionary<long, ushort>>(json);

                if (data != null)
                {
                    foreach (var entry in data)
                    {
                        _markedTiles[entry.Key] = entry.Value;
                    }
                }
            }
            catch
            {
            }
        }

        private static string GetSavePath(string profilePath)
        {
            return Path.Combine(profilePath, "tilemarkers.json");
        }

        private static long MakeKey(int x, int y, int map)
        {
            unchecked
            {
                return ((long)map & 0xFFFFL) << 48
                     | ((long)x & 0xFFFFFFL) << 24
                     | ((long)y & 0xFFFFFFL);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClassicUO.Game.Managers
{
    internal class TileMarkerManager
    {
        public static TileMarkerManager Instance { get; private set; } = new TileMarkerManager();

        private Dictionary<string, ushort> markedTiles = new Dictionary<string, ushort>();

        private string savePath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles", "TileMarkers.json");

        private TileMarkerManager() { Load(); }

        public void AddTile(int x, int y, int map, ushort hue)
        {
            markedTiles[FormatLocKey(x, y, map)] = hue; // indexer handles add and update
        }

        public void RemoveTile(int x, int y, int map)
        {
            markedTiles.Remove(FormatLocKey(x, y, map));
        }

        public bool IsTileMarked(int x, int y, int map, out ushort hue)
        {
            return markedTiles.TryGetValue(FormatLocKey(x, y, map), out hue);
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var lines = new List<string>();
                foreach (var kvp in markedTiles)
                    lines.Add($"{kvp.Key}={kvp.Value}");

                File.WriteAllLines(savePath, lines);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TileMarker] Failed to save: {ex.Message}");
            }
        }

        private void Load()
        {
            if (!File.Exists(savePath))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(savePath))
                {
                    int sep = line.LastIndexOf('=');
                    if (sep < 0) continue;

                    string key = line.Substring(0, sep);
                    string val = line.Substring(sep + 1);

                    if (ushort.TryParse(val, out ushort hue))
                        markedTiles[key] = hue;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TileMarker] Failed to load: {ex.Message}");
            }
        }

        private static string FormatLocKey(int x, int y, int map)
        {
            return $"{x}.{y}.{map}";
        }
    }
}
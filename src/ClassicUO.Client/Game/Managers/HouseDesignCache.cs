// SPDX-License-Identifier: BSD-2-Clause
// Persistent cache of custom house designs (packet 0xD8), keyed by house serial + design revision.
//
// Stock behaviour: when a custom house leaves view range (or the server deletes the foundation from the
// client), the House and its design are dropped. On the way back the server sends the house's revision
// (0xBF/0x1D); with nothing to compare against, the client requests the full design again. Designs only
// change when the owner commits a new one (which bumps the revision), so the same bytes get downloaded
// over and over. This cache keeps the raw 0xD8 payload per serial and revision, on disk per server, and
// the revision handler replays it instead of requesting it. A changed house has a new revision, so it is
// still requested normally.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal static class HouseDesignCache
    {
        private const uint FILE_MAGIC = 0x48444331; // "HDC1"
        private const uint SAVE_INTERVAL_MS = 30_000;

        private readonly struct Entry
        {
            public Entry(uint revision, byte[] data)
            {
                Revision = revision;
                Data = data;
            }

            public readonly uint Revision;
            public readonly byte[] Data;
        }

        private static readonly Dictionary<uint, Entry> _entries = new Dictionary<uint, Entry>();
        private static readonly object _saveLock = new object();
        private static string _loadedKey;
        private static bool _dirty;
        private static uint _nextSave;

        public static int Hits { get; private set; }
        public static int Misses { get; private set; }
        public static int Stored { get; private set; }
        public static long BytesSaved { get; private set; }
        public static int Count => _entries.Count;

        public static bool Enabled => ProfileManager.CurrentProfile?.CacheCustomHouseDesigns ?? true;

        /// <summary>Returns the cached 0xD8 payload (from the byte after the packet length) for this exact revision.</summary>
        public static bool TryGet(World world, uint serial, uint revision, out byte[] data)
        {
            data = null;

            if (!Enabled)
            {
                return false;
            }

            EnsureLoaded(world);

            if (_entries.TryGetValue(serial, out Entry e) && e.Revision == revision)
            {
                data = e.Data;
                Hits++;
                BytesSaved += data.Length;
                return true;
            }

            Misses++;
            return false;
        }

        public static void Store(World world, uint serial, uint revision, ReadOnlySpan<byte> payload)
        {
            if (!Enabled || payload.IsEmpty)
            {
                return;
            }

            EnsureLoaded(world);

            if (_entries.TryGetValue(serial, out Entry e) && e.Revision == revision && e.Data.Length == payload.Length)
            {
                return;
            }

            _entries[serial] = new Entry(revision, payload.ToArray());
            Stored++;
            _dirty = true;
        }

        /// <summary>Called every frame from the world update; writes changes to disk at most every 30 seconds.</summary>
        public static void Update()
        {
            if (_dirty && Time.Ticks >= _nextSave)
            {
                _nextSave = Time.Ticks + SAVE_INTERVAL_MS;
                SaveAsync();
            }
        }

        /// <summary>Called on logout / disconnect: flush and forget the per-server data.</summary>
        public static void Flush()
        {
            if (_dirty)
            {
                Save(_loadedKey, Snapshot());
                _dirty = false;
            }

            _entries.Clear();
            _loadedKey = null;
        }

        public static void Clear(World world)
        {
            EnsureLoaded(world);
            _entries.Clear();
            _dirty = true;

            try
            {
                string path = GetPath(_loadedKey);

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[HouseDesignCache] clear failed: {ex.Message}");
            }
        }

        private static string GetKey(World world)
        {
            string server = world?.ServerName ?? "_";
            string ip = Settings.GlobalSettings.IP ?? "";

            return $"{ip}_{server}";
        }

        private static string GetPath(string key)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                key = key.Replace(c, '_');
            }

            return Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client", "HouseDesignCache", key + ".bin");
        }

        private static void EnsureLoaded(World world)
        {
            string key = GetKey(world);

            if (key == _loadedKey)
            {
                return;
            }

            if (_loadedKey != null && _dirty)
            {
                Save(_loadedKey, Snapshot());
            }

            _entries.Clear();
            _dirty = false;
            _loadedKey = key;

            string path = GetPath(key);

            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                using FileStream fs = File.OpenRead(path);
                using BinaryReader reader = new BinaryReader(fs);

                if (reader.ReadUInt32() != FILE_MAGIC)
                {
                    return;
                }

                int count = reader.ReadInt32();

                for (int i = 0; i < count; i++)
                {
                    uint serial = reader.ReadUInt32();
                    uint revision = reader.ReadUInt32();
                    int len = reader.ReadInt32();

                    if (len <= 0 || len > 0x10000)
                    {
                        _entries.Clear();
                        return;
                    }

                    _entries[serial] = new Entry(revision, reader.ReadBytes(len));
                }

                Log.Trace($"[HouseDesignCache] loaded {_entries.Count} house designs for {key}");
            }
            catch (Exception ex)
            {
                Log.Warn($"[HouseDesignCache] load failed, starting empty: {ex.Message}");
                _entries.Clear();
            }
        }

        private static KeyValuePair<uint, Entry>[] Snapshot()
        {
            KeyValuePair<uint, Entry>[] items = new KeyValuePair<uint, Entry>[_entries.Count];
            ((ICollection<KeyValuePair<uint, Entry>>)_entries).CopyTo(items, 0);
            return items;
        }

        private static void SaveAsync()
        {
            string key = _loadedKey;

            if (key == null)
            {
                return;
            }

            KeyValuePair<uint, Entry>[] items = Snapshot();
            _dirty = false;
            Task.Run(() => Save(key, items));
        }

        private static void Save(string key, KeyValuePair<uint, Entry>[] items)
        {
            if (key == null)
            {
                return;
            }

            lock (_saveLock)
            {
                try
                {
                    string path = GetPath(key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string tmp = path + ".tmp";

                    using (FileStream fs = File.Create(tmp))
                    using (BinaryWriter writer = new BinaryWriter(fs))
                    {
                        writer.Write(FILE_MAGIC);
                        writer.Write(items.Length);

                        foreach (KeyValuePair<uint, Entry> kv in items)
                        {
                            writer.Write(kv.Key);
                            writer.Write(kv.Value.Revision);
                            writer.Write(kv.Value.Data.Length);
                            writer.Write(kv.Value.Data);
                        }
                    }

                    File.Move(tmp, path, true);
                }
                catch (Exception ex)
                {
                    Log.Warn($"[HouseDesignCache] save failed: {ex.Message}");
                }
            }
        }
    }
}

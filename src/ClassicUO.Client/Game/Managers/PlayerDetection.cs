// SPDX-License-Identifier: BSD-2-Clause
// ReX: tells players from NPCs that look human. On UO Dreams every player has the "Queen's Loyalty" property and
// NPCs don't. A mobile whose property list is just its name (one line) is ruled out straight away; otherwise the
// lines are searched once per property revision and the answer is cached. Until the properties arrive the
// mobile counts as "not a player". Other shards: no such property, so callers fall back to their old rule.

using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers
{
    internal static class PlayerDetection
    {
        private const string MARKER = "Queen's Loyalty";

        private readonly struct Cached
        {
            public Cached(uint revision, bool isPlayer)
            {
                Revision = revision;
                IsPlayer = isPlayer;
            }

            public uint Revision { get; }
            public bool IsPlayer { get; }
        }

        private static readonly Dictionary<uint, Cached> _cache = new Dictionary<uint, Cached>();

        /// <summary>True when this shard marks players with a property (UO Dreams).</summary>
        public static bool IsAvailable(World world) => OplRequestPacing.IsUoDreams(world);

        public static bool IsPlayer(World world, Mobile mobile)
        {
            if (mobile == null || world?.OPL == null)
            {
                return false;
            }

            uint serial = mobile.Serial;

            if (!world.OPL.TryGetRevision(serial, out uint revision))
            {
                return false; // properties not here yet
            }

            if (_cache.TryGetValue(serial, out Cached c) && c.Revision == revision)
            {
                return c.IsPlayer;
            }

            bool isPlayer = false;

            // one line = just the name (count 0 = clilocs not kept, so read the text)
            if (world.OPL.GetClilocCount(serial) != 1 && world.OPL.TryGetNameAndData(serial, out _, out string data) && data != null)
            {
                isPlayer = data.IndexOf(MARKER, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (_cache.Count > 4096)
            {
                _cache.Clear();
            }

            _cache[serial] = new Cached(revision, isPlayer);

            return isPlayer;
        }
    }
}

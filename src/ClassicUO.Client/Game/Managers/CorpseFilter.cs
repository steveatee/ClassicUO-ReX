using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;
using System;
using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    // "Hide non-player corpses" (ReX). On UO Dreams / TrueUO every corpse is 0x2006, and the only
    // thing that tells a player corpse apart is its name: corpses without a [CorpseName] attribute
    // (players, and humanoid NPCs like brigands) are "the remains of <name>"; monsters have their
    // own corpse names ("a ghoul corpse", ...). The decision waits for the corpse's properties (the
    // item name can be wrong at first); until they arrive the corpse stays visible, so a player
    // corpse never flickers out.
    internal static class CorpseFilter
    {
        private const string PLAYER_CORPSE_TEXT = "remains of";
        private const int PLAYER_CORPSE_CLILOC = 1046414;

        // serial -> true if the corpse should be hidden. Only filled once the name is known.
        private static readonly Dictionary<uint, bool> _hidden = new Dictionary<uint, bool>();
        private static readonly Dictionary<uint, uint> _requested = new Dictionary<uint, uint>(); // serial -> next retry

        public static bool ShouldHide(World world, Item corpse)
        {
            if (ProfileManager.CurrentProfile == null || !ProfileManager.CurrentProfile.HideNonPlayerCorpses)
            {
                return false;
            }

            if (_hidden.TryGetValue(corpse.Serial, out bool hide))
            {
                return hide;
            }

            // Only decide once the properties have arrived: the item's own name can be wrong at
            // first, the first property line is the real name.
            if (!world.OPL.TryGetNameAndData(corpse.Serial, out string oplName, out _) || string.IsNullOrEmpty(oplName))
            {
                // Ask once; the server normally sends the property revision for corpses anyway.
                if (world.ClientFeatures.TooltipsEnabled && (!_requested.TryGetValue(corpse.Serial, out uint retry) || Time.Ticks >= retry))
                {
                    _requested[corpse.Serial] = Time.Ticks + 3000;
                    PacketHandlers.AddMegaClilocRequest(corpse.Serial);
                }

                return false;
            }

            // Like the old ClassicAssist macro: keep it if either the properties or the item name
            // say "remains of" (or the name line is cliloc 1046414, "the remains of ~1_NAME~").
            bool playerLike =
                world.OPL.GetNameCliloc(corpse.Serial) == PLAYER_CORPSE_CLILOC
                || oplName.IndexOf(PLAYER_CORPSE_TEXT, StringComparison.OrdinalIgnoreCase) >= 0
                || (corpse.Name != null && corpse.Name.IndexOf(PLAYER_CORPSE_TEXT, StringComparison.OrdinalIgnoreCase) >= 0);

            hide = !playerLike;

            if (_hidden.Count > 4096)
            {
                _hidden.Clear();
                _requested.Clear();
            }

            _hidden[corpse.Serial] = hide;

            return hide;
        }

        // A corpse's properties changed (or it was removed); work it out again next frame.
        public static void Forget(uint serial)
        {
            _hidden.Remove(serial);
        }
    }
}

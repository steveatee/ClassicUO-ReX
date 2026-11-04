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
    // own corpse names ("a ghoul corpse", ...). Corpses whose name we don't know yet stay visible,
    // so a player corpse never flickers out.
    internal static class CorpseFilter
    {
        private const string PLAYER_CORPSE_TEXT = "remains of";

        // serial -> true if the corpse should be hidden. Only filled once the name is known.
        private static readonly Dictionary<uint, bool> _hidden = new Dictionary<uint, bool>();
        private static readonly HashSet<uint> _requested = new HashSet<uint>();

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

            string name = null;

            if (world.OPL.TryGetNameAndData(corpse.Serial, out string oplName, out _) && !string.IsNullOrEmpty(oplName))
            {
                name = oplName;
            }
            else if (!string.IsNullOrEmpty(corpse.Name))
            {
                name = corpse.Name;
            }

            if (name == null)
            {
                // Ask once; the server normally sends the property revision for corpses anyway.
                if (world.ClientFeatures.TooltipsEnabled && _requested.Add(corpse.Serial))
                {
                    PacketHandlers.AddMegaClilocRequest(corpse.Serial);
                }

                return false;
            }

            hide = name.IndexOf(PLAYER_CORPSE_TEXT, StringComparison.OrdinalIgnoreCase) < 0;

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

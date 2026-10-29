// SPDX-License-Identifier: BSD-2-Clause
// Visual and field helpers ported from Dust765 Light (CombatCollection, LTHighlightRangeHelper,
// FieldBlockTileData, transparent houses), adapted to ReX's chunk-mesh renderer.

using System;
using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers
{
    internal static class DustLightVisuals
    {
        public const ushort FIELD_PREVIEW_HUE = 0x0040;
        public const ushort CASTING_SELF_HUE = 0x0023;

        private const ushort BRIGHT_WHITE_COLOR = 0x080A;
        private const ushort BRIGHT_PINK_COLOR = 0x0503;
        private const ushort BRIGHT_ICE_COLOR = 0x0480;
        private const ushort BRIGHT_FIRE_COLOR = 0x0496;
        private const ushort BRIGHT_POISON_COLOR = 0x0A0B;
        private const ushort BRIGHT_PARALYZE_COLOR = 0x0A13;

        private const ushort WOS_REPLACEMENT_HUE = 945;
        private const ushort ENERGY_FIELD_REPLACEMENT_HUE = 293;

        // ------- Stealth -------

        public static bool IsStealthArt(ushort graphic) => graphic >= 0x1E03 && graphic <= 0x1E06;

        public static bool IsStealthTintEnabled(Profile profile) =>
            profile != null && (profile.ColorStealth || profile.StealthNeonType != 0);

        public static ushort StealthHue(ushort hue)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return hue;
            }

            if (profile.ColorStealth)
            {
                hue = profile.StealthHue;
            }

            return ApplyNeon(hue, profile.StealthNeonType, hue);
        }

        // ------- Last target -------

        public static bool TryGetLastTargetHue(Mobile mobile, ushort hue, out ushort result)
        {
            result = hue;
            Profile profile = ProfileManager.CurrentProfile;

            if (
                profile == null
                || profile.HighlightLastTargetType == 0
                    && profile.HighlightLastTargetTypePoison == 0
                    && profile.HighlightLastTargetTypePara == 0
            )
            {
                return false;
            }

            result = ApplyNeon(hue, profile.HighlightLastTargetType, profile.HighlightLastTargetTypeHue);

            if (mobile.IsPoisoned && profile.HighlightLastTargetTypePoison != 0)
            {
                result = profile.HighlightLastTargetTypePoison == 6
                    ? BRIGHT_POISON_COLOR
                    : ApplyNeon(result, profile.HighlightLastTargetTypePoison, profile.HighlightLastTargetTypePoisonHue);
            }

            if (mobile.IsParalyzed && mobile.NotorietyFlag != NotorietyFlag.Invulnerable && profile.HighlightLastTargetTypePara != 0)
            {
                result = profile.HighlightLastTargetTypePara == 6
                    ? BRIGHT_PARALYZE_COLOR
                    : ApplyNeon(result, profile.HighlightLastTargetTypePara, profile.HighlightLastTargetTypeParaHue);
            }

            return result != hue || profile.HighlightLastTargetType != 0;
        }

        private static ushort ApplyNeon(ushort hue, int neonType, ushort customHue)
        {
            return neonType switch
            {
                1 => BRIGHT_WHITE_COLOR,
                2 => BRIGHT_PINK_COLOR,
                3 => BRIGHT_ICE_COLOR,
                4 => BRIGHT_FIRE_COLOR,
                5 => customHue,
                _ => hue
            };
        }

        // ------- Field preview -------

        /// <summary>
        /// True when a field spell (Wall of Stone, Fire, Energy, Poison, Paralyze Field) is on the cursor and
        /// the tile at (x, y) would be covered by the field if cast on the tile under the mouse.
        /// </summary>
        public static bool IsInFieldPreview(World world, int x, int y)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || !profile.PreviewFields || world?.Player == null || !world.TargetManager.IsTargeting)
            {
                return false;
            }

            int spellIndex = GameActions.LastSpellIndex;

            if (spellIndex != 24 && spellIndex != 28 && spellIndex != 39 && spellIndex != 47 && spellIndex != 50)
            {
                return false;
            }

            if (SelectedObject.Object is not GameObject target)
            {
                return false;
            }

            int targetX = target.X;
            int targetY = target.Y;
            bool eastToWest = Math.Abs(targetX - world.Player.X) <= Math.Abs(targetY - world.Player.Y);

            return eastToWest
                ? y == targetY && x >= targetX - 2 && x <= targetX + 2
                : x == targetX && y >= targetY - 2 && y <= targetY + 2;
        }

        // ------- Range ring -------

        /// <summary>Hue for land tiles exactly N tiles from the player (while casting, or always on).</summary>
        public static bool TryGetRangeRingHue(World world, int x, int y, out ushort hue)
        {
            hue = 0;
            Profile p = ProfileManager.CurrentProfile;

            if (p == null || world?.Player == null || !p.LTHighlightRangeOnCast && !p.LTHighlightRangeOnActivated)
            {
                return false;
            }

            int px = world.Player.X;
            int py = world.Player.Y;

            if (world.Player.Steps.Count != 0)
            {
                ref Mobile.Step st = ref world.Player.Steps.Back();
                px = st.X;
                py = st.Y;
            }

            int d = Math.Max(Math.Abs(x - px), Math.Abs(y - py));

            if (p.LTHighlightRangeOnCast && world.Player.IsCasting && d == p.LTHighlightRangeOnCastRange)
            {
                hue = p.LTHighlightRangeOnCastHue;
                return true;
            }

            if (p.LTHighlightRangeOnActivated && d == p.LTHighlightRangeOnActivatedRange)
            {
                hue = p.LTHighlightRangeOnActivatedHue;
                return true;
            }

            return false;
        }

        /// <summary>Combined ground overlay: field preview wins over the range ring.</summary>
        public static bool TryGetGroundOverlayHue(World world, GameObject obj, out ushort hue)
        {
            if (IsInFieldPreview(world, obj.X, obj.Y))
            {
                hue = FIELD_PREVIEW_HUE;
                return true;
            }

            if (obj is Land)
            {
                return TryGetRangeRingHue(world, obj.X, obj.Y, out hue);
            }

            hue = 0;
            return false;
        }

        // ------- Houses -------

        public static bool IsHousePieceAbove(World world, GameObject obj, int zThreshold)
        {
            if (world?.Player == null || world.Map == null)
            {
                return false;
            }

            GameObject groundTile = world.Map.GetTile(obj.X, obj.Y);

            return groundTile != null
                && (obj.Z - world.Player.Z) > zThreshold
                && (obj.Z - groundTile.Z) > ProfileManager.CurrentProfile.DontRemoveHouseBelowZ;
        }

        /// <summary>Static/multi house pieces that should be drawn see-through (Dust-Light "transparent houses").</summary>
        public static bool IsTransparentHousePiece(World world, GameObject obj)
        {
            Profile p = ProfileManager.CurrentProfile;

            return p != null
                && p.TransparentHousesEnabled
                && (obj is Static || obj is Multi || obj is Item)
                && IsHousePieceAbove(world, obj, p.TransparentHousesZ);
        }

        public static float TransparentHouseAlpha =>
            Math.Clamp(ProfileManager.CurrentProfile?.TransparentHousesTransparency ?? 5, 1, 9) / 10f;

        // ------- Wall of Stone / Energy Field -------

        /// <summary>Draw-time art swap for WoS / Energy Field (the item keeps its real graphic for pathing).</summary>
        public static bool TryReplaceFieldArt(ushort graphic, ushort hue, out ushort newGraphic, out ushort newHue)
        {
            newGraphic = graphic;
            newHue = hue;
            Profile p = ProfileManager.CurrentProfile;

            if (p == null)
            {
                return false;
            }

            if (p.BlockWoSArtForceAoS && StaticFilters.IsWallOfStone(graphic))
            {
                newGraphic = (ushort)p.BlockWoSArt;
                newHue = WOS_REPLACEMENT_HUE;
                return true;
            }

            if (p.BlockEnergyFArtForceAoS && (StaticFilters.IsEnergyField(graphic) || graphic == 0x28A8 && hue == 0x0125))
            {
                newGraphic = (ushort)p.BlockEnergyFArt;
                newHue = ENERGY_FIELD_REPLACEMENT_HUE;
                return true;
            }

            return false;
        }

        private static readonly Dictionary<ushort, bool> _originalImpassable = new Dictionary<ushort, bool>();

        /// <summary>
        /// Marks Wall of Stone / Energy Field graphics impassable in the client tiledata so the walker,
        /// obstacle dodging and pathfinder won't try to step into them (the server refuses those steps).
        /// Called when a field item is drawn; the original flag is restored when the option is turned off.
        /// </summary>
        public static void UpdateFieldPassability(World world, ushort graphic)
        {
            Profile p = ProfileManager.CurrentProfile;

            if (p == null)
            {
                return;
            }

            bool isWoS = StaticFilters.IsWallOfStone(graphic);
            bool isEnergy = StaticFilters.IsEnergyField(graphic);

            if (!isWoS && !isEnergy)
            {
                return;
            }

            bool enabled = isWoS ? p.BlockWoS : p.BlockEnergyF;
            bool felOnly = isWoS ? p.BlockWoSFelOnly : p.BlockEnergyFFelOnly;

            if (enabled && !(felOnly && world.MapIndex != 0))
            {
                SetImpassable(graphic, true);
            }
            else
            {
                RestoreImpassable(graphic);
            }
        }

        /// <summary>Restores every field graphic we changed (used when options change).</summary>
        public static void RestoreAllFieldPassability()
        {
            if (_originalImpassable.Count == 0)
            {
                return;
            }

            ushort[] graphics = new ushort[_originalImpassable.Count];
            _originalImpassable.Keys.CopyTo(graphics, 0);

            foreach (ushort g in graphics)
            {
                RestoreImpassable(g);
            }
        }

        private static void SetImpassable(ushort graphic, bool impassable)
        {
            StaticTiles[] tiles = Client.Game.UO.FileManager.TileData.StaticData;

            if (graphic >= tiles.Length)
            {
                return;
            }

            ref StaticTiles st = ref tiles[graphic];

            if (!_originalImpassable.ContainsKey(graphic))
            {
                _originalImpassable[graphic] = (st.Flags & TileFlag.Impassable) != 0;
            }

            if (impassable)
            {
                st.Flags |= TileFlag.Impassable;
            }
            else
            {
                st.Flags &= ~TileFlag.Impassable;
            }
        }

        private static void RestoreImpassable(ushort graphic)
        {
            if (!_originalImpassable.TryGetValue(graphic, out bool original))
            {
                return;
            }

            SetImpassable(graphic, original);
            _originalImpassable.Remove(graphic);
        }
    }
}

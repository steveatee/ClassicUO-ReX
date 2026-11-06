// SPDX-License-Identifier: BSD-2-Clause
// Visual and field helpers ported from Dust765 Light (CombatCollection, LTHighlightRangeHelper,
// FieldBlockTileData, transparent houses), adapted to ReX's chunk-mesh renderer.

using System;
using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.Managers
{
    internal static class DustLightVisuals
    {
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
        /// True when a field spell is on the cursor and the server will create a field tile at (x, y)
        /// (see <see cref="FieldPlacement"/> for the per-server rules).
        /// </summary>
        public static bool IsInFieldPreview(World world, int x, int y)
        {
            return FieldPlacement.TryGetTileState(world, x, y, out bool placed) && placed;
        }

        // ------- Range ring -------

        /// <summary>
        /// The range ring to show now, if any: while casting / holding a spell's target cursor (the on-cast ring),
        /// otherwise the always-on ring.
        /// </summary>
        public static bool TryGetActiveRangeRing(World world, out int range, out ushort hue)
        {
            range = 0;
            hue = 0;
            Profile p = ProfileManager.CurrentProfile;

            if (p == null || world?.Player == null)
            {
                return false;
            }

            if (p.LTHighlightRangeOnCast && (world.Player.IsCasting || world.TargetManager.IsTargeting && world.TargetManager.IsSpellTarget))
            {
                range = p.LTHighlightRangeOnCastRange;
                hue = p.LTHighlightRangeOnCastHue;
                return true;
            }

            if (p.LTHighlightRangeOnActivated)
            {
                range = p.LTHighlightRangeOnActivatedRange;
                hue = p.LTHighlightRangeOnActivatedHue;
                return true;
            }

            return false;
        }

        private static ushort _ringColourHue = 0xFFFF;
        private static Color _ringColour;

        /// <summary>
        /// Draws the range ring as a line along the OUTER edge of the last tile in range: everything inside the line
        /// (including the tiles it runs along) is within N tiles, the same square-distance check the server uses
        /// (Mobile.InRange). Called in the ground overlay pass so statics and mobiles still draw over it.
        /// </summary>
        public static void DrawRangeRing(World world, UltimaBatcher2D batcher)
        {
            if (!TryGetActiveRangeRing(world, out int range, out ushort hue) || range <= 0 || world.Map == null)
            {
                return;
            }

            PlayerMobile player = world.Player;
            int px = player.X;
            int py = player.Y;

            if (player.Steps.Count != 0)
            {
                ref Mobile.Step st = ref player.Steps.Back();
                px = st.X;
                py = st.Y;
            }

            // Recover this frame's screen offset from the player (GameObject.UpdateRealScreenPosition), so tiles
            // that aren't on screen this frame still get correct positions.
            int offX = (player.X - player.Y) * 22 - 22 - player.RealScreenPosition.X;
            int offY = (player.X + player.Y) * 22 - (player.Z << 2) - 22 - player.RealScreenPosition.Y;

            if (hue != _ringColourHue)
            {
                _ringColourHue = hue;
                uint c = Client.Game.UO.FileManager.Hues.GetPolygoneColor(24, hue);
                _ringColour = new Color((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
            }

            Texture2D line = SolidColorTextureCache.GetTexture(_ringColour * 0.95f);
            Texture2D shadow = SolidColorTextureCache.GetTexture(Color.Black * 0.45f);

            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    if (Math.Abs(dx) != range && Math.Abs(dy) != range)
                    {
                        continue;
                    }

                    int x = px + dx;
                    int y = py + dy;
                    Land land = GetLand(world, x, y);

                    if (land == null || land.IsDestroyed)
                    {
                        continue;
                    }

                    GetCorners(land, x, y, offX, offY, out Vector2 top, out Vector2 right, out Vector2 bottom, out Vector2 left);
                    float depth = land.CalculateDepthZ() + 0.8f;

                    // Corners: top = (x, y), right = (x+1, y), bottom = (x+1, y+1), left = (x, y+1).
                    if (dy == -range) DrawEdge(batcher, line, shadow, top, right, depth);   // north side
                    if (dy == range) DrawEdge(batcher, line, shadow, left, bottom, depth);  // south side
                    if (dx == -range) DrawEdge(batcher, line, shadow, top, left, depth);    // west side
                    if (dx == range) DrawEdge(batcher, line, shadow, right, bottom, depth); // east side
                }
            }
        }

        private static readonly Vector3 _ringHueVector = new Vector3(0f, ShaderHueTranslator.SHADER_NONE, 1f);

        private static void DrawEdge(UltimaBatcher2D batcher, Texture2D line, Texture2D shadow, Vector2 a, Vector2 b, float depth)
        {
            batcher.DrawLine(shadow, a + new Vector2(0, 1), b + new Vector2(0, 1), _ringHueVector, 3f, depth);
            batcher.DrawLine(line, a, b, _ringHueVector, 2f, depth);
        }

        private static void GetCorners(Land land, int x, int y, int offX, int offY, out Vector2 top, out Vector2 right, out Vector2 bottom, out Vector2 left)
        {
            int posX = (x - y) * 22 - offX - 22;
            int posY = (x + y) * 22 - (land.Z << 2) - offY - 22;

            if (land.IsStretched)
            {
                posY += land.Z << 2;
                top = new Vector2(posX + 22, posY - land.YOffsets.Top);
                right = new Vector2(posX + 44, posY + (22 - land.YOffsets.Right));
                left = new Vector2(posX, posY + (22 - land.YOffsets.Left));
                bottom = new Vector2(posX + 22, posY + (44 - land.YOffsets.Bottom));
            }
            else
            {
                top = new Vector2(posX + 22, posY);
                right = new Vector2(posX + 44, posY + 22);
                left = new Vector2(posX, posY + 22);
                bottom = new Vector2(posX + 22, posY + 44);
            }
        }

        private static Land GetLand(World world, int x, int y)
        {
            for (GameObject o = world.Map.GetTile(x, y, false); o != null; o = o.TNext)
            {
                if (o is Land l)
                {
                    return l;
                }
            }

            return null;
        }

        /// <summary>Ground hue overlays. The range ring is now a line (DrawRangeRing) and the field preview an
        /// overlay (FieldPlacement.DrawOverlay), so nothing is hued here any more.</summary>
        public static bool TryGetGroundOverlayHue(World world, GameObject obj, out ushort hue)
        {
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
                && !InvisibleHousesState.PlayerInsideHouse
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

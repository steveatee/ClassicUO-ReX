// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Map;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    internal static class FieldPreviewManager
    {
        private const ushort PREVIEW_HUE = 0x0040;
        private const ushort PREVIEW_CENTER_HUE = 0x0023;
        private const int FIELD_RANGE = 10;
        private static readonly PreviewPalette DefaultPalette = new(
            new Color(160, 240, 110, 88),
            new Color(255, 160, 210, 88),
            new Color(94, 170, 38, 185),
            new Color(214, 86, 162, 185)
        );
        private static readonly PreviewPalette OutOfRangePalette = new(
            new Color(255, 120, 90, 96),
            new Color(255, 176, 128, 96),
            new Color(180, 52, 40, 200),
            new Color(215, 98, 54, 200)
        );

        public static bool IsEnabled(World world)
        {
            GameObject target = SelectedObject.Object as GameObject;

            return world != null
                   && world.TargetManager.IsTargeting
                   && ProfileManager.CurrentProfile?.PreviewFieldTiles == true
                   && target != null
                   && IsSupportedSpell(GameActions.LastSpellIndexCursor);
        }

        public static bool TryGetPreviewHue(World world, GameObject obj, out ushort hue)
        {
            hue = 0;

            if (!IsEnabled(world) || obj == null)
                return false;

            GameObject target = SelectedObject.Object as GameObject;

            if (target == null)
                return false;

            if (ReferenceEquals(SelectedObject.Object, obj))
            {
                hue = PREVIEW_CENTER_HUE;
                return true;
            }

            Point delta = obj.RealScreenPosition - target.RealScreenPosition;

            if (MatchesPreviewOffset(world, delta))
            {
                hue = PREVIEW_HUE;
                return true;
            }

            return false;
        }

        public static void DrawOverlay(UltimaBatcher2D batcher, sbyte maxGroundZ)
        {
            if (SelectedObject.Object is not GameObject target || !IsEnabled(target.World))
                return;

            bool eastToWest = CalculateEastToWest(target.World, target);
            Land centerLand = GetLand(target.World.Map.GetChunk(target.X, target.Y, false), target.X % 8, target.Y % 8);
            int centerZ = GetEffectiveLandZ(centerLand, target.Z);
            PreviewPalette palette = GetPreviewPalette(target.World, target);

            foreach ((int x, int y, bool isCenter) in EnumeratePreviewTiles(target.World, target, eastToWest))
            {
                Chunk chunk = target.World.Map.GetChunk(x, y, false);

                if (chunk == null)
                    continue;

                Land land = GetLand(chunk, x % 8, y % 8);

                if (land == null || !land.AllowedToDraw || land.IsDestroyed || land.Z > maxGroundZ)
                    continue;

                if (!isCenter && !CanPreviewTile(target.World, centerZ, land))
                    continue;

                TerrainVisualizer.DrawTileOverlay(
                    batcher,
                    land,
                    isCenter ? palette.CenterFill : palette.Fill,
                    isCenter ? palette.CenterLine : palette.Line,
                    land.CalculateDepthZ() + 0.8f
                );
            }
        }

        private static bool IsSupportedSpell(int spellIndex)
        {
            return spellIndex is 24 or 28 or 39 or 47 or 50;
        }

        private static IEnumerable<(int X, int Y, bool IsCenter)> EnumeratePreviewTiles(World world, GameObject target, bool eastToWest)
        {
            yield return (target.X, target.Y, true);

            if (GameActions.LastSpellIndexCursor == 24)
            {
                foreach ((int offsetX, int offsetY) in eastToWest ? new[] { (1, 0), (-1, 0) } : new[] { (0, 1), (0, -1) })
                {
                    yield return (target.X + offsetX, target.Y + offsetY, false);
                }

                yield break;
            }

            foreach ((int offsetX, int offsetY) in eastToWest
                         ? new[] { (1, 0), (2, 0), (-1, 0), (-2, 0) }
                         : new[] { (0, 1), (0, 2), (0, -1), (0, -2) })
            {
                yield return (target.X + offsetX, target.Y + offsetY, false);
            }
        }

        private static bool CanPreviewTile(World world, int centerZ, Land land)
        {
            int candidateZ = GetEffectiveLandZ(land, land.Z);

            if (GameActions.LastSpellIndexCursor == 39)
            {
                return Math.Abs(candidateZ - centerZ) <= 12;
            }

            return Math.Abs(candidateZ - centerZ) <= 4;
        }

        private static PreviewPalette GetPreviewPalette(World world, GameObject target)
        {
            if (IsOutOfRange(world, target))
            {
                return OutOfRangePalette;
            }

            return GameActions.LastSpellIndexCursor switch
            {
                24 => new PreviewPalette(
                    new Color(190, 190, 190, 88),
                    new Color(235, 235, 235, 88),
                    new Color(120, 120, 120, 185),
                    new Color(170, 170, 170, 185)
                ),
                28 => new PreviewPalette(
                    new Color(255, 172, 82, 88),
                    new Color(255, 214, 128, 88),
                    new Color(202, 102, 24, 185),
                    new Color(224, 138, 42, 185)
                ),
                39 => new PreviewPalette(
                    new Color(132, 214, 102, 88),
                    new Color(190, 240, 154, 88),
                    new Color(66, 146, 44, 185),
                    new Color(104, 176, 70, 185)
                ),
                47 => new PreviewPalette(
                    new Color(118, 176, 255, 88),
                    new Color(166, 210, 255, 88),
                    new Color(60, 112, 210, 185),
                    new Color(92, 146, 228, 185)
                ),
                50 => new PreviewPalette(
                    new Color(192, 130, 255, 88),
                    new Color(224, 184, 255, 88),
                    new Color(118, 62, 202, 185),
                    new Color(158, 98, 224, 185)
                ),
                _ => DefaultPalette
            };
        }

        private static bool IsOutOfRange(World world, GameObject target)
        {
            int dx = Math.Abs(world.Player.X - target.X);
            int dy = Math.Abs(world.Player.Y - target.Y);
            return Math.Max(dx, dy) > FIELD_RANGE;
        }

        private static int GetEffectiveLandZ(Land land, int fallbackZ)
        {
            if (land == null)
                return fallbackZ;

            return land.IsStretched ? land.AverageZ : land.Z;
        }

        private readonly record struct PreviewPalette(Color Fill, Color CenterFill, Color Line, Color CenterLine);

        private static Land GetLand(Chunk chunk, int cellX, int cellY)
        {
            for (GameObject obj = chunk.GetHeadObject(cellX, cellY); obj != null; obj = obj.TNext)
            {
                if (obj is Land land)
                    return land;
            }

            return null;
        }

        private static bool MatchesPreviewOffset(World world, Point delta)
        {
            GameObject target = SelectedObject.Object as GameObject;

            if (target == null)
                return false;

            bool eastToWest = CalculateEastToWest(world, target);

            return GameActions.LastSpellIndexCursor switch
            {
                24 => eastToWest
                    ? delta is { X: 22, Y: 22 } or { X: -22, Y: -22 }
                    : delta is { X: -22, Y: 22 } or { X: 22, Y: -22 },

                28 or 39 or 47 or 50 => eastToWest
                    ? delta is { X: 22, Y: 22 } or { X: 44, Y: 44 } or { X: -22, Y: -22 } or { X: -44, Y: -44 }
                    : delta is { X: -22, Y: 22 } or { X: -44, Y: 44 } or { X: 22, Y: -22 } or { X: 44, Y: -44 },

                _ => false
            };
        }

        private static bool CalculateEastToWest(World world, GameObject obj)
        {
            int dx = world.Player.X - obj.X;
            int dy = world.Player.Y - obj.Y;
            int rx = (dx - dy) * 44;
            int ry = (dx + dy) * 44;

            if (rx >= 0 && ry >= 0)
                return false;

            if (rx >= 0)
                return true;

            if (ry >= 0)
                return true;

            return false;
        }
    }
}

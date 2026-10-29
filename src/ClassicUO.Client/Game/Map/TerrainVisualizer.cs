// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ClassicUO.Assets;
using System;

namespace ClassicUO.Game.Map
{
    internal static class TerrainVisualizer
    {
        private static readonly Vector3 _lineHue = new Vector3(0f, ShaderHueTranslator.SHADER_NONE, 1f);
        private static readonly Vector3 _overlayHue = new Vector3(0f, ShaderHueTranslator.SHADER_NONE, 1f);
        private static readonly Texture2D _normalGridTexture = SolidColorTextureCache.GetTexture(new Color(0, 0, 0, 92));
        private static readonly Texture2D _stretchedGridTexture = SolidColorTextureCache.GetTexture(new Color(95, 30, 20, 120));

        public static bool UseFlatColors => ProfileManager.CurrentProfile?.UseFlatTerrainColors ?? false;
        public static bool ShowGridOverlay => ProfileManager.CurrentProfile?.ShowTerrainGridOverlay ?? false;

        public static Texture2D GetLandTexture(Land land)
        {
            Color color = GetLandColor(land);
            return SolidColorTextureCache.GetTexture(color);
        }

        public static void DrawGrid(UltimaBatcher2D batcher, Chunk chunk, sbyte maxGroundZ, float depth)
        {
            EnsureObstructionCache(chunk);
            ulong visibleMask = 0;

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    if (chunk.GetHeadObject(x, y) is not Land land)
                        continue;

                    if (!land.AllowedToDraw || land.IsDestroyed || land.Z > maxGroundZ)
                        continue;

                    if (IsObstructed(chunk, x, y))
                        continue;

                    visibleMask |= 1UL << GetTileBitIndex(x, y);
                }
            }

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    if ((visibleMask & (1UL << GetTileBitIndex(x, y))) == 0)
                        continue;

                    if (chunk.GetHeadObject(x, y) is not Land land)
                        continue;

                    GetDiamondPoints(
                        land,
                        out Vector2 top,
                        out Vector2 right,
                        out Vector2 left,
                        out Vector2 bottom
                    );

                    Texture2D gridTexture = land.IsStretched ? _stretchedGridTexture : _normalGridTexture;
                    float stroke = land.IsStretched ? 1.2f : 1f;
                    float tileDepth = land.CalculateDepthZ() + 0.75f;

                    // Each interior edge is owned by exactly one tile to avoid duplicate draws.
                    batcher.DrawLine(gridTexture, top, right, _lineHue, stroke, tileDepth);
                    batcher.DrawLine(gridTexture, left, top, _lineHue, stroke, tileDepth);

                    if (x == 7)
                        batcher.DrawLine(gridTexture, right, bottom, _lineHue, stroke, tileDepth);

                    if (y == 7)
                        batcher.DrawLine(gridTexture, bottom, left, _lineHue, stroke, tileDepth);
                }
            }
        }

        public static void DrawTileOverlay(UltimaBatcher2D batcher, Land land, Color fillColor, Color lineColor, float depth)
        {
            Texture2D fillTexture = SolidColorTextureCache.GetTexture(fillColor);
            Texture2D lineTexture = SolidColorTextureCache.GetTexture(lineColor);

            if (land.IsStretched)
            {
                int posX = land.RealScreenPosition.X;
                int posY = land.RealScreenPosition.Y + (land.Z << 2);

                batcher.DrawStretchedLand(
                    fillTexture,
                    new Vector2(posX, posY),
                    fillTexture.Bounds,
                    ref land.YOffsets,
                    ref land.NormalTop,
                    ref land.NormalRight,
                    ref land.NormalLeft,
                    ref land.NormalBottom,
                    _overlayHue,
                    depth
                );
            }
            else
            {
                UltimaBatcher2D.YOffsets offsets = default;
                Vector3 flatNormal = new Vector3(0f, 0f, 1f);

                batcher.DrawStretchedLand(
                    fillTexture,
                    new Vector2(land.RealScreenPosition.X, land.RealScreenPosition.Y),
                    fillTexture.Bounds,
                    ref offsets,
                    ref flatNormal,
                    ref flatNormal,
                    ref flatNormal,
                    ref flatNormal,
                    _overlayHue,
                    depth
                );
            }

            GetDiamondPoints(land, out Vector2 top, out Vector2 right, out Vector2 left, out Vector2 bottom);

            batcher.DrawLine(lineTexture, top, right, _lineHue, 1.4f, depth + 0.0001f);
            batcher.DrawLine(lineTexture, right, bottom, _lineHue, 1.4f, depth + 0.0001f);
            batcher.DrawLine(lineTexture, bottom, left, _lineHue, 1.4f, depth + 0.0001f);
            batcher.DrawLine(lineTexture, left, top, _lineHue, 1.4f, depth + 0.0001f);
        }

        private static Color GetLandColor(Land land)
        {
            Color color = GetCategoryColor(land.TileData, land.Graphic);

            if (land.IsStretched)
            {
                color = Color.Lerp(color, new Color(208, 224, 235), 0.22f);
            }

            return color;
        }

        private static Color GetCategoryColor(LandTiles tileData, ushort graphic)
        {
            string name = tileData.Name?.ToLowerInvariant() ?? string.Empty;
            ushort radar16 = Client.Game.UO.FileManager.Hues.GetRadarColorData(graphic);
            uint radarPacked = HuesHelper.Color16To32(radar16) | 0xFF_00_00_00;
            var (b, g, r, a) = HuesHelper.GetBGRA(radarPacked);
            Color radarColor = new Color(r, g, b, a);

            if (TerrainPalette.TryGetOverride(graphic, name, out Color overrideColor))
            {
                return overrideColor;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return QuantizeRadarColor(radarColor, tileData);
            }

            if (tileData.IsWet)
            {
                if (ContainsAny(name, "swamp", "bog", "marsh", "muck"))
                    return new Color(78, 108, 64);

                return new Color(76, 118, 160);
            }

            if (ContainsAny(name, "lava", "volcan", "magma"))
                return new Color(160, 75, 40);

            if (ContainsAny(name, "sand", "desert", "dune"))
                return new Color(150, 132, 88);

            if (ContainsAny(name, "snow", "ice", "frost", "tundra"))
                return new Color(200, 206, 212);

            if (ContainsAny(name, "swamp", "bog", "marsh", "muck"))
                return new Color(86, 113, 70);

            if (ContainsAny(name, "grass", "field", "meadow", "moss", "fern", "leaf", "jungle"))
                return new Color(74, 114, 40);

            if (ContainsAny(name, "rock", "mountain", "stone", "cave", "cliff", "gravel"))
                return new Color(128, 120, 104);

            if (ContainsAny(name, "dirt", "mud", "earth"))
                return new Color(115, 93, 63);

            if (ContainsAny(name, "path", "road", "cobble", "flagstone", "brick", "pave"))
                return new Color(125, 116, 96);

            return QuantizeRadarColor(radarColor, tileData);
        }

        private static Color QuantizeRadarColor(Color radarColor, LandTiles tileData)
        {
            if (tileData.IsWet)
            {
                return radarColor.B > radarColor.G
                    ? new Color(76, 118, 160)
                    : new Color(78, 108, 64);
            }

            int max = Math.Max(radarColor.R, Math.Max(radarColor.G, radarColor.B));
            int min = Math.Min(radarColor.R, Math.Min(radarColor.G, radarColor.B));
            int delta = max - min;
            int brightness = (radarColor.R + radarColor.G + radarColor.B) / 3;

            if (brightness > 190)
                return new Color(200, 206, 212);

            if (radarColor.R > 150 && radarColor.G > 120 && radarColor.B < 120)
                return new Color(150, 132, 88);

            if (radarColor.G > radarColor.R + 18 && radarColor.G > radarColor.B + 18)
                return brightness < 95 ? new Color(86, 113, 70) : new Color(74, 114, 40);

            if (delta < 18)
                return brightness < 96 ? new Color(98, 104, 92) : new Color(128, 120, 104);

            if (radarColor.R > radarColor.G + 20 && radarColor.R > radarColor.B + 20)
                return new Color(115, 93, 63);

            if (radarColor.B > radarColor.R + 20 && radarColor.B > radarColor.G + 10)
                return new Color(112, 124, 142);

            return new Color(125, 116, 96);
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (value.Contains(needle, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void EnsureObstructionCache(Chunk chunk)
        {
            if (!chunk.GridObstructionCacheDirty)
                return;

            ulong obstructionMask = 0;

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    if (chunk.GetHeadObject(x, y) is not Land land)
                        continue;

                    if (HasObstructionAbove(land))
                        obstructionMask |= 1UL << GetTileBitIndex(x, y);
                }
            }

            chunk.GridObstructionMask = obstructionMask;
            chunk.GridObstructionCacheDirty = false;
        }

        private static bool IsObstructed(Chunk chunk, int tileX, int tileY)
        {
            return (chunk.GridObstructionMask & (1UL << GetTileBitIndex(tileX, tileY))) != 0;
        }

        private static int GetTileBitIndex(int tileX, int tileY)
        {
            return (tileY << 3) + tileX;
        }

        private static bool HasObstructionAbove(Land land)
        {
            int landTop = land.IsStretched ? land.AverageZ : land.Z;

            for (GameObject obj = land.TNext; obj != null; obj = obj.TNext)
            {
                switch (obj)
                {
                    case Static staticObj:
                    {
                        ref StaticTiles itemData = ref staticObj.ItemData;

                        if (itemData.IsBackground || itemData.IsSurface)
                            continue;

                        if (staticObj.Z + itemData.Height <= landTop)
                            continue;

                        return true;
                    }

                    case Multi multi:
                    {
                        ref StaticTiles itemData = ref multi.ItemData;

                        if (itemData.IsBackground || itemData.IsSurface)
                            continue;

                        if (multi.Z + itemData.Height <= landTop)
                            continue;

                        return true;
                    }
                }
            }

            return false;
        }

        internal static void GetDiamondPoints(
            Land land,
            out Vector2 top,
            out Vector2 right,
            out Vector2 left,
            out Vector2 bottom
        )
        {
            int posX = land.RealScreenPosition.X;
            int posY = land.RealScreenPosition.Y;

            if (land.IsStretched)
            {
                posY += land.Z << 2;

                top = new Vector2(posX + 22, posY - land.YOffsets.Top);
                right = new Vector2(posX + 44, posY + (22 - land.YOffsets.Right));
                left = new Vector2(posX, posY + (22 - land.YOffsets.Left));
                bottom = new Vector2(posX + 22, posY + (44 - land.YOffsets.Bottom));
                return;
            }

            top = new Vector2(posX + 22, posY);
            right = new Vector2(posX + 44, posY + 22);
            left = new Vector2(posX, posY + 22);
            bottom = new Vector2(posX + 22, posY + 44);
        }
    }
}

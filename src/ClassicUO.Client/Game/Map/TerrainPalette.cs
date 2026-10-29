// SPDX-License-Identifier: BSD-2-Clause

using System;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Map
{
    internal static class TerrainPalette
    {
        private static class TerrainColors
        {
            //public static readonly Color Grass = new(76, 122, 52);
            //public static readonly Color Grass = new(153, 153, 153);
            public static readonly Color Grass = new(61, 97, 41);
            public static readonly Color Swamp = new(84, 108, 70);
            //public static readonly Color Swamp = new(153, 153, 153);

            public static readonly Color Sand = new(138, 122, 86);
            //public static readonly Color Sand = new(153, 153, 153);
            public static readonly Color BrightSand = new(152, 136, 92);

            public static readonly Color Dirt = new(138, 122, 86);
            //#public static readonly Color Dirt = new(153, 153, 153);
            public static readonly Color Water = new(78, 118, 160);
            public static readonly Color Rock = new(128, 120, 104);
            public static readonly Color Snow = new(213, 213, 213);
            public static readonly Color BrightSnow = new(200, 206, 212);
            public static readonly Color Lava = new(160, 75, 40);
            public static readonly Color Road = new(122, 114, 94);
            //public static readonly Color Road = new(153, 153, 153);
        }

        private readonly struct TileRangeOverride
        {
            public TileRangeOverride(ushort min, ushort max, Color color)
            {
                Min = min;
                Max = max;
                Color = color;
            }

            public ushort Min { get; }
            public ushort Max { get; }
            public Color Color { get; }
        }

        private readonly struct KeywordOverride
        {
            public KeywordOverride(string keyword, Color color)
            {
                Keyword = keyword;
                Color = color;
            }

            public string Keyword { get; }
            public Color Color { get; }
        }

        // Edit these values to tune the flat-terrain palette.
        // Range overrides are checked first, then name keywords, then the fallback radar/map color bucket.
        private static readonly TileRangeOverride[] _rangeOverrides =
        [
            new TileRangeOverride(0x0003, 0x0015, TerrainColors.Grass),   // grass/open ground
            new TileRangeOverride(0x0016, 0x006F, TerrainColors.Grass),   // sand
            new TileRangeOverride(0x0071, 0x00A7, TerrainColors.Grass),   // dirt
            new TileRangeOverride(0x0050, 0x007F, TerrainColors.Grass),  // dirt/sand mix
            new TileRangeOverride(0x00A8, 0x00AB, TerrainColors.Water),  // water
            // new TileRangeOverride(0x00AC, 0x00AF, TerrainColors.Swamp),   // swamp
            new TileRangeOverride(0x00AC, 0x00DB, TerrainColors.Grass),   // swamp
            new TileRangeOverride(0X00DC, 0x0107, TerrainColors.Rock), // rock/mountain
            new TileRangeOverride(0x0108, 0x010B, TerrainColors.Grass),   // grass/open ground
            new TileRangeOverride(0x010C, 0x011D, TerrainColors.Snow),   // snow
            new TileRangeOverride(0x011E, 0x012D, TerrainColors.Grass),   // sand
            new TileRangeOverride(0x012E, 0x0131, TerrainColors.Snow),   // snow
            new TileRangeOverride(0x0136, 0x0137, TerrainColors.Water),  // water
            new TileRangeOverride(0x0141, 0x0144, TerrainColors.Rock), // rock/mountain
            new TileRangeOverride(0x014C, 0x0174, TerrainColors.Grass),   // grass/open ground
            new TileRangeOverride(0x0179, 0x018A, TerrainColors.Snow),   // snow
            new TileRangeOverride(0x0192, 0x01AB, TerrainColors.Grass),   // grass/open ground
            //new TileRangeOverride(0x0110, 0x013F, TerrainColors.Rock), // rock/mountain
            //new TileRangeOverride(0x0140, 0x017F, TerrainColors.Road)   // road/path style
        ];

        private static readonly KeywordOverride[] _keywordOverrides =
        [
            // new KeywordOverride("grass", TerrainColors.Grass),
            // new KeywordOverride("field", TerrainColors.Grass),
            // new KeywordOverride("swamp", TerrainColors.Swamp),
            // new KeywordOverride("bog", TerrainColors.Swamp),
            // new KeywordOverride("sand", TerrainColors.BrightSand),
            // new KeywordOverride("desert", TerrainColors.BrightSand),
            // new KeywordOverride("rock", TerrainColors.Rock),
            // new KeywordOverride("mountain", TerrainColors.Rock),
            // new KeywordOverride("snow", TerrainColors.BrightSnow),
            // new KeywordOverride("lava", TerrainColors.Lava),
            // new KeywordOverride("road", TerrainColors.Road),
            // new KeywordOverride("path", TerrainColors.Road)
            new KeywordOverride("grass", TerrainColors.Grass),
            new KeywordOverride("field", TerrainColors.Grass),
            new KeywordOverride("swamp", TerrainColors.Grass),
            new KeywordOverride("bog", TerrainColors.Grass),
            new KeywordOverride("sand", TerrainColors.BrightSand),
            new KeywordOverride("desert", TerrainColors.BrightSand),
            new KeywordOverride("rock", TerrainColors.Rock),
            new KeywordOverride("mountain", TerrainColors.Rock),
            new KeywordOverride("snow", TerrainColors.BrightSnow),
            new KeywordOverride("lava", TerrainColors.Lava),
            new KeywordOverride("road", TerrainColors.Grass),
            new KeywordOverride("path", TerrainColors.Grass)
        ];

        public static bool TryGetOverride(ushort graphic, string name, out Color color)
        {
            foreach (ref readonly var entry in _rangeOverrides.AsSpan())
            {
                if (graphic >= entry.Min && graphic <= entry.Max)
                {
                    color = entry.Color;
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (ref readonly var entry in _keywordOverrides.AsSpan())
                {
                    if (name.Contains(entry.Keyword, StringComparison.Ordinal))
                    {
                        color = entry.Color;
                        return true;
                    }
                }
            }

            color = default;
            return false;
        }
    }
}

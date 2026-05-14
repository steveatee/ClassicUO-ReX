// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Data
{
    internal static class StaticArtReplacements
    {
        public const ushort TreeReplaceGraphic = 0x0720;
        public const ushort TreeReplaceGraphicTile = 0x0720;
        public const ushort BlockerReplaceGraphicTile = 0x0750;
        public const ushort BlockerReplaceGraphicStump = 0x0750;
        public const ushort BlockerReplaceGraphicRock = 0x0750;

        public static ushort Replace(ushort graphic)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return graphic;
            }

            if (StaticFilters.IsTree(graphic, out _))
            {
                if (profile.TreeType == 1 || profile.TreeType == 0 && profile.TreeToStumps)
                {
                    return TreeReplaceGraphic;
                }

                if (profile.TreeType == 2)
                {
                    return TreeReplaceGraphicTile;
                }
            }

            if (IsBlockerTreeArt(graphic))
            {
                if (profile.BlockerType == 1)
                {
                    return BlockerReplaceGraphicStump;
                }

                if (profile.BlockerType == 2)
                {
                    return BlockerReplaceGraphicTile;
                }
            }

            if (IsBlockerStoneArt(graphic))
            {
                if (profile.BlockerType == 1)
                {
                    return BlockerReplaceGraphicRock;
                }

                if (profile.BlockerType == 2)
                {
                    return BlockerReplaceGraphicTile;
                }
            }

            return graphic;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBlockerTreeArt(ushort graphic)
        {
            switch (graphic)
            {
                case 0x1772:
                case 0x177A:
                case 0x0C2D:
                case 0x0C99:
                case 0x0C9B:
                case 0x0C9C:
                case 0x0C9D:
                case 0x0CA6:
                case 0x0CC4:
                    return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBlockerStoneArt(ushort graphic)
        {
            switch (graphic)
            {
                case 0x1363:
                case 0x1364:
                case 0x1365:
                case 0x1366:
                case 0x1367:
                case 0x1368:
                case 0x1369:
                case 0x136A:
                case 0x136B:
                case 0x136C:
                case 0x136D:
                case 0x0ED7:
                case 0x0ED8:
                case 0x0ED9:
                case 0x0EDA:
                case 0x1165:
                case 0x1166:
                case 0x1167:
                case 0x1168:
                case 0x1169:
                case 0x116A:
                case 0x116B:
                case 0x116C:
                case 0x116D:
                case 0x116E:
                case 0x116F:
                case 0x1170:
                case 0x1171:
                case 0x1172:
                case 0x1173:
                case 0x1174:
                case 0x1175:
                case 0x1176:
                case 0x1177:
                case 0x1178:
                case 0x1179:
                case 0x117A:
                case 0x117B:
                case 0x117C:
                case 0x117D:
                case 0x117E:
                case 0x117F:
                case 0x1180:
                case 0x1181:
                case 0x1182:
                    return true;
            }

            return false;
        }
    }
}

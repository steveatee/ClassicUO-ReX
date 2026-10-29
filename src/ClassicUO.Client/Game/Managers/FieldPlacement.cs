// SPDX-License-Identifier: BSD-2-Clause
// Predicts which tiles of a field spell the server will actually create, so the field preview can show
// placed tiles (and skipped ones) before you cast.
//
// "UO Dreams" rules were measured in game (29 Oct 2026, see the project doc "Field placement test spots"):
//   * every field skips a tile whose ground rises above the target height anywhere on the tile;
//   * Poison / Fire: search from the target height down to 9 below; sloped ground is fine (field sits at the
//     tile's average height);
//   * Energy / Paralyze / Wall of Stone: search down to 5 below, and skip sloped ground.
// "RunUO" and "ServUO / TrueUO" follow those servers' SpellHelper.AdjustField / Map.CanFit code.

using System;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers
{
    internal static class FieldPlacement
    {
        public const int RULES_UO_DREAMS = 0;
        public const int RULES_RUNUO = 1;
        public const int RULES_SERVUO = 2;

        public static readonly string[] RuleNames = { "UO Dreams (measured)", "RunUO", "ServUO / TrueUO" };

        private const int SPELL_WALL_OF_STONE = 24;
        private const int SPELL_FIRE_FIELD = 28;
        private const int SPELL_ENERGY_FIELD = 39;
        private const int SPELL_POISON_FIELD = 47;
        private const int SPELL_PARALYZE_FIELD = 50;

        private const int UNKNOWN = int.MinValue;

        private struct TileResult
        {
            public int X, Y;
            public bool Placed;
        }

        private static readonly TileResult[] _tiles = new TileResult[5];
        private static int _tileCount;

        // cache key
        private static object _keyTarget;
        private static int _keyTx, _keyTy, _keyTz, _keySpell, _keyPx, _keyPy, _keyRules;
        private static uint _keyTicks;

        public static bool IsFieldSpell(int spellIndex) =>
            spellIndex == SPELL_WALL_OF_STONE || spellIndex == SPELL_FIRE_FIELD || spellIndex == SPELL_ENERGY_FIELD
            || spellIndex == SPELL_POISON_FIELD || spellIndex == SPELL_PARALYZE_FIELD;

        /// <summary>
        /// When a field spell is on the cursor, reports whether (x, y) is one of the field's tiles and whether the
        /// server will place a field there.
        /// </summary>
        public static bool TryGetTileState(World world, int x, int y, out bool placed)
        {
            placed = false;

            if (!Update(world))
            {
                return false;
            }

            for (int i = 0; i < _tileCount; i++)
            {
                if (_tiles[i].X == x && _tiles[i].Y == y)
                {
                    placed = _tiles[i].Placed;
                    return true;
                }
            }

            return false;
        }

        private static bool Update(World world)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (
                profile == null
                || !profile.PreviewFields
                || world?.Player == null
                || world.Map == null
                || !world.TargetManager.IsTargeting
                || !IsFieldSpell(GameActions.LastSpellIndex)
                || SelectedObject.Object is not GameObject target
                || target is GameEffect
            )
            {
                _tileCount = 0;
                _keyTarget = null;
                return false;
            }

            int spell = GameActions.LastSpellIndex;
            int rules = profile.FieldPlacementRules;
            int pz = GetTargetSurfaceZ(target);

            GetCasterPosition(world, out int px, out int py);

            if (
                ReferenceEquals(_keyTarget, target) && _keyTx == target.X && _keyTy == target.Y && _keyTz == pz
                && _keySpell == spell && _keyPx == px && _keyPy == py && _keyRules == rules
                && Time.Ticks - _keyTicks < 250
            )
            {
                return _tileCount > 0;
            }

            _keyTarget = target;
            _keyTx = target.X;
            _keyTy = target.Y;
            _keyTz = pz;
            _keySpell = spell;
            _keyPx = px;
            _keyPy = py;
            _keyRules = rules;
            _keyTicks = Time.Ticks;

            Compute(world, target.X, target.Y, pz, px, py, spell, rules);

            return _tileCount > 0;
        }

        private static void Compute(World world, int tx, int ty, int pz, int px, int py, int spell, int rules)
        {
            // Orientation: identical to the server spells (RunUO / ServUO / TrueUO all share it).
            int dx = px - tx;
            int dy = py - ty;
            int rx = (dx - dy) * 44;
            int ry = (dx + dy) * 44;
            bool eastToWest = !(rx >= 0 && ry >= 0) && (rx >= 0 || ry >= 0);

            bool isWall = spell == SPELL_WALL_OF_STONE;
            bool tolerant = spell == SPELL_FIRE_FIELD || spell == SPELL_POISON_FIELD;
            int half = isWall ? 1 : 2;

            _tileCount = 0;

            for (int i = -half; i <= half; i++)
            {
                int x = eastToWest ? tx + i : tx;
                int y = eastToWest ? ty : ty + i;

                bool placed;

                switch (rules)
                {
                    case RULES_RUNUO:
                        placed = PlacedRunUO(world, x, y, pz, isWall, tolerant);
                        break;

                    case RULES_SERVUO:
                        placed = true; // ServUO / TrueUO always create the tile (it may end up buried or floating)
                        break;

                    default:
                        placed = i == 0 || PlacedUODreams(world, x, y, pz, tolerant);
                        break;
                }

                _tiles[_tileCount++] = new TileResult { X = x, Y = y, Placed = placed };
            }
        }

        private static bool PlacedUODreams(World world, int x, int y, int pz, bool tolerant)
        {
            if (!TryGetLandCorners(world, x, y, out int low, out int avg, out int top))
            {
                return true; // map not loaded: don't claim a skip we can't check
            }

            if (top > pz)
            {
                return false; // ground rises above the target somewhere on this tile
            }

            int maxDown = tolerant ? 9 : 5;

            for (int o = 0; o <= maxDown; o++)
            {
                int z = pz - o;
                int fit = CanFit(world, x, y, z, 12, out bool onLand);

                if (fit == UNKNOWN)
                {
                    return true;
                }

                if (fit == 1)
                {
                    return tolerant || !onLand || low == top;
                }
            }

            return false;
        }

        private static bool PlacedRunUO(World world, int x, int y, int pz, bool isWall, bool tolerant)
        {
            if (tolerant)
            {
                return true; // RunUO always creates Fire / Poison tiles (they just don't damage if they didn't fit)
            }

            int height = isWall ? 22 : 12;

            for (int o = 0; o < 10; o++)
            {
                int fit = CanFit(world, x, y, pz - o, height, out _);

                if (fit == UNKNOWN)
                {
                    return true;
                }

                if (fit == 1)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Client copy of the server's Map.CanFit(requireSurface: true) using land and statics/multis
        /// (plus non-movable items). Returns 1 = fits, 0 = doesn't, UNKNOWN = map data not available.
        /// </summary>
        private static int CanFit(World world, int x, int y, int z, int height, out bool onLand)
        {
            onLand = false;

            if (!TryGetLandCorners(world, x, y, out int low, out int avg, out _))
            {
                return UNKNOWN;
            }

            GameObject head = world.Map.GetTile(x, y, false);

            if (head == null)
            {
                return UNKNOWN;
            }

            Land land = null;

            for (GameObject o = head; o != null; o = o.TNext)
            {
                if (o is Land l)
                {
                    land = l;
                    break;
                }
            }

            if (land == null)
            {
                return UNKNOWN;
            }

            bool landImpassable = land.TileData.IsImpassable;

            if (landImpassable && avg > z && z + height > low)
            {
                return 0;
            }

            bool hasSurface = !landImpassable && z == avg && !IsIgnoredLand(land.Graphic);
            onLand = hasSurface;

            for (GameObject o = head; o != null; o = o.TNext)
            {
                ushort graphic;
                int oz = o.Z;

                switch (o)
                {
                    case Static s:
                        graphic = s.OriginalGraphic;
                        break;

                    case Multi m:
                        graphic = m.Graphic;
                        break;

                    case Item it when !it.IsMulti && !it.IsCorpse && (it.Flags & Flags.Movable) == 0 && !StaticFilters.IsField(it.Graphic) && !StaticFilters.IsWallOfStone(it.Graphic):
                        graphic = it.Graphic;
                        break;

                    default:
                        continue;
                }

                if (graphic >= Client.Game.UO.FileManager.TileData.StaticData.Length)
                {
                    continue;
                }

                ref StaticTiles data = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];

                bool surface = data.IsSurface;
                bool impassable = data.IsImpassable;
                int calcHeight = data.IsBridge ? data.Height / 2 : data.Height;

                if ((surface || impassable) && oz + calcHeight > z && z + height > oz)
                {
                    return 0;
                }

                if (surface && !impassable && z == oz + calcHeight)
                {
                    hasSurface = true;
                    onLand = false;
                }
            }

            return hasSurface ? 1 : 0;
        }

        /// <summary>Server Map.GetAverageZ, from the raw land heights of the tile's four corners.</summary>
        private static bool TryGetLandCorners(World world, int x, int y, out int low, out int avg, out int top)
        {
            int zTop = world.Map.GetTileZ(x, y);
            int zLeft = world.Map.GetTileZ(x, y + 1);
            int zRight = world.Map.GetTileZ(x + 1, y);
            int zBottom = world.Map.GetTileZ(x + 1, y + 1);

            low = Math.Min(Math.Min(zTop, zLeft), Math.Min(zRight, zBottom));
            top = Math.Max(Math.Max(zTop, zLeft), Math.Max(zRight, zBottom));

            if (low == -125)
            {
                avg = 0;
                return false;
            }

            avg = Math.Abs(zTop - zBottom) > Math.Abs(zLeft - zRight) ? FloorAverage(zLeft, zRight) : FloorAverage(zTop, zBottom);

            return true;
        }

        private static int FloorAverage(int a, int b)
        {
            int v = a + b;

            if (v < 0)
            {
                --v;
            }

            return v / 2;
        }

        private static bool IsIgnoredLand(ushort graphic) => graphic == 2 || graphic == 0x1DB || graphic >= 0x1AE && graphic <= 0x1B5;

        /// <summary>The Z the server uses as the field's target height (SpellHelper.GetSurfaceTop).</summary>
        private static int GetTargetSurfaceZ(GameObject target)
        {
            switch (target)
            {
                case Land land:
                    return land.Z; // the client sends the land tile's own height

                case Static s:
                {
                    ref StaticTiles data = ref Client.Game.UO.FileManager.TileData.StaticData[s.OriginalGraphic];
                    return data.IsSurface ? s.Z + (data.IsBridge ? data.Height / 2 : data.Height) : s.Z;
                }

                case Multi m:
                {
                    ref StaticTiles data = ref Client.Game.UO.FileManager.TileData.StaticData[m.Graphic];
                    return data.IsSurface ? m.Z + (data.IsBridge ? data.Height / 2 : data.Height) : m.Z;
                }

                case Item it:
                {
                    ref StaticTiles data = ref it.ItemData;
                    return data.IsSurface ? it.Z + (data.IsBridge ? data.Height / 2 : data.Height) : it.Z;
                }

                default:
                    return target.Z;
            }
        }

        private static void GetCasterPosition(World world, out int x, out int y)
        {
            x = world.Player.X;
            y = world.Player.Y;

            if (world.Player.Steps.Count != 0)
            {
                ref Mobile.Step st = ref world.Player.Steps.Back();
                x = st.X;
                y = st.Y;
            }
        }
    }
}

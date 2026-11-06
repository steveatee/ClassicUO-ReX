// SPDX-License-Identifier: BSD-2-Clause
// Predicts which tiles of a field spell the server will actually create, so the field preview can show
// placed tiles (and skipped ones) before you cast.
//
// "UO Dreams" rules, fitted to every logged cast (29 Oct field_dump.csv + 6 Nov field_casts.csv, 0 misses
// outside houses, checked against the real map files):
//   * each tile's height is found like TrueUO's SpellHelper.AdjustField: try the target height, then 1 down,
//     1 up, 2 down, 2 up ... (Map.CanFit with a surface), so a field can sit ABOVE the target too;
//   * then the caster needs line of sight (RunUO Map.LineOfSight from eye = feet + 14) to the tile, which is
//     what skips tiles behind bumps, on the far side of a slope or behind house walls;
//       - Energy / Paralyze / Wall of Stone check LOS to where the field ends up; Poison / Fire to the tile
//         at the target height;
//   * at most 2 above the target; a side tile only goes up onto land whose average height is its low corner
//     (flat ground, or a slope rising from there) - the centre may go up onto any slope;
//   * Energy / Paralyze / Wall of Stone: at most 5 below the target; Poison / Fire: at most 9 below;
//   * the centre follows the same rules (6 Nov: a centre on a rise was skipped while the sides placed).
//   Offline check: Sourcecode/field_rules_check (check_casts.py, old.py) against the real map files.
// "RunUO" and "ServUO / TrueUO" follow those servers' SpellHelper.AdjustField / Map.CanFit code.

using System;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
        // Magery spell numbers (SpellsMagery.cs): Poison Field 39, Paralyze Field 47, Energy Field 50.
        private const int SPELL_POISON_FIELD = 39;
        private const int SPELL_PARALYZE_FIELD = 47;
        private const int SPELL_ENERGY_FIELD = 50;

        // Energy / Paralyze / Wall of Stone on sloped ground: placed when the field sits at most this far
        // below the target (2090,134: slope 0..4 under a z4 target, field at 2 → placed; every logged
        // skip had the field 3 or more below).
        private const int STRICT_SLOPE_MAX_DROP = 2; // old rule, no longer used: line of sight explains those skips

        // A field tile can sit at most this far above the target height (6 Nov: +2 placed, +3 not).
        private const int MAX_RISE = 2;

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

        public const int SKIPPED_GREY = 0;
        public const int SKIPPED_HIDDEN = 1;

        /// <summary>
        /// UO hue of each field when drawn as tiles (matches ItemView's "fields as tiles" colours).
        /// Used for the preview overlay and the mobile tint.
        /// </summary>
        public static ushort GetFieldHue(int spellIndex)
        {
            switch (spellIndex)
            {
                case SPELL_FIRE_FIELD: return 0x0020;
                case SPELL_PARALYZE_FIELD: return 0x0058;
                case SPELL_ENERGY_FIELD: return 0x0070;
                case SPELL_POISON_FIELD: return 0x0044;
                case SPELL_WALL_OF_STONE: return 0x038A;
                default: return 0x0040;
            }
        }

        public static int CurrentSpell => _keySpell;

        private static readonly Vector3 _overlayHue = new Vector3(0f, ShaderHueTranslator.SHADER_NONE, 1f);
        private static Vector3 _flatNormal = new Vector3(0f, 0f, 1f);
        private static int _colorSpell = -1;
        private static Color _fill, _line;
        private static readonly Color _skipFill = new Color(110, 110, 110) * 0.18f;
        private static readonly Color _skipLine = new Color(150, 150, 150) * 0.65f;

        /// <summary>
        /// Draws the preview as translucent diamonds on the ground, in the field's own colour (faded).
        /// Called right after the land is drawn so statics and mobiles still render on top.
        /// </summary>
        public static void DrawOverlay(World world, UltimaBatcher2D batcher)
        {
            if (!Update(world))
            {
                return;
            }

            if (_colorSpell != _keySpell)
            {
                _colorSpell = _keySpell;
                Color baseColor = HueToColor(GetFieldHue(_keySpell));
                Color faded = Color.Lerp(baseColor, Color.White, 0.25f);
                _fill = faded * 0.45f;
                _line = faded * 0.9f;
            }

            bool showSkipped = ProfileManager.CurrentProfile.FieldPreviewSkippedStyle != SKIPPED_HIDDEN;

            for (int i = 0; i < _tileCount; i++)
            {
                ref TileResult t = ref _tiles[i];

                if (!t.Placed && !showSkipped)
                {
                    continue;
                }

                Land land = GetLand(world, t.X, t.Y);

                if (land == null || land.IsDestroyed)
                {
                    continue;
                }

                DrawDiamond(batcher, land, t.Placed ? _fill : _skipFill, t.Placed ? _line : _skipLine, land.CalculateDepthZ() + 0.8f);
            }
        }

        private static Color HueToColor(ushort hue)
        {
            // cell 24 of the hue's 32-step ramp: a bright but not washed-out shade
            uint c = Client.Game.UO.FileManager.Hues.GetPolygoneColor(24, hue);
            return new Color((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
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

        private static void DrawDiamond(UltimaBatcher2D batcher, Land land, Color fill, Color line, float depth)
        {
            Texture2D fillTexture = SolidColorTextureCache.GetTexture(fill);
            Texture2D lineTexture = SolidColorTextureCache.GetTexture(line);

            int posX = land.RealScreenPosition.X;
            int posY = land.RealScreenPosition.Y;
            Vector2 top, right, left, bottom;

            if (land.IsStretched)
            {
                posY += land.Z << 2;

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

                top = new Vector2(posX + 22, posY - land.YOffsets.Top);
                right = new Vector2(posX + 44, posY + (22 - land.YOffsets.Right));
                left = new Vector2(posX, posY + (22 - land.YOffsets.Left));
                bottom = new Vector2(posX + 22, posY + (44 - land.YOffsets.Bottom));
            }
            else
            {
                UltimaBatcher2D.YOffsets offsets = default;

                batcher.DrawStretchedLand(
                    fillTexture,
                    new Vector2(posX, posY),
                    fillTexture.Bounds,
                    ref offsets,
                    ref _flatNormal,
                    ref _flatNormal,
                    ref _flatNormal,
                    ref _flatNormal,
                    _overlayHue,
                    depth
                );

                top = new Vector2(posX + 22, posY);
                right = new Vector2(posX + 44, posY + 22);
                left = new Vector2(posX, posY + 22);
                bottom = new Vector2(posX + 22, posY + 44);
            }

            float lineDepth = depth + 0.0001f;
            batcher.DrawLine(lineTexture, top, right, _overlayHue, 1.4f, lineDepth);
            batcher.DrawLine(lineTexture, right, bottom, _overlayHue, 1.4f, lineDepth);
            batcher.DrawLine(lineTexture, bottom, left, _overlayHue, 1.4f, lineDepth);
            batcher.DrawLine(lineTexture, left, top, _overlayHue, 1.4f, lineDepth);
        }

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

            // Out of range (spellranges.txt, e.g. 9 on UO Dreams): the cast will fail, so nothing is placed.
            if (SpellRanges.TryGetRule(spell, out SpellRangeRule rangeRule) && !rangeRule.Contains(target.X - px, target.Y - py))
            {
                for (int i = 0; i < _tileCount; i++)
                {
                    _tiles[i].Placed = false;
                }
            }

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
                        placed = PlacedUODreams(world, x, y, pz, tolerant, px, py, GetCasterZ(world), isWall, spell == SPELL_PARALYZE_FIELD, i == 0);
                        break;
                }

                _tiles[_tileCount++] = new TileResult { X = x, Y = y, Placed = placed };
            }
        }

        private static bool PlacedUODreams(World world, int x, int y, int pz, bool tolerant, int px, int py, int casterZ, bool isWall, bool isParalyze, bool centre)
        {
            if (!TryGetLandCorners(world, x, y, out int low, out int avg, out int top))
            {
                return true; // map not loaded: don't claim a skip we can't check
            }

            int height = isWall ? 22 : isParalyze ? 16 : 12;
            int z = UNKNOWN;
            bool onLand = false;

            // TrueUO SpellHelper.AdjustField: 0, -1, +1, -2, +2 ... up to 24.
            for (int o = 0; o < 25 && z == UNKNOWN; o++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int tryZ = pz + sign * o;
                    int fit = CanFit(world, x, y, tryZ, height, out bool tryOnLand);

                    if (fit == UNKNOWN)
                    {
                        return true;
                    }

                    if (fit == 1)
                    {
                        z = tryZ;
                        onLand = tryOnLand;
                        break;
                    }

                    if (o == 0)
                    {
                        break; // +0 and -0 are the same
                    }
                }
            }

            if (z == UNKNOWN)
            {
                return false;
            }

            if (z > pz)
            {
                // At most 2 above the target; a side tile only onto land whose average is its low corner
                // (flat, or a slope that only rises from there). The centre may sit on any slope.
                if (z - pz > MAX_RISE || !centre && onLand && avg != low)
                {
                    return false;
                }
            }
            else if (z < pz && pz - z > (tolerant ? 9 : 5))
            {
                return false;
            }

            return HasLineOfSight(world, px, py, casterZ + 14, x, y, tolerant ? pz : z);
        }

        private static int GetCasterZ(World world)
        {
            if (world.Player.Steps.Count != 0)
            {
                return world.Player.Steps.Back().Z;
            }

            return world.Player.Z;
        }

        // ------- line of sight: a copy of RunUO / TrueUO Map.LineOfSight (land + statics / multis / items) -------

        private static readonly System.Collections.Generic.List<(int X, int Y, int Z)> _losPath = new System.Collections.Generic.List<(int, int, int)>(64);

        private static bool NumberBetween(double num, int bound1, int bound2, double allowance)
        {
            if (bound1 > bound2)
            {
                (bound1, bound2) = (bound2, bound1);
            }

            return num < bound2 + allowance && num > bound1 - allowance;
        }

        internal static bool HasLineOfSight(World world, int ox, int oy, int oz, int dx, int dy, int dz)
        {
            int endX = dx, endY = dy, endZ = dz;

            if (ox > dx || ox == dx && oy > dy || ox == dx && oy == dy && oz > dz)
            {
                (ox, dx) = (dx, ox);
                (oy, dy) = (dy, oy);
                (oz, dz) = (dz, oz);
            }

            if (ox == dx && oy == dy && oz == dz)
            {
                return true;
            }

            int xd = dx - ox, yd = dy - oy, zd = dz - oz;
            double zslp = Math.Sqrt(xd * xd + yd * yd);
            double sq3d = zd != 0 ? Math.Sqrt(zslp * zslp + zd * zd) : zslp;
            double rise = yd / sq3d, run = xd / sq3d;
            zslp = zd / sq3d;

            double fx = ox, fy = oy, fz = oz;
            _losPath.Clear();

            while (NumberBetween(fx, dx, ox, 0.5) && NumberBetween(fy, dy, oy, 0.5) && NumberBetween(fz, dz, oz, 0.5))
            {
                var p = ((int)Math.Round(fx), (int)Math.Round(fy), (int)Math.Round(fz));

                if (_losPath.Count == 0 || _losPath[_losPath.Count - 1] != p)
                {
                    _losPath.Add(p);
                }

                fx += run;
                fy += rise;
                fz += zslp;

                if (_losPath.Count > 256)
                {
                    break;
                }
            }

            if (_losPath.Count == 0)
            {
                return true;
            }

            if (_losPath[_losPath.Count - 1] != (dx, dy, dz))
            {
                _losPath.Add((dx, dy, dz));
            }

            int endTop = endZ + 1;

            for (int i = 0; i < _losPath.Count; i++)
            {
                var (x, y, z) = _losPath[i];
                int pointTop = z + 1;
                bool atEnd = x == endX && y == endY;

                if (!TryGetLandCorners(world, x, y, out int landLow, out _, out int landTop))
                {
                    return true; // unknown map: don't claim a block
                }

                Land land = GetLand(world, x, y);

                if (land != null && !IsIgnoredLand(land.Graphic) && landLow <= pointTop && landTop >= z && (!atEnd || landLow > endTop || landTop < endZ))
                {
                    return false;
                }

                for (GameObject o = world.Map.GetTile(x, y, false); o != null; o = o.TNext)
                {
                    ushort graphic;

                    switch (o)
                    {
                        case Static st: graphic = st.OriginalGraphic; break;
                        case Multi m: graphic = m.Graphic; break;
                        case Item it when !it.IsMulti && !StaticFilters.IsField(it.Graphic) && !StaticFilters.IsWallOfStone(it.Graphic): graphic = it.Graphic; break;
                        default: continue;
                    }

                    if (graphic >= Client.Game.UO.FileManager.TileData.StaticData.Length)
                    {
                        continue;
                    }

                    ref StaticTiles data = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];

                    if (!data.IsWindow && !data.IsNoShoot)
                    {
                        continue;
                    }

                    int height = data.IsBridge ? data.Height / 2 : data.Height;

                    if (o.Z <= pointTop && o.Z + height >= z)
                    {
                        if (atEnd && o.Z <= endTop && o.Z + height >= endZ)
                        {
                            continue;
                        }

                        return false;
                    }
                }
            }

            return true;
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

        // ------- for FieldCastLog -------

        /// <summary>The preview's prediction for a target, ignoring range (each tile and whether it's placed).</summary>
        internal static int Predict(World world, int tx, int ty, int pz, int px, int py, int spell, int rules, (int X, int Y, bool Placed)[] result)
        {
            Compute(world, tx, ty, pz, px, py, spell, rules);
            int n = Math.Min(_tileCount, result.Length);

            for (int i = 0; i < n; i++)
            {
                result[i] = (_tiles[i].X, _tiles[i].Y, _tiles[i].Placed);
            }

            _keyTarget = null; // the hover cache no longer matches _tiles

            return n;
        }

        /// <summary>Height the preview used for the hovered target, if it was (x, y).</summary>
        internal static bool TryGetHoverTargetZ(int x, int y, out int pz)
        {
            pz = _keyTz;

            return _keyTarget != null && _keyTx == x && _keyTy == y;
        }

        internal static bool GetLandInfo(World world, int x, int y, out int low, out int avg, out int top) => TryGetLandCorners(world, x, y, out low, out avg, out top);

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

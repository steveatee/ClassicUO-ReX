// SPDX-License-Identifier: BSD-2-Clause
// ReX: small round status icons under a mobile's feet, with a short label underneath ("MS", "CURSE").
//  - Mortal Strike: the server turns a mobile's health bar yellow for exactly as long as the wound lasts
//    (TrueUO MortalStrike.BeginWound / EndWound set YellowHealthbar), so the icon shows while the flag is on.
//    Invulnerable NPCs also have yellow bars and are skipped. The ring drains over the expected duration
//    (6 s players, 12 s creatures, counted from when the flag appeared); for yourself the buff timer is used.
//  - Curse: anything CurseTracker has on the mobile (Curse, Corpse Skin, Hit Curse), ring = its estimated
//    time left. For yourself: the Curse / Mass Curse / Corpse Skin buffs.

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.Managers
{
    internal static class StatusIconsOverlay
    {
        private const int GAP = 3;
        private const uint PLAYER_MORTAL_MS = 6_000;
        private const uint NPC_MORTAL_MS = 12_000;
        private const ushort MORTAL_ICON = 0x0F52; // dagger
        private const ushort CURSE_ICON = 0x1AE2; // skull

        private enum Kind
        {
            Mortal,
            Curse
        }

        private static readonly Dictionary<uint, uint> _mortalSeenAt = new Dictionary<uint, uint>();
        private static readonly Dictionary<BuffIconType, uint> _buffSeenAt = new Dictionary<BuffIconType, uint>();
        private static readonly List<uint> _stale = new List<uint>();
        private static readonly Vector3 _plainHue = new Vector3(0f, ShaderHueTranslator.SHADER_NONE, 1f);

        private static Texture2D _mortalDisc, _curseDisc;
        private static RenderedText _mortalLabel, _curseLabel;

        public static void Draw(World world, UltimaBatcher2D batcher)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || world?.Player == null || world.Player.IsDestroyed || (!profile.ShowMortalIcon && !profile.ShowCurseIcon))
            {
                _mortalSeenAt.Clear();
                _buffSeenAt.Clear();

                return;
            }

            int size = Math.Clamp(profile.StatusIconSize, 16, 48);
            int drop = Math.Clamp(profile.StatusIconOffsetY, 0, 100);
            uint now = Time.Ticks;
            byte range = world.ClientViewRange;

            foreach (Mobile mobile in world.Mobiles.Values)
            {
                if (mobile.IsDestroyed || mobile.IsDead || mobile.Distance > range)
                {
                    continue;
                }

                bool self = mobile.Serial == world.Player.Serial;
                float mortal = -1f, curse = -1f;

                if (profile.ShowMortalIcon)
                {
                    mortal = self ? GetSelfMortal(world.Player, now) : GetMortal(mobile, now);
                }

                if (profile.ShowCurseIcon)
                {
                    curse = self ? GetSelfCurse(world.Player, now) : GetCurse(mobile);
                }

                if (mortal < 0f && curse < 0f)
                {
                    continue;
                }

                int count = (mortal >= 0f ? 1 : 0) + (curse >= 0f ? 1 : 0);
                int rowWidth = count * size + (count - 1) * GAP;

                Point p = mobile.RealScreenPosition;
                p.X += (int)mobile.Offset.X + 22;
                p.Y += (int)(mobile.Offset.Y - mobile.Offset.Z) + 22;
                p = Client.Game.Scene.Camera.WorldToScreen(p, true);

                int x = p.X - rowWidth / 2;
                int y = p.Y + drop;

                // Your own row goes below the timer circles if they're showing.
                if (self && TimerCirclesOverlay.LastRowBottom >= 0)
                {
                    y = Math.Max(y, TimerCirclesOverlay.LastRowBottom + GAP + 2);
                }

                if (mortal >= 0f)
                {
                    DrawIcon(batcher, Kind.Mortal, mortal, x, y, size);
                    x += size + GAP;
                }

                if (curse >= 0f)
                {
                    DrawIcon(batcher, Kind.Curse, curse, x, y, size);
                }
            }

            PruneMortals(world);
        }

        // Fraction of time left (0..1), or -1 when not mortally wounded.
        private static float GetMortal(Mobile mobile, uint now)
        {
            if (!mobile.IsYellowHits || mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
            {
                return -1f;
            }

            if (!_mortalSeenAt.TryGetValue(mobile.Serial, out uint seen))
            {
                seen = now;
                _mortalSeenAt[mobile.Serial] = seen;
            }

            uint duration = mobile.IsHuman || mobile.IsGargoyle ? PLAYER_MORTAL_MS : NPC_MORTAL_MS;

            return Math.Clamp(1f - (now - seen) / (float)duration, 0f, 1f);
        }

        private static float GetSelfMortal(PlayerMobile player, uint now)
        {
            float f = GetBuffFraction(player, BuffIconType.MortalStrike, now);

            if (f >= 0f)
            {
                return f;
            }

            return GetMortal(player, now);
        }

        private static float GetCurse(Mobile mobile)
        {
            return CurseTracker.TryGetCurseFraction(mobile.Serial, out float f) ? Math.Clamp(f, 0f, 1f) : -1f;
        }

        private static float GetSelfCurse(PlayerMobile player, uint now)
        {
            float best = -1f;
            best = Math.Max(best, GetBuffFraction(player, BuffIconType.Curse, now));
            best = Math.Max(best, GetBuffFraction(player, BuffIconType.MassCurse, now));
            best = Math.Max(best, GetBuffFraction(player, BuffIconType.CorpseSkin, now));

            return best;
        }

        // Time left on one of your buffs, measured from when it was first seen; -1 when absent.
        private static float GetBuffFraction(PlayerMobile player, BuffIconType type, uint now)
        {
            if (!player.BuffIcons.TryGetValue(type, out BuffIcon buff))
            {
                _buffSeenAt.Remove(type);

                return -1f;
            }

            if (buff.Timer == 0xFFFF_FFFF)
            {
                return 1f; // no duration given
            }

            if (!_buffSeenAt.TryGetValue(type, out uint seen) || seen > (uint)buff.Timer)
            {
                seen = now;
                _buffSeenAt[type] = seen;
            }

            long total = buff.Timer - seen;
            long left = buff.Timer - now;

            return total <= 0 ? 0f : Math.Clamp(left / (float)total, 0f, 1f);
        }

        private static void PruneMortals(World world)
        {
            if (_mortalSeenAt.Count == 0)
            {
                return;
            }

            _stale.Clear();

            foreach (KeyValuePair<uint, uint> kv in _mortalSeenAt)
            {
                if (world.Get(kv.Key) is not Mobile m || m.IsDestroyed || !m.IsYellowHits)
                {
                    _stale.Add(kv.Key);
                }
            }

            foreach (uint serial in _stale)
            {
                _mortalSeenAt.Remove(serial);
            }
        }

        private static void DrawIcon(UltimaBatcher2D batcher, Kind kind, float remaining, int x, int y, int size)
        {
            const float depth = 0f;

            bool mortal = kind == Kind.Mortal;
            Texture2D disc = mortal ? GetMortalDisc() : GetCurseDisc();
            Color ring = mortal ? new Color(255, 214, 64) : new Color(205, 140, 255);
            Color rim = mortal ? new Color(255, 120, 110) : new Color(190, 150, 235);

            batcher.Draw(disc, new Rectangle(x, y, size, size), _plainHue, depth);

            ushort graphic = mortal ? MORTAL_ICON : CURSE_ICON;
            ref readonly var art = ref Client.Game.UO.Arts.GetArt(graphic);

            if (art.Texture != null && art.UV.Width > 0 && art.UV.Height > 0)
            {
                Rectangle src = art.UV;
                Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(graphic);

                if (real.Width > 0 && real.Height > 0)
                {
                    src = new Rectangle(art.UV.X + real.X, art.UV.Y + real.Y, real.Width, real.Height);
                }

                float box = size * 0.62f;
                float scale = Math.Min(Math.Min(box / src.Width, box / src.Height), 2f);
                int w = Math.Max(1, (int)(src.Width * scale));
                int h = Math.Max(1, (int)(src.Height * scale));

                batcher.Draw(art.Texture, new Rectangle(x + (size - w) / 2, y + (size - h) / 2, w, h), src, _plainHue, depth);
            }

            float stroke = Math.Max(2f, size / 12f);
            float radius = size / 2f - stroke / 2f - 0.5f;
            Vector2 centre = new Vector2(x + size / 2f, y + size / 2f);

            // thin rim all the way round, then the time left as a brighter arc from 12 o'clock
            TimerCirclesOverlay.DrawArc(batcher, SolidColorTextureCache.GetTexture(rim * 0.55f), centre, radius, 1f, stroke, depth);

            if (remaining > 0f)
            {
                TimerCirclesOverlay.DrawArc(batcher, SolidColorTextureCache.GetTexture(ring), centre, radius, remaining, stroke, depth);
            }

            RenderedText label = mortal ? GetLabel(ref _mortalLabel, "MS") : GetLabel(ref _curseLabel, "CURSE");

            if (label != null && label.Width > 0)
            {
                label.Draw(batcher, x + (size - label.Width) / 2, y + size - 1, depth);
            }
        }

        private static RenderedText GetLabel(ref RenderedText cache, string text)
        {
            if (cache == null || cache.IsDestroyed)
            {
                cache = RenderedText.Create(text, 0x0481, 1, true, FontStyle.BlackBorder);
            }

            return cache;
        }

        private static Texture2D GetMortalDisc()
        {
            if (_mortalDisc == null || _mortalDisc.IsDisposed)
            {
                _mortalDisc = TimerCirclesOverlay.CreateDisc(new Color(150, 18, 18), 0.88f);
            }

            return _mortalDisc;
        }

        private static Texture2D GetCurseDisc()
        {
            if (_curseDisc == null || _curseDisc.IsDisposed)
            {
                _curseDisc = TimerCirclesOverlay.CreateDisc(new Color(78, 28, 120), 0.88f);
            }

            return _curseDisc;
        }
    }
}

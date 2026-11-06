// SPDX-License-Identifier: BSD-2-Clause
// ReX: small round status icons under a mobile's feet ("MS" label under the mortal one).
//  - Mortal Strike: the server turns a mobile's health bar yellow for exactly as long as the wound lasts
//    (TrueUO MortalStrike.BeginWound / EndWound set YellowHealthbar), so the icon shows while the flag is on.
//    Invulnerable NPCs also have yellow bars and are skipped. Ring: for your own mortals ("You deliver a mortal
//    wound!") it drains over UO Dreams' duration (8 s melee, 4 s ranged / thrown); for yourself the buff timer;
//    for anyone else's mortal the length isn't known, so the ring stays full until the bar clears.
//  Your own row also holds the swing / consumable timer circles (TimerCirclesOverlay), all one size, in one line.
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
        private const uint MELEE_MORTAL_MS = 8_000; // UO Dreams (TrueUO: 6 s players / 12 s creatures)
        private const uint RANGED_MORTAL_MS = 4_000;
        private const uint MY_MORTAL_MATCH_MS = 1_500;
        private const int CLILOC_DELIVER_MORTAL = 1060086; // You deliver a mortal wound!
        private const ushort MORTAL_ICON = 0x0F52; // dagger (fallback if the ability gump is missing)
        private const uint MORTAL_ABILITY_GUMP = 0x5208; // the Mortal Strike ability icon (0x5200 + 9 - 1)
        private const ushort DISCORD_ICON = 0x0EB3; // lute
        private const ushort DISCORD_EFFECT = 0x376A; // TrueUO re-sends this sparkle on a discorded creature every 1.25 s
        private const uint DISCORD_PULSE_MS = 1_250;
        private const uint DISCORD_HOLD_MS = 2_800; // icon stays this long after the last sparkle
        private const ushort CURSE_ICON = 0x1AE2; // skull

        private enum Kind
        {
            Mortal,
            Curse,
            Discord
        }

        private static readonly Dictionary<uint, MortalSeen> _mortalSeenAt = new Dictionary<uint, MortalSeen>();
        private static uint _myMortalAt;
        private static uint _myMortalDuration;

        private struct MortalSeen
        {
            public uint At;
            public uint Duration; // 0 = unknown
        }

        private static readonly HashSet<ushort> _rangedWeapons = new HashSet<ushort>
        {
            0x13B1, 0x13B2, // bow
            0x0F4F, 0x0F50, // crossbow
            0x13FC, 0x13FD, // heavy crossbow
            0x26C2, 0x26CC, // composite bow
            0x26C3, 0x26CD, // repeating crossbow
            0x27A5, 0x27F0, // yumi
            0x2D1E, 0x2D2A, // elven composite longbow
            0x2D1F, 0x2D2B, // magical shortbow
            0x08FF, 0x0901, 0x090A, // boomerang, cyclone, soul glaive (thrown)
        };

        /// <summary>Called for every cliloc message: notes when you deliver a mortal wound, and with what.</summary>
        public static void NotifyCliloc(World world, uint cliloc)
        {
            if (cliloc != CLILOC_DELIVER_MORTAL || world?.Player == null)
            {
                return;
            }

            _myMortalAt = Time.Ticks;
            _myMortalDuration = IsWieldingRanged(world.Player) ? RANGED_MORTAL_MS : MELEE_MORTAL_MS;

            // The yellow bar can show up in the same frame; give a just-seen unknown mortal this duration.
            uint bestSerial = 0;
            uint bestAt = 0;

            foreach (KeyValuePair<uint, MortalSeen> kv in _mortalSeenAt)
            {
                if (kv.Value.Duration == 0 && _myMortalAt - kv.Value.At < MY_MORTAL_MATCH_MS && kv.Value.At >= bestAt)
                {
                    bestSerial = kv.Key;
                    bestAt = kv.Value.At;
                }
            }

            if (bestSerial != 0)
            {
                _mortalSeenAt[bestSerial] = new MortalSeen { At = bestAt, Duration = _myMortalDuration };
                _myMortalAt = 0;
            }
        }

        /// <summary>
        /// Graphic effects: discordance makes the server repeat a sparkle (0x376A) on the creature every 1.25 s
        /// for as long as it lasts (TrueUO Discordance.ProcessDiscordance). Two sparkles in a row on the same
        /// creature = discorded; the icon goes a few seconds after they stop.
        /// </summary>
        public static void NotifyGraphicEffect(World world, uint source, uint target, ushort graphic)
        {
            if (graphic != DISCORD_EFFECT || world?.Player == null || ProfileManager.CurrentProfile?.ShowDiscordIcon != true)
            {
                return;
            }

            uint serial = SerialHelper.IsMobile(source) ? source : SerialHelper.IsMobile(target) ? target : 0;

            if (serial == 0 || serial == world.Player.Serial)
            {
                return;
            }

            uint now = Time.Ticks;

            if (_discord.TryGetValue(serial, out DiscordSeen seen) && now - seen.Last < DISCORD_HOLD_MS)
            {
                // ignore duplicates within the same pulse
                if (now - seen.Last >= DISCORD_PULSE_MS / 2)
                {
                    seen.Pulses++;
                }

                seen.Last = now;
            }
            else
            {
                seen = new DiscordSeen { First = now, Last = now, Pulses = 1 };
            }

            _discord[serial] = seen;
        }

        private static bool IsDiscorded(Mobile mobile, uint now)
        {
            if (!_discord.TryGetValue(mobile.Serial, out DiscordSeen seen))
            {
                return false;
            }

            if (now - seen.Last >= DISCORD_HOLD_MS)
            {
                _discord.Remove(mobile.Serial);

                return false;
            }

            return seen.Pulses >= 2;
        }

        private static bool IsWieldingRanged(PlayerMobile player)
        {
            Item weapon = player.FindItemByLayer(Layer.TwoHanded) ?? player.FindItemByLayer(Layer.OneHanded);

            return weapon != null && _rangedWeapons.Contains(weapon.Graphic);
        }
        private static readonly Dictionary<BuffIconType, uint> _buffSeenAt = new Dictionary<BuffIconType, uint>();
        private static readonly List<uint> _stale = new List<uint>();
        private static readonly Vector3 _plainHue = new Vector3(0f, ShaderHueTranslator.SHADER_NONE, 1f);

        private static Texture2D _mortalDisc, _curseDisc, _discordDisc, _mortalRound;
        private static bool _mortalRoundFailed;
        private static readonly Dictionary<uint, DiscordSeen> _discord = new Dictionary<uint, DiscordSeen>();

        private struct DiscordSeen
        {
            public uint First, Last;
            public int Pulses;
        }
        private static RenderedText _mortalLabel;

        public static void Draw(World world, UltimaBatcher2D batcher)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || world?.Player == null || world.Player.IsDestroyed)
            {
                return;
            }

            bool icons = profile.ShowMortalIcon || profile.ShowCurseIcon || profile.ShowDiscordIcon;

            if (!icons)
            {
                _mortalSeenAt.Clear();
                _buffSeenAt.Clear();
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

                bool discord = !self && profile.ShowDiscordIcon && IsDiscorded(mobile, now);
                int timers = self ? TimerCirclesOverlay.CollectCircles(world) : 0;
                int count = timers + (mortal >= 0f ? 1 : 0) + (curse >= 0f ? 1 : 0) + (discord ? 1 : 0);

                if (count == 0)
                {
                    continue;
                }

                int rowWidth = count * size + (count - 1) * GAP;

                Point p = mobile.RealScreenPosition;
                p.X += (int)mobile.Offset.X + 22;
                p.Y += (int)(mobile.Offset.Y - mobile.Offset.Z) + 22;
                p = Client.Game.Scene.Camera.WorldToScreen(p, true);

                int x = p.X - rowWidth / 2;
                int y = p.Y + drop;

                for (int i = 0; i < timers; i++)
                {
                    TimerCirclesOverlay.DrawCollected(batcher, i, x, y, size);
                    x += size + GAP;
                }

                if (mortal >= 0f)
                {
                    DrawIcon(batcher, Kind.Mortal, mortal, x, y, size);
                    x += size + GAP;
                }

                if (curse >= 0f)
                {
                    DrawIcon(batcher, Kind.Curse, curse, x, y, size);
                    x += size + GAP;
                }

                if (discord)
                {
                    DrawIcon(batcher, Kind.Discord, 1f, x, y, size);
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

            if (!_mortalSeenAt.TryGetValue(mobile.Serial, out MortalSeen seen))
            {
                seen = new MortalSeen { At = now };

                // Yours if you just delivered one ("You deliver a mortal wound!" arrives first).
                if (_myMortalAt != 0 && now - _myMortalAt < MY_MORTAL_MATCH_MS)
                {
                    seen.Duration = _myMortalDuration;
                    _myMortalAt = 0;
                }

                _mortalSeenAt[mobile.Serial] = seen;
            }

            if (seen.Duration == 0)
            {
                return 1f; // someone else's: length unknown, full ring until the bar clears
            }

            return Math.Clamp(1f - (now - seen.At) / (float)seen.Duration, 0f, 1f);
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

            foreach (KeyValuePair<uint, MortalSeen> kv in _mortalSeenAt)
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

            Color ring, rim;
            ushort art;
            Texture2D disc;

            switch (kind)
            {
                case Kind.Mortal:
                    ring = new Color(255, 214, 64);
                    rim = new Color(255, 120, 110);
                    art = MORTAL_ICON;
                    disc = GetMortalDisc();
                    break;

                case Kind.Curse:
                    ring = new Color(205, 140, 255);
                    rim = new Color(190, 150, 235);
                    art = CURSE_ICON;
                    disc = GetCurseDisc();
                    break;

                default:
                    ring = new Color(120, 230, 255);
                    rim = new Color(120, 200, 230);
                    art = DISCORD_ICON;
                    disc = GetDiscordDisc();
                    break;
            }

            Texture2D round = kind == Kind.Mortal ? GetMortalRound() : null;

            if (round != null)
            {
                // the real ability icon, cropped to a circle
                batcher.Draw(round, new Rectangle(x, y, size, size), _plainHue, depth);
            }
            else
            {
                batcher.Draw(disc, new Rectangle(x, y, size, size), _plainHue, depth);
                DrawArt(batcher, art, x, y, size, depth);
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

            // Only the mortal icon has a label.
            RenderedText label = kind == Kind.Mortal ? GetLabel(ref _mortalLabel, "MS") : null;

            if (label != null && label.Width > 0)
            {
                label.Draw(batcher, x + (size - label.Width) / 2, y + size - 1, depth);
            }
        }

        private static void DrawArt(UltimaBatcher2D batcher, ushort graphic, int x, int y, int size, float depth)
        {
            ref readonly var info = ref Client.Game.UO.Arts.GetArt(graphic);

            if (info.Texture == null || info.UV.Width <= 0 || info.UV.Height <= 0)
            {
                return;
            }

            Rectangle src = info.UV;
            Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(graphic);

            if (real.Width > 0 && real.Height > 0)
            {
                src = new Rectangle(info.UV.X + real.X, info.UV.Y + real.Y, real.Width, real.Height);
            }

            float scale = TimerCirclesOverlay.IconScale(src, size * 0.62f);
            int w = Math.Max(1, (int)(src.Width * scale));
            int h = Math.Max(1, (int)(src.Height * scale));

            batcher.Draw(info.Texture, new Rectangle(x + (size - w) / 2, y + (size - h) / 2, w, h), src, _plainHue, depth);
        }

        /// <summary>The Mortal Strike ability icon (gump 0x5208) with a soft round mask, built once.</summary>
        private static Texture2D GetMortalRound()
        {
            if (_mortalRound != null && !_mortalRound.IsDisposed)
            {
                return _mortalRound;
            }

            if (_mortalRoundFailed)
            {
                return null;
            }

            try
            {
                var gump = Client.Game.UO.FileManager.Gumps.GetGump(MORTAL_ABILITY_GUMP);
                int w = gump.Width, h = gump.Height;

                if (gump.Pixels.IsEmpty || w <= 0 || h <= 0)
                {
                    _mortalRoundFailed = true;
                    return null;
                }

                // Crop the centre square, then mask it to a circle (premultiplied alpha, 1.5 px soft edge).
                // The icon's own frame sits at its edge; trim 2 px so it doesn't show as a square-ish rim.
                int side = Math.Min(w, h) - 4;
                int ox = (w - side) / 2, oy = (h - side) / 2;
                Color[] data = new Color[side * side];
                float r = side / 2f;

                for (int py = 0; py < side; py++)
                {
                    for (int px = 0; px < side; px++)
                    {
                        uint packed = gump.Pixels[(py + oy) * w + px + ox];
                        Color c = new Color { PackedValue = packed };

                        float dx = px + 0.5f - r, dy = py + 0.5f - r;
                        float edge = Math.Clamp(r - MathF.Sqrt(dx * dx + dy * dy), 0f, 1.5f) / 1.5f;

                        data[py * side + px] = c.A == 0 ? Color.Transparent : new Color(c.R, c.G, c.B, (byte)255) * edge;
                    }
                }

                _mortalRound = new Texture2D(Client.Game.GraphicsDevice, side, side, false, SurfaceFormat.Color);
                _mortalRound.SetData(data);

                return _mortalRound;
            }
            catch (Exception)
            {
                _mortalRoundFailed = true;
                return null;
            }
        }

        private static Texture2D GetDiscordDisc()
        {
            if (_discordDisc == null || _discordDisc.IsDisposed)
            {
                _discordDisc = TimerCirclesOverlay.CreateDisc(new Color(18, 70, 110), 0.88f);
            }

            return _discordDisc;
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

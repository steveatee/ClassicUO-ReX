// SPDX-License-Identifier: BSD-2-Clause
// ReX: swing / casting / consumable timers drawn as circles under your character. Each running timer
// is a dark disc with the item's icon, and a coloured ring that fills clockwise from 12 o'clock as the
// timer runs; the circle disappears when it's done. Same timers (and colours) as the bar window.

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.Managers
{
    internal static class TimerCirclesOverlay
    {
        public const int STYLE_BARS = 0;
        public const int STYLE_CIRCLES = 1;
        public const int STYLE_BOTH = 2;

        public static readonly string[] StyleNames = { "Bars (movable window)", "Circles under your character", "Both" };

        private const int DISC_TEXTURE_SIZE = 64;
        private const int GAP = 4;
        private const int RING_SEGMENTS = 48;

        private static readonly List<SwingTimerGump.ActiveTimer> _timers = new List<SwingTimerGump.ActiveTimer>();
        private static Texture2D _disc;
        private static readonly Vector3 _plainHue = new Vector3(0f, ShaderHueTranslator.SHADER_NONE, 1f);

        public static bool ShowBars(Profile p) => p == null || p.TimerDisplayStyle != STYLE_CIRCLES;

        public static void Draw(World world, UltimaBatcher2D batcher)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || profile.TimerDisplayStyle == STYLE_BARS || world?.Player == null || world.Player.IsDestroyed)
            {
                return;
            }

            _timers.Clear();
            SwingTimerGump.CollectActiveTimers(_timers);

            if (_timers.Count == 0)
            {
                return;
            }

            int size = Math.Clamp(profile.TimerCircleSize, 16, 64);
            int rowWidth = _timers.Count * size + (_timers.Count - 1) * GAP;

            // Under the feet: the same anchor the health underline uses, then a configurable drop.
            var player = world.Player;
            Point p = player.RealScreenPosition;
            p.X += (int)player.Offset.X + 22;
            p.Y += (int)(player.Offset.Y - player.Offset.Z) + 22;
            p = Client.Game.Scene.Camera.WorldToScreen(p, true);

            int x = p.X - rowWidth / 2;
            int y = p.Y + Math.Clamp(profile.TimerCircleOffsetY, 0, 120);

            Texture2D disc = GetDisc();

            for (int i = 0; i < _timers.Count; i++)
            {
                DrawCircle(batcher, disc, _timers[i], x + i * (size + GAP), y, size);
            }
        }

        private static void DrawCircle(UltimaBatcher2D batcher, Texture2D disc, SwingTimerGump.ActiveTimer t, int x, int y, int size)
        {
            const float depth = 0f;

            // dark disc
            batcher.Draw(disc, new Rectangle(x, y, size, size), _plainHue, depth);

            // icon
            if (t.IconGraphic != 0)
            {
                ref readonly var art = ref Client.Game.UO.Arts.GetArt(t.IconGraphic);

                if (art.Texture != null && art.UV.Width > 0 && art.UV.Height > 0)
                {
                    // Item art has a lot of empty canvas; draw only the visible part.
                    Rectangle src = art.UV;
                    Rectangle real = Client.Game.UO.Arts.GetRealArtBounds(t.IconGraphic);

                    if (real.Width > 0 && real.Height > 0)
                    {
                        src = new Rectangle(art.UV.X + real.X, art.UV.Y + real.Y, real.Width, real.Height);
                    }

                    float iconBox = size * 0.66f;
                    float scale = Math.Min(Math.Min(iconBox / src.Width, iconBox / src.Height), 2f);
                    int w = Math.Max(1, (int)(src.Width * scale));
                    int h = Math.Max(1, (int)(src.Height * scale));

                    batcher.Draw(
                        art.Texture,
                        new Rectangle(x + (size - w) / 2, y + (size - h) / 2, w, h),
                        src,
                        ShaderHueTranslator.GetHueVector(t.IconHue, false, 1f),
                        depth
                    );
                }
            }

            // ring: faint full track, then the elapsed part in the timer's colour
            float stroke = Math.Max(2f, size / 11f);
            float radius = size / 2f - stroke / 2f - 0.5f;
            Vector2 centre = new Vector2(x + size / 2f, y + size / 2f);

            DrawArc(batcher, SolidColorTextureCache.GetTexture(new Color(20, 20, 20) * 0.75f), centre, radius, 1f, stroke, depth);

            float elapsed = t.Cooldown == 0 ? 1f : 1f - Math.Clamp((float)t.Remaining / t.Cooldown, 0f, 1f);

            if (elapsed > 0f)
            {
                DrawArc(batcher, SolidColorTextureCache.GetTexture(t.Color), centre, radius, elapsed, stroke, depth);
            }
        }

        private static void DrawArc(UltimaBatcher2D batcher, Texture2D texture, Vector2 centre, float radius, float fraction, float stroke, float depth)
        {
            int segments = Math.Max(1, (int)Math.Ceiling(RING_SEGMENTS * fraction));
            float start = -MathF.PI / 2f;
            float sweep = MathF.PI * 2f * fraction;

            Vector2 prev = centre + new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius;

            for (int i = 1; i <= segments; i++)
            {
                float a = start + sweep * i / segments;
                Vector2 next = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                batcher.DrawLine(texture, prev, next, _plainHue, stroke, depth);
                prev = next;
            }
        }

        // A soft-edged dark disc, premultiplied alpha.
        private static Texture2D GetDisc()
        {
            if (_disc != null && !_disc.IsDisposed)
            {
                return _disc;
            }

            const int n = DISC_TEXTURE_SIZE;
            Color[] data = new Color[n * n];
            float r = n / 2f;

            for (int py = 0; py < n; py++)
            {
                for (int px = 0; px < n; px++)
                {
                    float dx = px + 0.5f - r;
                    float dy = py + 0.5f - r;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    float edge = Math.Clamp(r - d, 0f, 1.5f) / 1.5f;
                    float a = 0.62f * edge;
                    data[py * n + px] = new Color(12, 12, 16) * a;
                }
            }

            _disc = new Texture2D(Client.Game.GraphicsDevice, n, n, false, SurfaceFormat.Color);
            _disc.SetData(data);

            return _disc;
        }
    }
}

// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Rotating target ring on the floor under your enemy.
    ///
    /// Who the enemy is: ClassicAssist's SetEnemy (its "enemy" alias) sends a Change Combatant packet (0xAA)
    /// to the client, and the server sends the same packet when you attack someone. The ring follows the last
    /// non-zero one; the zero the server sends when combat drops (war mode off, target gone) is ignored, so the
    /// ring stays on the enemy until a new one is set.
    ///
    /// Cost: the art is generated once (per size) and drawn as two quads; the spin is done by rotating the
    /// texture coordinates, so the ring stays flat on the floor while it turns.
    /// </summary>
    internal static class EnemyRing
    {
        private static readonly Lazy<BlendState> _blend = new Lazy<BlendState>
        (
            () => new BlendState
            {
                ColorSourceBlend = Blend.SourceAlpha,
                AlphaSourceBlend = Blend.SourceAlpha,
                ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaDestinationBlend = Blend.InverseSourceAlpha
            }
        );

        private static Texture2D _texture;
        private static int _textureRadius;

        public static uint Serial { get; private set; }

        /// <summary>Change Combatant (0xAA) arrived. Zero is ignored so the ring is sticky.</summary>
        public static void OnCombatant(uint serial)
        {
            if (serial != 0)
            {
                Serial = serial;
            }
        }

        public static void Clear() => Serial = 0;

        public static bool ShouldDraw(uint mobileSerial, uint playerSerial)
        {
            Profile p = ProfileManager.CurrentProfile;

            return p != null && p.EnemyRing && Serial != 0 && mobileSerial == Serial && mobileSerial != playerSerial;
        }

        public static void Draw(UltimaBatcher2D batcher, int x, int y, float depth)
        {
            Profile p = ProfileManager.CurrentProfile;
            int radius = Math.Clamp(p.EnemyRingSize, 20, 90);

            EnsureTexture(batcher.GraphicsDevice, radius);

            float alpha = Math.Clamp(p.EnemyRingOpacity / 100f, 0.1f, 1f);
            Vector3 hueVec = ShaderHueTranslator.GetHueVector(p.EnemyRingHue, false, alpha);

            double degrees = (double) Time.Ticks * Math.Clamp(p.EnemyRingSpeed, 0, 180) / 1000.0 % 360.0;
            float angle = (float) (degrees * Math.PI / 180.0);
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            // Floor ellipse: 2R wide, R tall, centred like the curse ring.
            int w = radius * 2;
            int half = radius >> 1;
            int left = x - radius;
            int top = y - half - 2;

            Rectangle back = new Rectangle(left, top, w, half);
            Rectangle front = new Rectangle(left, top + half, w, radius - half);

            batcher.SetBlendState(_blend.Value);
            batcher.SetSampler(SamplerState.LinearClamp);

            // Back half (behind the body) covers floor y -1..0, front half 0..1 (normalised).
            batcher.DrawQuadUV(texture: _texture, destination: back,
                uvTopLeft: Uv(-1, -1, cos, sin), uvTopRight: Uv(1, -1, cos, sin),
                uvBottomLeft: Uv(-1, 0, cos, sin), uvBottomRight: Uv(1, 0, cos, sin),
                hue: hueVec, depth: depth + 1f);

            batcher.DrawQuadUV(texture: _texture, destination: front,
                uvTopLeft: Uv(-1, 0, cos, sin), uvTopRight: Uv(1, 0, cos, sin),
                uvBottomLeft: Uv(-1, 1, cos, sin), uvBottomRight: Uv(1, 1, cos, sin),
                hue: hueVec, depth: depth + 1.49f);

            batcher.SetSampler(SamplerState.PointClamp);
            batcher.SetBlendState(BlendState.AlphaBlend);
        }

        /// <summary>Floor point (normalised, -1..1) to texture coordinates, with the art turned by the angle.</summary>
        internal static Vector2 Uv(float fx, float fy, float cos, float sin)
        {
            float tx = fx * cos + fy * sin;
            float ty = -fx * sin + fy * cos;

            return new Vector2(0.5f + 0.5f * tx, 0.5f + 0.5f * ty);
        }

        private static void EnsureTexture(GraphicsDevice device, int radius)
        {
            if (_texture != null && !_texture.IsDisposed && _textureRadius == radius)
            {
                return;
            }

            int size = Math.Min(radius * 4, 360); // 2x the drawn width, filtered down
            _texture?.Dispose();
            _texture = new Texture2D(device, size, size, false, SurfaceFormat.Color);
            _texture.SetData(GenerateArt(size));
            _textureRadius = radius;
        }

        /// <summary>
        /// White art with alpha (the hue colours it): outer and inner rings, 24 ticks between them (longer at
        /// the four cardinal points), and four arrows on the diagonals pointing inward. Supersampled 3x3.
        /// Everything stays inside 0.99 of the half-size, so clamped edge texels are transparent.
        /// </summary>
        internal static Color[] GenerateArt(int size)
        {
            var colors = new Color[size * size];
            const int SS = 3;
            float half = size * 0.5f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float sum = 0f;

                    for (int sy = 0; sy < SS; sy++)
                    {
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float nx = (px + (sx + 0.5f) / SS - half) / half;
                            float ny = (py + (sy + 0.5f) / SS - half) / half;
                            sum += ArtAlpha(nx, ny);
                        }
                    }

                    byte a = (byte) Math.Clamp((int) MathF.Round(sum / (SS * SS) * 255f), 0, 255);
                    colors[px + py * size] = new Color((byte) 255, (byte) 255, (byte) 255, a);
                }
            }

            return colors;
        }

        internal static float ArtAlpha(float x, float y)
        {
            float r = MathF.Sqrt(x * x + y * y);

            if (r > 0.99f)
            {
                return 0f;
            }

            const float OUTER = 0.84f, INNER = 0.62f;

            if (MathF.Abs(r - OUTER) < 0.035f || MathF.Abs(r - INNER) < 0.025f)
            {
                return 1f;
            }

            float angle = MathF.Atan2(y, x); // -pi..pi

            // Arrows on the diagonals, tip touching the outer ring, pointing to the centre.
            for (int i = 0; i < 4; i++)
            {
                float a = MathF.PI / 4f + i * MathF.PI / 2f;
                float u = x * MathF.Cos(a) + y * MathF.Sin(a);   // along the arrow axis (outward)
                float v = -x * MathF.Sin(a) + y * MathF.Cos(a);  // across it

                if (u >= 0.86f && u <= 0.985f && MathF.Abs(v) <= 0.15f * (u - 0.86f) / 0.125f)
                {
                    return 1f;
                }
            }

            // Ticks every 15 degrees between the rings; cardinal ones longer and thicker.
            if (r > INNER && r < OUTER)
            {
                const float STEP = MathF.PI / 12f;
                float nearest = MathF.Round(angle / STEP) * STEP;
                float across = MathF.Abs(MathF.Sin(angle - nearest)) * r; // distance from the tick line
                int index = ((int) MathF.Round(angle / STEP) % 24 + 24) % 24;
                bool cardinal = index % 6 == 0;

                if (cardinal ? across < 0.02f : across < 0.011f && r < 0.74f)
                {
                    return 1f;
                }

                return 0.14f; // faint band between the rings
            }

            return r < INNER ? 0.05f : 0f;
        }
    }
}

// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace ClassicUO.Game.Managers
{
    sealed class Aura : IDisposable
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

        private Texture2D _texture;
        private readonly int _radius;
        private readonly bool _ring;

        public Aura(int radius, bool ring = false)
        {
            _radius = radius;
            _ring = ring;
        }

        public int Radius => _radius;

        /// <summary>
        /// Ground ring: drawn flattened 2:1 so it lies on the isometric floor around the mobile's feet.
        /// The back half goes behind the body and the front half in front of it.
        /// </summary>
        public void DrawRing(UltimaBatcher2D batcher, int x, int y, ushort hue, float depth, float alpha)
        {
            EnsureTexture(batcher);

            int w = _radius * 2;
            int h = _radius; // flattened
            int left = x - _radius;
            int top = y - (h >> 1) - 2;

            Vector3 hueVec = ShaderHueTranslator.GetHueVector(hue, false, alpha);

            batcher.SetBlendState(_blend.Value);
            // back half (behind the body)
            batcher.Draw(_texture, new Rectangle(left, top, w, h >> 1), new Rectangle(0, 0, w, _radius), hueVec, depth + 1f);
            // front half (in front of the feet)
            batcher.Draw(_texture, new Rectangle(left, top + (h >> 1), w, h - (h >> 1)), new Rectangle(0, _radius, w, _radius), hueVec, depth + 1.49f);
            batcher.SetBlendState(BlendState.AlphaBlend);
        }

        private void EnsureTexture(UltimaBatcher2D batcher)
        {
            if (_texture == null || _texture.IsDisposed)
            {
                var w = _radius * 2;
                var h = _radius * 2;
                var data = _ring ? GenerateRingColors(_radius) : GenerateBlendedCircleColors(_radius);

                _texture?.Dispose();
                _texture = new Texture2D(batcher.GraphicsDevice, w, h, false, SurfaceFormat.Color);
                _texture.SetData(data);
            }
        }

        private static Color[] GenerateRingColors(int radius)
        {
            int size = radius * 2;
            var colors = new Color[size * size];
            float band = radius * 0.80f;   // where the ring is brightest
            float width = radius * 0.13f;  // softness of the band

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = MathF.Sqrt((x - radius + 0.5f) * (x - radius + 0.5f) + (y - radius + 0.5f) * (y - radius + 0.5f));

                    if (d > radius)
                    {
                        colors[x + y * size] = Color.Transparent;
                        continue;
                    }

                    float t = (d - band) / width;
                    float ringA = MathF.Exp(-t * t);                       // bright soft band
                    float innerA = d < band ? 0.12f * (d / band) : 0f;     // faint glow inside the ring
                    float a = Math.Clamp(ringA + innerA, 0f, 1f);

                    colors[x + y * size] = Color.White * a;
                }
            }

            return colors;
        }

        public void Draw(UltimaBatcher2D batcher, int x, int y, ushort hue, float depth, float alpha = 1f)
        {
            int yBump = -5;

            if (_texture == null || _texture.IsDisposed)
            {
                var w = _radius * 2;
                var h = _radius * 2;
                var data = GenerateBlendedCircleColors(_radius);

                _texture?.Dispose();
                _texture = new Texture2D(batcher.GraphicsDevice, w, h, false, SurfaceFormat.Color);
                _texture.SetData(data);
            }

            x -= (_texture.Width >> 1);
            y -= (_texture.Height >> 1);

            Vector3 hueVec = ShaderHueTranslator.GetHueVector(hue, false, alpha);

            batcher.SetBlendState(_blend.Value);
            batcher.Draw(_texture, new Rectangle(x, y + yBump, _radius * 2, _radius - yBump), new Rectangle(0, 0, _radius * 2, _radius - yBump), hueVec, depth + 1f);
            batcher.Draw(_texture, new Rectangle(x, y + _radius, _radius * 2, _radius + yBump), new Rectangle(0, _radius - yBump, _radius * 2, _radius + yBump), hueVec, depth + 1.49f);
            batcher.SetBlendState(BlendState.AlphaBlend);
        }

        private Color[] GenerateBlendedCircleColors(int radius)
        {
            var width = radius * 2;
            var height = radius * 2;

            var blendedColors = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var distance = (int)Math.Sqrt(Math.Pow(x - radius, 2) + Math.Pow(y - radius, 2));

                    if (distance > radius)
                    {
                        blendedColors[x + y * width] = Color.Transparent;
                        continue;
                    }

                    var opacityFactor = 1f - distance / (float)radius;

                    blendedColors[x + y * width] = Color.White * opacityFactor;
                }
            }

            return blendedColors;
        }

        public void Dispose()
        {
            if (_texture != null && !_texture.IsDisposed)
            {
                _texture.Dispose();
            }
        }
    }

    internal sealed class AuraManager
    {
        private readonly World _world;
        private readonly Aura _aura;
        private Aura _curseRing;
        private int _saveAuraUnderFeetType;

        public AuraManager(World world)
        {
            _world = world;
            _aura = new Aura(40);
        }

        public bool IsEnabled
        {
            get
            {
                if (ProfileManager.CurrentProfile == null)
                {
                    return false;
                }

                switch (ProfileManager.CurrentProfile.AuraUnderFeetType)
                {
                    default:
                    case 0: return false;

                    case 1 when _world.Player != null && _world.Player.InWarMode: return true;
                    case 2 when Keyboard.Ctrl && Keyboard.Shift: return true;
                    case 3: return true;
                }
            }
        }

        public void ToggleVisibility()
        {
            Profile currentProfile = ProfileManager.CurrentProfile;

            if (!IsEnabled)
            {
                _saveAuraUnderFeetType = currentProfile.AuraUnderFeetType;
                currentProfile.AuraUnderFeetType = 3;
            }
            else
            {
                currentProfile.AuraUnderFeetType = _saveAuraUnderFeetType;
            }
        }

        public void Draw(UltimaBatcher2D batcher, int x, int y, ushort hue, float depth)
        {
            _aura.Draw(batcher, x, y, hue, depth);
        }

        /// <summary>
        /// Curse aura: same cached ring texture, slowly pulsing and fading as the estimated curse time runs out.
        /// </summary>
        public void DrawCurse(UltimaBatcher2D batcher, int x, int y, float remainingFraction, float depth)
        {
            Profile p = ProfileManager.CurrentProfile;
            float pulse = 0.78f + 0.22f * MathF.Sin(Time.Ticks / 220f);
            float fade = 0.45f + 0.55f * Math.Clamp(remainingFraction, 0f, 1f);
            float alpha = Math.Clamp(p.CurseAuraOpacity / 100f, 0.1f, 1f) * pulse * fade;

            int radius = Math.Clamp(p.CurseAuraSize, 30, 110);

            if (_curseRing == null || _curseRing.Radius != radius)
            {
                _curseRing?.Dispose();
                _curseRing = new Aura(radius, ring: true);
            }

            _curseRing.DrawRing(batcher, x, y, p.CurseAuraHue, depth, alpha);
        }
    }
}
// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    internal sealed class SwingTimerFillLine : Control
    {
        private readonly Texture2D _texture;

        public SwingTimerFillLine(int x, int y, int maxWidth, int height, uint colorPacked)
        {
            X = x;
            Y = y;
            Width = maxWidth;
            Height = height;
            _texture = SolidColorTextureCache.GetTexture(new Color { PackedValue = colorPacked });
        }

        public int FillWidth { get; set; }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            int width = Math.Max(0, Math.Min(FillWidth, Width));
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, Alpha);
            float layerDepth = layerDepthRef;

            renderLists.AddGumpNoAtlas
            (
                batcher =>
                {
                    batcher.Draw(_texture, new Rectangle(x, y, width, Height), hueVector, layerDepth);

                    return true;
                }
            );

            return true;
        }
    }

    internal sealed class SwingTimerGump : Gump
    {
        private const byte FONT = 0xFF;
        private const ushort HUE_YELLOW = 0x35;
        private const ushort HUE_RED = 0x26;
        private const ushort HUE_GREEN = 0x3F;
        private const int ROW_HEIGHT = 22;
        private const int LABEL_WIDTH = 58;
        private const int FILL_WIDTH = 116;
        private const uint DEFAULT_SWING_COOLDOWN = 2000;
        private const ushort DEFAULT_WEAPON_TICKS = 17;
        private const ushort BARE_HAND_TICKS = 10;
        private const short MAX_SWING_SPEED_INCREASE = 60;

        private static readonly Dictionary<ushort, ushort> _weaponSpeeds = new Dictionary<ushort, ushort>();

        private readonly TimerRow _swingRow;
        private readonly TimerRow _appleRow;
        private readonly TimerRow _conflagRow;

        private bool _triggerSwing;
        private uint _timerSwing;
        private uint _tickSwing;

        public SwingTimerGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            CanCloseWithEsc = false;
            CanCloseWithRightClick = false;
            AcceptMouseInput = true;
            LayerOrder = UILayer.Over;
            Profile profile = ProfileManager.CurrentProfile;
            int rowCount = (profile?.ShowSwingTimer == true ? 1 : 0) + (profile?.ShowConsumableCooldowns == true ? 2 : 0);
            Width = LABEL_WIDTH + FILL_WIDTH + 1;
            Height = Math.Max(1, rowCount) * ROW_HEIGHT;

            Add
            (
                new AlphaBlendControl(0.6f)
                {
                    Width = Width,
                    Height = Height
                }
            );

            int y = 0;

            if (profile?.ShowSwingTimer == true)
            {
                _swingRow = AddTimerRow("Swing", HUE_YELLOW, Color.Red, y);
                y += ROW_HEIGHT;
            }

            if (profile?.ShowConsumableCooldowns == true)
            {
                _appleRow = AddTimerRow("Apple", HUE_GREEN, Color.LimeGreen, y);
                y += ROW_HEIGHT;
                _conflagRow = AddTimerRow("Conflag", HUE_YELLOW, Color.OrangeRed, y);
            }

            WantUpdateSize = false;
            LoadSwingTimerFile();
        }

        public override GumpType GumpType => GumpType.None;

        private TimerRow AddTimerRow(string title, ushort titleHue, Color fillColor, int y)
        {
            TimerRow row = new TimerRow(title, titleHue, fillColor, y);

            Add(row.TitleLabel);
            Add(row.FillLine);
            Add(row.TimerLabel);

            return row;
        }

        internal static void NotifyPlayerAnimation(World world, ushort action)
        {
            if (world?.Player == null)
            {
                return;
            }

            if (
                action >= 9 && action <= 15
                || action == 18
                || action == 19
                || action >= 26 && action <= 29
                || action == 31
            )
            {
                UIManager.GetGump<SwingTimerGump>()?.TriggerSwing();
            }
        }

        internal static void NotifyPlayerNewAnimation(World world)
        {
            if (world?.Player == null)
            {
                return;
            }

            UIManager.GetGump<SwingTimerGump>()?.TriggerSwing();
        }

        internal static void RefreshOpenGump(World world)
        {
            UIManager.GetGump<SwingTimerGump>()?.Dispose();

            Profile profile = ProfileManager.CurrentProfile;

            if (profile != null && (profile.ShowSwingTimer || profile.ShowConsumableCooldowns))
            {
                UIManager.Add
                (
                    new SwingTimerGump(world)
                    {
                        X = profile.SwingTimerLocation.X,
                        Y = profile.SwingTimerLocation.Y
                    }
                );
            }
        }

        internal static void StartConsumableCooldown(ConsumableCooldownKind kind, uint cooldown)
        {
            SwingTimerGump gump = UIManager.GetGump<SwingTimerGump>();

            if (gump == null)
            {
                return;
            }

            TimerRow row = kind == ConsumableCooldownKind.EnchantedApple ? gump._appleRow : gump._conflagRow;
            row?.Start(cooldown);
        }

        public void TriggerSwing()
        {
            if (!ProfileManager.CurrentProfile.ShowSwingTimer)
            {
                return;
            }

            _tickSwing = Time.Ticks;
            _triggerSwing = true;
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);
            ProfileManager.CurrentProfile.SwingTimerLocation = Location;
        }

        public override void Update()
        {
            base.Update();

            if (World.Player == null || World.Player.IsDestroyed)
            {
                return;
            }

            if (_triggerSwing)
            {
                RunSwingCooldown();
            }
            else
            {
                _swingRow?.SetReady();
            }

            _appleRow?.Update(false);
            _conflagRow?.Update(false);
        }

        private void RunSwingCooldown()
        {
            uint swingCooldown = GetSwingCooldown();
            uint elapsed = Time.Ticks - _tickSwing;

            if (_tickSwing == 0 || elapsed >= swingCooldown)
            {
                _timerSwing = 0;
                _tickSwing = 0;
                _triggerSwing = false;
                _swingRow?.SetReady();

                return;
            }

            uint remaining = swingCooldown - elapsed;
            _timerSwing = remaining;
            _swingRow?.SetRemaining(remaining, swingCooldown, true);
        }

        private sealed class TimerRow
        {
            private uint _cooldownStart;
            private uint _cooldownLength;

            public TimerRow(string title, ushort titleHue, Color fillColor, int y)
            {
                TitleLabel = new Label(title, true, titleHue, font: FONT, style: FontStyle.BlackBorder)
                {
                    X = 0,
                    Y = y,
                    Width = LABEL_WIDTH,
                    Height = 20
                };

                FillLine = new SwingTimerFillLine(LABEL_WIDTH + 1, y, FILL_WIDTH, 20, fillColor.PackedValue);

                TimerLabel = new Label("0", true, HUE_GREEN, font: FONT, style: FontStyle.BlackBorder)
                {
                    X = LABEL_WIDTH + 10,
                    Y = y
                };
            }

            public Label TitleLabel { get; }
            public SwingTimerFillLine FillLine { get; }
            public Label TimerLabel { get; }

            public void Start(uint cooldown)
            {
                _cooldownStart = Time.Ticks;
                _cooldownLength = cooldown;
            }

            public void Update(bool showTenths)
            {
                if (_cooldownStart == 0 || _cooldownLength == 0)
                {
                    SetReady();
                    return;
                }

                uint elapsed = Time.Ticks - _cooldownStart;

                if (elapsed >= _cooldownLength)
                {
                    _cooldownStart = 0;
                    _cooldownLength = 0;
                    SetReady();
                    return;
                }

                SetRemaining(_cooldownLength - elapsed, _cooldownLength, showTenths);
            }

            public void SetReady()
            {
                TimerLabel.Hue = HUE_GREEN;
                TimerLabel.Text = "0";
                FillLine.FillWidth = 0;
            }

            public void SetRemaining(uint remaining, uint cooldown, bool showTenths)
            {
                TimerLabel.Hue = HUE_RED;
                TimerLabel.Text = showTenths
                    ? (remaining / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)
                    : ((remaining + 999) / 1000).ToString(CultureInfo.InvariantCulture);
                FillLine.FillWidth = (int)Math.Clamp((long)remaining * FILL_WIDTH / Math.Max(1, cooldown), 0, FILL_WIDTH);
            }
        }

        private uint GetSwingCooldown()
        {
            ushort weaponTicks = DEFAULT_WEAPON_TICKS;

            Item weapon = World.Player.FindItemByLayer(Layer.TwoHanded)
                ?? World.Player.FindItemByLayer(Layer.OneHanded);

            if (weapon != null && _weaponSpeeds.TryGetValue(weapon.Graphic, out ushort configuredSpeed))
            {
                weaponTicks = configuredSpeed;
            }
            else if (weapon == null)
            {
                weaponTicks = BARE_HAND_TICKS;
            }

            if (weapon != null && (weapon.Graphic == 0x13B1 || weapon.Graphic == 0x13B2) && (weaponTicks == 20 || weaponTicks == 25))
            {
                weaponTicks = 17;
            }

            int staminaTicks = World.Player.Stamina / 30;
            short swingSpeedIncrease = Math.Min(World.Player.SwingSpeedIncrease, MAX_SWING_SPEED_INCREASE);
            double modifiedTicks = Math.Floor((weaponTicks - staminaTicks) * (100.0 / (100 + swingSpeedIncrease)));

            if (modifiedTicks < 5)
            {
                modifiedTicks = 5;
            }

            double final = Math.Min(modifiedTicks * 0.25, 10) * 1000;

            return Convert.ToUInt32(final);
        }

        private static void LoadSwingTimerFile()
        {
            if (_weaponSpeeds.Count > 0)
            {
                return;
            }

            string path = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Client");

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string swingPath = Path.Combine(path, "swingtimer.txt");

            if (!File.Exists(swingPath))
            {
                CreateDefaultSwingTimerFile(swingPath);
            }

            TextFileParser parser = new TextFileParser
            (
                File.ReadAllText(swingPath),
                new[] { ' ', '\t', ',', '=' },
                new[] { '#', ';' },
                new[] { '"', '"' }
            );

            while (!parser.IsEOF())
            {
                List<string> tokens = parser.ReadTokens();

                if (tokens == null || tokens.Count < 2)
                {
                    continue;
                }

                if (TryParseUShort(tokens[0], out ushort graphic) && TryParseUShort(tokens[1], out ushort speed))
                {
                    _weaponSpeeds[graphic] = speed;
                }
            }
        }

        private static bool TryParseUShort(string value, out ushort result)
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
            }

            return ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static void CreateDefaultSwingTimerFile(string swingPath)
        {
            ushort[] weapons =
            {
                0x1400, 0x1401, 0x13FE, 0x13FF, 0x1440, 0x1441, 0x13B5, 0x13B6,
                0x13B3, 0x13B4, 0x143C, 0x143D, 0x13AF, 0x13B0, 0x1404, 0x1405,
                0x13B7, 0x13B8, 0x0F60, 0x0F61, 0x0F5E, 0x0F5F, 0x13B9, 0x13BA,
                0x0F5C, 0x0F5D, 0x143A, 0x143B, 0x1406, 0x1407, 0x13B1, 0x13B2,
                0x1402, 0x1403, 0x0E87, 0x0E88, 0x0F49, 0x0F4A, 0x0F47, 0x0F48,
                0x0F4B, 0x0F4C, 0x0F45, 0x0F46, 0x13FA, 0x13FB, 0x1442, 0x1443,
                0x13F8, 0x13F9, 0x0DF0, 0x0DF1, 0x0E89, 0x0E8A, 0x0F4F, 0x0F50,
                0x0F62, 0x0F63, 0x143E, 0x143F, 0x0F4D, 0x0F4E, 0x1438, 0x1439,
                0x13FC, 0x13FD
            };

            ushort[] speeds =
            {
                53, 53, 58, 58, 45, 45, 43, 43, 40, 40, 30, 30, 40, 40, 45, 45,
                35, 35, 35, 35, 45, 45, 30, 30, 30, 30, 30, 30, 32, 32, 17, 17,
                50, 50, 45, 45, 37, 37, 30, 30, 37, 37, 37, 37, 30, 30, 30, 30,
                33, 33, 35, 35, 48, 48, 18, 18, 46, 46, 25, 25, 26, 26, 31, 31,
                10, 10
            };

            using StreamWriter writer = new StreamWriter(swingPath);

            for (int i = 0; i < weapons.Length && i < speeds.Length; i++)
            {
                writer.WriteLine($"0x{weapons[i]:X4}={speeds[i]}");
            }
        }
    }
}

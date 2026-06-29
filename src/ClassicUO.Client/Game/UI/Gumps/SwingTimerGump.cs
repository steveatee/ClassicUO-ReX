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
    internal enum ClientTimerKind
    {
        Swing,
        Casting,
        EnchantedApple,
        ConflagrationPotion,
        HealingPotion,
        Bandage,
        GrapesOfWrath,
        GrapesOfWrathCooldown
    }

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

    internal sealed class SwingTimerIcon : Control
    {
        private const int ICON_SIZE = 28;

        private readonly ushort _graphic;
        private readonly ushort _hue;

        public SwingTimerIcon(ushort graphic, ushort hue)
        {
            _graphic = graphic;
            _hue = hue;
            Width = ICON_SIZE;
            Height = ICON_SIZE;
            AcceptMouseInput = false;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            ref readonly var spriteInfo = ref Client.Game.UO.Arts.GetArt(_graphic);

            if (spriteInfo.Texture == null)
            {
                return false;
            }

            Texture2D texture = spriteInfo.Texture;
            Rectangle sourceRectangle = spriteInfo.UV;
            int width = sourceRectangle.Width;
            int height = sourceRectangle.Height;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            float scale = Math.Min((float)ICON_SIZE / width, (float)ICON_SIZE / height);
            int drawWidth = Math.Max(1, (int)(width * scale));
            int drawHeight = Math.Max(1, (int)(height * scale));
            Rectangle destination = new Rectangle
            (
                x + (ICON_SIZE - drawWidth) / 2,
                y + (ICON_SIZE - drawHeight) / 2,
                drawWidth,
                drawHeight
            );
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(_hue, false, Alpha);
            float layerDepth = layerDepthRef;

            renderLists.AddGumpWithAtlas
            (
                batcher =>
                {
                    batcher.Draw(texture, destination, sourceRectangle, hueVector, layerDepth);

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
        private const int ROW_HEIGHT = 30;
        private const int ICON_SIZE = 28;
        private const int ICON_SLOT_WIDTH = 34;
        private const int LABEL_WIDTH = 112;
        private const int FILL_WIDTH = 108;
        private const ushort BANDAGE_GRAPHIC = 0x0E21;
        private const ushort HEALING_POTION_GRAPHIC = 0x0F0C;
        private const ushort CONFLAGRATION_GRAPHIC = 0x0F06;
        private const ushort CONFLAGRATION_HUE = 0x0489;
        private const ushort APPLE_GRAPHIC = 0x2FD8;
        private const ushort APPLE_HUE = 0x0488;
        private const ushort GRAPES_OF_WRATH_GRAPHIC = 0x2FD7;
        private const ushort GRAPES_OF_WRATH_HUE = 0x0482;
        private const uint DEFAULT_SWING_COOLDOWN = 2000;
        private const ushort DEFAULT_WEAPON_TICKS = 17;
        private const ushort BARE_HAND_TICKS = 10;
        private const short MAX_SWING_SPEED_INCREASE = 60;
        private const uint BANDAGE_BEGIN_CLILOC_MIN = 500956;
        private const uint BANDAGE_BEGIN_CLILOC_MAX = 500960;
        private const uint UOF_BANDAGE_BEGIN_CLILOC_MIN = 501979;
        private const uint UOF_BANDAGE_BEGIN_CLILOC_MAX = 501980;

        private static readonly Dictionary<ushort, ushort> _weaponSpeeds = new Dictionary<ushort, ushort>();

        private readonly AlphaBlendControl _background;
        private readonly TimerRow _swingRow;
        private readonly TimerRow _castingRow;
        private readonly TimerRow _appleRow;
        private readonly TimerRow _conflagRow;
        private readonly TimerRow _healRow;
        private readonly TimerRow _bandageRow;
        private readonly TimerRow _grapesRow;
        private readonly TimerRow _grapesCooldownRow;

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
            Width = LABEL_WIDTH + FILL_WIDTH + 1;
            Height = ROW_HEIGHT;

            _background = new AlphaBlendControl(0.6f)
            {
                Width = Width,
                Height = Height
            };
            Add(_background);

            if (profile?.ShowSwingTimer == true)
            {
                _swingRow = AddTimerRow("Swing", HUE_YELLOW, Color.Red, 0x13B2);
                _swingRow.SetReady();
            }

            if (profile?.ShowConsumableCooldowns == true)
            {
                _castingRow = AddTimerRow("Casting", HUE_YELLOW, Color.DeepSkyBlue, 0x1F2E);
                _appleRow = AddTimerRow("Apple", HUE_GREEN, Color.LimeGreen, APPLE_GRAPHIC, APPLE_HUE);
                _conflagRow = AddTimerRow("Conflag", HUE_YELLOW, Color.OrangeRed, CONFLAGRATION_GRAPHIC, CONFLAGRATION_HUE);
                _healRow = AddTimerRow("Heal", HUE_GREEN, Color.Gold, HEALING_POTION_GRAPHIC);
                _bandageRow = AddTimerRow("Bandage", HUE_GREEN, Color.LightSkyBlue, BANDAGE_GRAPHIC);
                _grapesRow = AddTimerRow("Grapes", HUE_GREEN, Color.MediumPurple, GRAPES_OF_WRATH_GRAPHIC, GRAPES_OF_WRATH_HUE);
                _grapesCooldownRow = AddTimerRow("Grape CD", HUE_YELLOW, Color.MediumPurple, GRAPES_OF_WRATH_GRAPHIC, GRAPES_OF_WRATH_HUE);

                _castingRow.Hide();
                _appleRow.Hide();
                _conflagRow.Hide();
                _healRow.Hide();
                _bandageRow.Hide();
                _grapesRow.Hide();
                _grapesCooldownRow.Hide();
            }

            WantUpdateSize = false;
            LoadSwingTimerFile();
            ReflowRows();
        }

        public override GumpType GumpType => GumpType.None;

        private TimerRow AddTimerRow(string title, ushort titleHue, Color fillColor, ushort iconGraphic = 0, ushort iconHue = 0)
        {
            TimerRow row = new TimerRow(this, title, titleHue, fillColor, iconGraphic, iconHue);

            if (row.Icon != null)
            {
                Add(row.Icon);
            }
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

            TimerRow row = kind switch
            {
                ConsumableCooldownKind.EnchantedApple => gump._appleRow,
                ConsumableCooldownKind.ConflagrationPotion => gump._conflagRow,
                ConsumableCooldownKind.GrapesOfWrath => gump._grapesRow,
                _ => gump._healRow
            };
            row?.Start(cooldown);
            gump.ReflowRows();
        }

        internal static void StartGrapesOfWrathCooldown(uint duration, uint cooldown)
        {
            SwingTimerGump gump = UIManager.GetGump<SwingTimerGump>();

            if (gump == null)
            {
                return;
            }

            gump._grapesRow?.Start(duration);
            gump._grapesCooldownRow?.Start(cooldown, duration);
            gump.ReflowRows();
        }

        internal static uint GetRemainingTimer(World world, ClientTimerKind kind)
        {
            SwingTimerGump gump = UIManager.GetGump<SwingTimerGump>();

            if (kind == ClientTimerKind.Casting)
            {
                return world?.Player?.CastingRemaining ?? 0;
            }

            if (gump == null)
            {
                return 0;
            }

            return kind switch
            {
                ClientTimerKind.Swing => gump._timerSwing,
                ClientTimerKind.EnchantedApple => gump._appleRow?.Remaining ?? 0,
                ClientTimerKind.ConflagrationPotion => gump._conflagRow?.Remaining ?? 0,
                ClientTimerKind.HealingPotion => gump._healRow?.Remaining ?? 0,
                ClientTimerKind.Bandage => gump._bandageRow?.Remaining ?? 0,
                ClientTimerKind.GrapesOfWrath => gump._grapesRow?.Remaining ?? 0,
                ClientTimerKind.GrapesOfWrathCooldown => gump._grapesCooldownRow?.Remaining ?? 0,
                _ => 0
            };
        }

        internal static void StartBandageCooldown(World world, uint targetSerial = 0)
        {
            if (world?.Player == null || ProfileManager.CurrentProfile?.ShowConsumableCooldowns != true)
            {
                return;
            }

            if (targetSerial == world.Player.Serial && world.Player.HitsMax > 0 && world.Player.Hits >= world.Player.HitsMax)
            {
                return;
            }

            SwingTimerGump gump = UIManager.GetGump<SwingTimerGump>();

            if (gump == null)
            {
                return;
            }

            gump._bandageRow?.Start(GetBandageCooldown(world.Player));
            gump.ReflowRows();
        }

        internal static void NotifyCliloc(World world, uint cliloc)
        {
            if (
                cliloc >= BANDAGE_BEGIN_CLILOC_MIN && cliloc <= BANDAGE_BEGIN_CLILOC_MAX
                || cliloc >= UOF_BANDAGE_BEGIN_CLILOC_MIN && cliloc <= UOF_BANDAGE_BEGIN_CLILOC_MAX
            )
            {
                StartBandageCooldown(world);
            }
        }

        private static uint GetBandageCooldown(PlayerMobile player)
        {
            double seconds = Math.Min(8, Math.Ceiling(11.0 - player.Dexterity / 20));
            seconds = Math.Max(seconds, 4);

            return (uint)(seconds * 1000);
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

            UpdateCastingRow();
            _appleRow?.Update(false);
            _conflagRow?.Update(false);
            _healRow?.Update(false);
            _bandageRow?.Update(false);
            _grapesRow?.Update(false);
            _grapesCooldownRow?.Update(false);

            ReflowRows();
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

        private void UpdateCastingRow()
        {
            if (_castingRow == null)
            {
                return;
            }

            uint remaining = World.Player.CastingRemaining;
            uint duration = World.Player.CastingDuration;

            if (remaining == 0 || duration == 0)
            {
                _castingRow.Hide();
                return;
            }

            _castingRow.SetRemaining(remaining, duration, true);
        }

        private void ReflowRows()
        {
            int y = 0;
            ReflowRow(_swingRow, ref y);
            ReflowRow(_castingRow, ref y);
            ReflowRow(_appleRow, ref y);
            ReflowRow(_conflagRow, ref y);
            ReflowRow(_healRow, ref y);
            ReflowRow(_bandageRow, ref y);
            ReflowRow(_grapesRow, ref y);
            ReflowRow(_grapesCooldownRow, ref y);

            bool anyVisible = y > 0;
            IsVisible = anyVisible;
            Height = anyVisible ? y : ROW_HEIGHT;
            _background.Height = Height;
        }

        private static void ReflowRow(TimerRow row, ref int y)
        {
            if (row?.IsVisible != true)
            {
                return;
            }

            row.SetY(y);
            y += ROW_HEIGHT;
        }

        private sealed class TimerRow
        {
            private readonly string _title;
            private uint _cooldownStart;
            private uint _cooldownLength;

            public TimerRow(Gump parent, string title, ushort titleHue, Color fillColor, ushort iconGraphic, ushort iconHue)
            {
                _title = title;

                if (iconGraphic != 0)
                {
                    Icon = new SwingTimerIcon(iconGraphic, iconHue)
                    {
                        X = 2,
                        Y = 1
                    };
                }

                TitleLabel = new Label(title, true, titleHue, font: FONT, style: FontStyle.BlackBorder)
                {
                    X = Icon == null ? 0 : ICON_SLOT_WIDTH,
                    Y = 0,
                    Width = LABEL_WIDTH - (Icon == null ? 0 : ICON_SLOT_WIDTH),
                    Height = 20
                };

                FillLine = new SwingTimerFillLine(LABEL_WIDTH + 1, 0, FILL_WIDTH, 22, fillColor.PackedValue);

                TimerLabel = new Label("0", true, HUE_GREEN, font: FONT, style: FontStyle.BlackBorder)
                {
                    X = LABEL_WIDTH + 8,
                    Y = 1
                };
            }

            public Label TitleLabel { get; }
            public SwingTimerIcon Icon { get; }
            public SwingTimerFillLine FillLine { get; }
            public Label TimerLabel { get; }
            public bool IsVisible => TitleLabel.IsVisible;
            public uint Remaining
            {
                get
                {
                    if (_cooldownStart == 0 || _cooldownLength == 0)
                    {
                        return 0;
                    }

                    if (Time.Ticks < _cooldownStart)
                    {
                        return _cooldownStart - Time.Ticks + _cooldownLength;
                    }

                    uint elapsed = Time.Ticks - _cooldownStart;
                    return elapsed >= _cooldownLength ? 0 : _cooldownLength - elapsed;
                }
            }

            public void Start(uint cooldown, uint delay = 0)
            {
                _cooldownStart = Time.Ticks + delay;
                _cooldownLength = cooldown;

                if (delay == 0)
                {
                    Show();
                }
                else
                {
                    Hide();
                }
            }

            public void Update(bool showTenths)
            {
                if (_cooldownStart == 0 || _cooldownLength == 0)
                {
                    Hide();
                    return;
                }

                if (Time.Ticks < _cooldownStart)
                {
                    Hide();
                    return;
                }

                uint elapsed = Time.Ticks - _cooldownStart;

                if (elapsed >= _cooldownLength)
                {
                    _cooldownStart = 0;
                    _cooldownLength = 0;
                    Hide();
                    return;
                }

                SetRemaining(_cooldownLength - elapsed, _cooldownLength, showTenths);
            }

            public void SetReady()
            {
                Show();
                TimerLabel.Hue = HUE_GREEN;
                TimerLabel.Text = "0";
                FillLine.FillWidth = 0;
            }

            public void SetRemaining(uint remaining, uint cooldown, bool showTenths)
            {
                Show();
                TimerLabel.Hue = HUE_RED;
                TimerLabel.Text = showTenths
                    ? (remaining / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)
                    : ((remaining + 999) / 1000).ToString(CultureInfo.InvariantCulture);
                FillLine.FillWidth = (int)Math.Clamp((long)remaining * FILL_WIDTH / Math.Max(1, cooldown), 0, FILL_WIDTH);
            }

            public void SetY(int y)
            {
                if (Icon != null)
                {
                    Icon.Y = y + 1;
                }

                TitleLabel.Y = y + 2;
                FillLine.Y = y + 2;
                TimerLabel.Y = y + 3;
            }

            public void Hide()
            {
                if (Icon != null)
                {
                    Icon.IsVisible = false;
                }

                TitleLabel.IsVisible = false;
                FillLine.IsVisible = false;
                TimerLabel.IsVisible = false;
            }

            private void Show()
            {
                if (Icon != null)
                {
                    Icon.IsVisible = true;
                }

                if (TitleLabel.Text != _title)
                {
                    TitleLabel.Text = _title;
                }

                TitleLabel.IsVisible = true;
                FillLine.IsVisible = true;
                TimerLabel.IsVisible = true;
            }
        }

        private uint GetSwingCooldown()
        {
            ushort weaponTicks = DEFAULT_WEAPON_TICKS;

            Item weapon = World.Player.FindItemByLayer(Layer.TwoHanded)
                ?? World.Player.FindItemByLayer(Layer.OneHanded);

            if (weapon != null && _weaponSpeeds.TryGetValue(weapon.Graphic, out ushort configuredSpeed))
            {
                weaponTicks = GetWeaponTicks(weapon.Graphic, configuredSpeed);
            }
            else if (weapon == null)
            {
                weaponTicks = BARE_HAND_TICKS;
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

        private static ushort GetWeaponTicks(ushort graphic, ushort configuredSpeed)
        {
            if ((graphic == 0x13B1 || graphic == 0x13B2) && (configuredSpeed == 20 || configuredSpeed == 25))
            {
                return 17;
            }

            if (configuredSpeed > BARE_HAND_TICKS)
            {
                return (ushort)Math.Max(BARE_HAND_TICKS, (int)Math.Round(configuredSpeed * 0.4, MidpointRounding.AwayFromZero));
            }

            return configuredSpeed;
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

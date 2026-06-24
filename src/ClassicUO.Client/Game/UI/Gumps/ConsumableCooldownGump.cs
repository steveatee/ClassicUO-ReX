// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Globalization;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    internal enum ConsumableCooldownKind
    {
        EnchantedApple,
        ConflagrationPotion
    }

    internal sealed class ConsumableCooldownGump : Gump
    {
        private const byte FONT = 0xFF;
        private const ushort HUE_RED = 0x26;
        private const ushort HUE_GREEN = 0x3F;
        private const uint ENCHANTED_APPLE_COOLDOWN = 30000;
        private const uint CONFLAGRATION_COOLDOWN = 30000;
        private const uint CONFLAGRATION_PENDING_TIME = 3000;

        private const ushort APPLE_GRAPHIC = 0x2FD8;
        private const ushort APPLE_HUE = 0x0488;
        private const int APPLE_CLILOC = 1072952;
        private const ushort CONFLAGRATION_GRAPHIC = 0x0F06;
        private const ushort CONFLAGRATION_HUE = 0x0489;
        private const int CONFLAGRATION_CLILOC = 1072095;
        private const int GREATER_CONFLAGRATION_CLILOC = 1072098;

        private const uint APPLE_SUCCESS_CLILOC = 1074846;
        private const uint APPLE_COOLDOWN_CLILOC = 1151180;
        private const uint GENERIC_ITEM_COOLDOWN_CLILOC = 1070772;
        private const uint CONFLAGRATION_COOLDOWN_CLILOC = 1072529;
        private const ushort CONFLAGRATION_EFFECT = 0x0F0D;
        private const ushort CONFLAGRATION_CAPTURED_EFFECT = 0x0F06;
        private const ushort CONFLAGRATION_CAPTURED_EFFECT_HUE = 0x0488;

        private static uint _pendingConflagrationUntil;

        private readonly ConsumableCooldownKind _kind;
        private readonly SwingTimerFillLine _fillLine;
        private readonly Label _timerLabel;
        private uint _cooldownStart;
        private uint _cooldownLength;

        public ConsumableCooldownGump(World world, ConsumableCooldownKind kind) : base(world, 0, 0)
        {
            _kind = kind;
            CanMove = true;
            CanCloseWithEsc = false;
            CanCloseWithRightClick = false;
            AcceptMouseInput = true;
            LayerOrder = UILayer.Over;
            Width = 141;
            Height = 24;

            Add
            (
                new AlphaBlendControl(0.6f)
                {
                    Width = Width,
                    Height = Height
                }
            );

            Label titleLabel = new Label(GetTitle(kind), true, GetTitleHue(kind), font: FONT, style: FontStyle.BlackBorder)
            {
                X = 0,
                Y = 0,
                Width = 40,
                Height = 20
            };
            Add(titleLabel);

            _fillLine = new SwingTimerFillLine(titleLabel.Width + 1, 0, 100, 20, GetFillColor(kind).PackedValue);
            Add(_fillLine);

            _timerLabel = new Label("0", true, HUE_GREEN, font: FONT, style: FontStyle.BlackBorder)
            {
                X = titleLabel.Width + 10,
                Y = 0
            };
            Add(_timerLabel);

            WantUpdateSize = false;
        }

        public override GumpType GumpType => GumpType.None;

        internal static void RefreshOpenGumps(World world)
        {
            UIManager.GetGump<ConsumableCooldownGump>(0)?.Dispose();
            UIManager.GetGump<ConsumableCooldownGump>(1)?.Dispose();

            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || !profile.ShowConsumableCooldowns)
            {
                return;
            }

            UIManager.Add
            (
                new ConsumableCooldownGump(world, ConsumableCooldownKind.EnchantedApple)
                {
                    LocalSerial = 0,
                    X = profile.EnchantedAppleCooldownLocation.X,
                    Y = profile.EnchantedAppleCooldownLocation.Y
                }
            );

            UIManager.Add
            (
                new ConsumableCooldownGump(world, ConsumableCooldownKind.ConflagrationPotion)
                {
                    LocalSerial = 1,
                    X = profile.ConflagrationCooldownLocation.X,
                    Y = profile.ConflagrationCooldownLocation.Y
                }
            );
        }

        internal static void NotifyItemDoubleClick(World world, uint serial)
        {
            if (world?.Player == null || ProfileManager.CurrentProfile?.ShowConsumableCooldowns != true)
            {
                return;
            }

            Item item = world.Items.Get(serial);

            if (item == null)
            {
                return;
            }

            if (IsConflagrationPotion(world, item))
            {
                _pendingConflagrationUntil = Time.Ticks + CONFLAGRATION_PENDING_TIME;
            }
        }

        internal static void NotifyCliloc(World world, uint cliloc, string arguments)
        {
            if (world?.Player == null || ProfileManager.CurrentProfile?.ShowConsumableCooldowns != true)
            {
                return;
            }

            switch (cliloc)
            {
                case APPLE_SUCCESS_CLILOC:
                    StartCooldown(ConsumableCooldownKind.EnchantedApple, ENCHANTED_APPLE_COOLDOWN);
                    break;

                case APPLE_COOLDOWN_CLILOC:
                case GENERIC_ITEM_COOLDOWN_CLILOC:
                    break;

                case CONFLAGRATION_COOLDOWN_CLILOC:
                    if (_pendingConflagrationUntil >= Time.Ticks && TryGetFirstNumber(arguments, out int seconds))
                    {
                        StartCooldown(ConsumableCooldownKind.ConflagrationPotion, (uint)Math.Max(1, seconds) * 1000);
                    }
                    break;
            }
        }

        internal static void NotifyGraphicEffect(World world, uint source, ushort graphic, ushort hue)
        {
            if (world?.Player == null || ProfileManager.CurrentProfile?.ShowConsumableCooldowns != true)
            {
                return;
            }

            if (source == world.Player.Serial && IsConflagrationEffect(graphic, hue))
            {
                _pendingConflagrationUntil = 0;
                StartCooldown(ConsumableCooldownKind.ConflagrationPotion, CONFLAGRATION_COOLDOWN);
            }
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);

            if (_kind == ConsumableCooldownKind.EnchantedApple)
            {
                ProfileManager.CurrentProfile.EnchantedAppleCooldownLocation = Location;
            }
            else
            {
                ProfileManager.CurrentProfile.ConflagrationCooldownLocation = Location;
            }
        }

        public override void Update()
        {
            base.Update();

            if (_cooldownStart == 0 || _cooldownLength == 0)
            {
                _timerLabel.Hue = HUE_GREEN;
                _timerLabel.Text = "0";
                _fillLine.FillWidth = 0;

                return;
            }

            uint elapsed = Time.Ticks - _cooldownStart;

            if (elapsed >= _cooldownLength)
            {
                _cooldownStart = 0;
                _cooldownLength = 0;
                _timerLabel.Hue = HUE_GREEN;
                _timerLabel.Text = "0";
                _fillLine.FillWidth = 0;

                return;
            }

            uint remaining = _cooldownLength - elapsed;
            _timerLabel.Hue = HUE_RED;
            _timerLabel.Text = ((remaining + 999) / 1000).ToString();
            _fillLine.FillWidth = (int)Math.Clamp((long)remaining * 100 / _cooldownLength, 0, 100);
        }

        private static void StartCooldown(ConsumableCooldownKind kind, uint cooldown)
        {
            ConsumableCooldownGump gump = UIManager.GetGump<ConsumableCooldownGump>(kind == ConsumableCooldownKind.EnchantedApple ? 0u : 1u);

            if (gump == null)
            {
                return;
            }

            gump._cooldownStart = Time.Ticks;
            gump._cooldownLength = cooldown;
        }

        private static bool IsConflagrationPotion(World world, Item item)
        {
            int nameCliloc = world.OPL.GetNameCliloc(item.Serial);

            return nameCliloc == CONFLAGRATION_CLILOC
                || nameCliloc == GREATER_CONFLAGRATION_CLILOC
                || item.Graphic == CONFLAGRATION_GRAPHIC && item.Hue == CONFLAGRATION_HUE;
        }

        private static bool IsConflagrationEffect(ushort graphic, ushort hue)
        {
            return graphic == CONFLAGRATION_EFFECT && hue == CONFLAGRATION_HUE
                || graphic == CONFLAGRATION_CAPTURED_EFFECT && hue == CONFLAGRATION_CAPTURED_EFFECT_HUE;
        }

        private static bool TryGetFirstNumber(string arguments, out int value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(arguments))
            {
                return false;
            }

            foreach (string token in arguments.Split(new[] { '\t', ' ', ',', '.', ';', ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetTitle(ConsumableCooldownKind kind)
        {
            return kind == ConsumableCooldownKind.EnchantedApple ? "Apple" : "Conflag";
        }

        private static ushort GetTitleHue(ConsumableCooldownKind kind)
        {
            return kind == ConsumableCooldownKind.EnchantedApple ? (ushort)0x3F : (ushort)0x35;
        }

        private static Color GetFillColor(ConsumableCooldownKind kind)
        {
            return kind == ConsumableCooldownKind.EnchantedApple ? Color.LimeGreen : Color.OrangeRed;
        }
    }
}

// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    internal sealed class UCCLinesGump : Gump
    {
        private const byte FONT = 0xFF;
        private const ushort OPTION_HUE = 999;
        private const ushort TITLE_HUE = 0x5D;
        private const ushort LAST_TARGET_HUE = 0x35;

        private readonly Checkbox _lastTargetCheckbox;
        private readonly Checkbox _huntingModeCheckbox;
        private readonly Checkbox _blueCheckbox;
        private readonly Checkbox _redCheckbox;
        private readonly Checkbox _orangeCheckbox;
        private readonly Checkbox _criminalCheckbox;
        private readonly Label _lastTargetLabel;

        public UCCLinesGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            CanCloseWithEsc = false;
            CanCloseWithRightClick = false;
            AcceptMouseInput = true;
            AcceptKeyboardInput = false;
            WantUpdateSize = false;
            LayerOrder = UILayer.Over;

            Width = 180;
            Height = 85;

            Add(new AlphaBlendControl(0.6f)
            {
                Width = Width,
                Height = Height
            });

            Add(new Label("UCC -LINES-", true, TITLE_HUE, 0, 1, FontStyle.BlackBorder)
            {
                X = 2,
                Y = 2
            });

            _lastTargetCheckbox = new Checkbox(0x00D2, 0x00D3, "Last Target", FONT, OPTION_HUE)
            {
                X = 2,
                Y = 20,
                IsChecked = ProfileManager.CurrentProfile.UCCLinesShowLastTarget
            };
            _lastTargetCheckbox.ValueChanged += (_, _) =>
                ProfileManager.CurrentProfile.UCCLinesShowLastTarget = _lastTargetCheckbox.IsChecked;
            Add(_lastTargetCheckbox);

            _lastTargetLabel = new Label(string.Empty, true, LAST_TARGET_HUE, 0, 1, FontStyle.BlackBorder)
            {
                X = 2,
                Y = _lastTargetCheckbox.Bounds.Bottom
            };
            Add(_lastTargetLabel);

            _huntingModeCheckbox = new Checkbox(0x00D2, 0x00D3, "HM", FONT, OPTION_HUE)
            {
                X = 2,
                Y = _lastTargetLabel.Bounds.Bottom + 20,
                IsChecked = ProfileManager.CurrentProfile.UCCLinesHuntingMode
            };
            _huntingModeCheckbox.ValueChanged += (_, _) =>
                ProfileManager.CurrentProfile.UCCLinesHuntingMode = _huntingModeCheckbox.IsChecked;
            Add(_huntingModeCheckbox);

            _blueCheckbox = CreateModeCheckbox("B", _huntingModeCheckbox.Bounds.Right, ProfileManager.CurrentProfile.UCCLinesHuntingBlue,
                value => ProfileManager.CurrentProfile.UCCLinesHuntingBlue = value);
            _redCheckbox = CreateModeCheckbox("R", _blueCheckbox.Bounds.Right, ProfileManager.CurrentProfile.UCCLinesHuntingRed,
                value => ProfileManager.CurrentProfile.UCCLinesHuntingRed = value);
            _orangeCheckbox = CreateModeCheckbox("O", _redCheckbox.Bounds.Right, ProfileManager.CurrentProfile.UCCLinesHuntingOrange,
                value => ProfileManager.CurrentProfile.UCCLinesHuntingOrange = value);
            _criminalCheckbox = CreateModeCheckbox("C", _orangeCheckbox.Bounds.Right, ProfileManager.CurrentProfile.UCCLinesHuntingCriminal,
                value => ProfileManager.CurrentProfile.UCCLinesHuntingCriminal = value);
        }

        public void DrawOverlay(UltimaBatcher2D batcher)
        {
            if (!ProfileManager.CurrentProfile.UCCLinesEnabled || World?.Player == null || World.Player.IsDestroyed || World.Player.IsDead)
            {
                return;
            }

            UpdateLastTargetLabel();

            HashSet<uint> trackedTargets = GetTrackedTargets();
            uint lastTargetSerial = World.TargetManager.LastTargetInfo.Serial;
            Point playerPoint = GetAnchorPoint(World.Player);

            foreach (Mobile mobile in World.Mobiles.Values)
            {
                if (mobile == null || mobile.IsDestroyed || mobile.IsDead || mobile == World.Player || !mobile.IsHuman)
                {
                    continue;
                }

                if (!ShouldDrawMobile(mobile, trackedTargets, lastTargetSerial, out Color color))
                {
                    continue;
                }

                Point mobilePoint = GetAnchorPoint(mobile);
                batcher.DrawLine
                (
                    SolidColorTextureCache.GetTexture(color),
                    new Vector2(mobilePoint.X, mobilePoint.Y),
                    new Vector2(playerPoint.X, playerPoint.Y),
                    ShaderHueTranslator.GetHueVector(0),
                    1,
                    0f
                );
            }
        }

        protected override void OnDragEnd(int x, int y)
        {
            base.OnDragEnd(x, y);
            ProfileManager.CurrentProfile.UCCLinesLocation = Location;
        }

        private Checkbox CreateModeCheckbox(string label, int x, bool initialValue, Action<bool> onChanged)
        {
            Checkbox checkbox = new Checkbox(0x00D2, 0x00D3, label, FONT, OPTION_HUE)
            {
                X = x,
                Y = _huntingModeCheckbox.Y,
                IsChecked = initialValue
            };

            checkbox.ValueChanged += (_, _) => onChanged(checkbox.IsChecked);
            Add(checkbox);

            return checkbox;
        }

        private HashSet<uint> GetTrackedTargets()
        {
            HashSet<uint> tracked = new HashSet<uint>();

            if (!ProfileManager.CurrentProfile.UCCLinesHuntingMode)
            {
                return tracked;
            }

            foreach (Mobile mobile in World.Mobiles.Values)
            {
                if (mobile == null || mobile.IsDestroyed || mobile == World.Player || !mobile.IsHuman || World.Party.Contains(mobile.Serial))
                {
                    continue;
                }

                switch (mobile.NotorietyFlag)
                {
                    case NotorietyFlag.Innocent:
                        if (ProfileManager.CurrentProfile.UCCLinesHuntingBlue)
                        {
                            tracked.Add(mobile.Serial);
                        }
                        break;
                    case NotorietyFlag.Criminal:
                        if (ProfileManager.CurrentProfile.UCCLinesHuntingCriminal)
                        {
                            tracked.Add(mobile.Serial);
                        }
                        break;
                    case NotorietyFlag.Enemy:
                        if (ProfileManager.CurrentProfile.UCCLinesHuntingOrange)
                        {
                            tracked.Add(mobile.Serial);
                        }
                        break;
                    case NotorietyFlag.Murderer:
                        if (ProfileManager.CurrentProfile.UCCLinesHuntingRed)
                        {
                            tracked.Add(mobile.Serial);
                        }
                        break;
                }
            }

            return tracked;
        }

        private static Color GetLineColor(NotorietyFlag notorietyFlag)
        {
            switch (notorietyFlag)
            {
                case NotorietyFlag.Innocent:
                    return Color.Blue;
                case NotorietyFlag.Ally:
                    return Color.Green;
                case NotorietyFlag.Criminal:
                case NotorietyFlag.Gray:
                    return Color.Gray;
                case NotorietyFlag.Enemy:
                    return Color.Orange;
                case NotorietyFlag.Murderer:
                    return Color.Red;
                case NotorietyFlag.Invulnerable:
                    return Color.Yellow;
                default:
                    return Color.White;
            }
        }

        private Point GetAnchorPoint(Mobile mobile)
        {
            Point point = mobile.RealScreenPosition;
            point.X += (int) mobile.Offset.X + 22;
            point.Y += (int) (mobile.Offset.Y - mobile.Offset.Z) + 22;
            return Client.Game.Scene.Camera.WorldToScreen(point, true);
        }

        private bool ShouldDrawMobile(Mobile mobile, HashSet<uint> trackedTargets, uint lastTargetSerial, out Color color)
        {
            bool draw = trackedTargets.Contains(mobile.Serial);
            color = GetLineColor(mobile.NotorietyFlag);

            if (ProfileManager.CurrentProfile.UCCLinesShowLastTarget && lastTargetSerial != 0 && mobile.Serial == lastTargetSerial)
            {
                draw = true;
                color = Color.White;
            }

            return draw;
        }

        private void UpdateLastTargetLabel()
        {
            if (!ProfileManager.CurrentProfile.UCCLinesShowLastTarget)
            {
                _lastTargetLabel.Text = string.Empty;
                return;
            }

            if (!(World.Get(World.TargetManager.LastTargetInfo.Serial) is Mobile mobile))
            {
                _lastTargetLabel.Text = string.Empty;
                return;
            }

            _lastTargetLabel.Text = mobile.Name ?? string.Empty;
            _lastTargetLabel.Hue = Notoriety.GetHue(mobile.NotorietyFlag);
        }
    }
}

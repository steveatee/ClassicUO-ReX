// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.Managers
{
    internal sealed class HealthLinesManager
    {
        private const int BAR_WIDTH = 34; //28;
        private const int BAR_HEIGHT = 8;
        private const int BAR_WIDTH_HALF = BAR_WIDTH >> 1;
        private const int BAR_HEIGHT_HALF = BAR_HEIGHT >> 1;
        private const int OLD_BAR_HEIGHT = 3;

        const ushort BACKGROUND_GRAPHIC = 0x1068;
        const ushort HP_GRAPHIC = 0x1069;

        private static readonly Texture2D _oldEdge = SolidColorTextureCache.GetTexture(Color.Black);
        private static readonly Texture2D _oldBack = SolidColorTextureCache.GetTexture(Color.Red);

        private readonly World _world;

        public HealthLinesManager(World world) { _world = world; }

        public bool IsEnabled =>
            ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.ShowMobilesHP;

        public void Draw(UltimaBatcher2D batcher, float layerDepth)
        {
            var camera = Client.Game.Scene.Camera;
            int mode = ProfileManager.CurrentProfile.MobileHPType;

            if (mode < 0)
            {
                return;
            }

            int showWhen = ProfileManager.CurrentProfile.MobileHPShowWhen;
            var useNewTargetSystem = ProfileManager.CurrentProfile.UseNewTargetSystem;
            var animations = Client.Game.UO.Animations;
            var isEnabled = IsEnabled;

            foreach (Mobile mobile in _world.Mobiles.Values)
            {
                if (mobile.IsDestroyed)
                {
                    continue;
                }

                var newTargSystem = false;
                var forceDraw = false;
                var passive = mobile.Serial != _world.Player.Serial;

                if (_world.TargetManager.LastTargetInfo.Serial == mobile ||
                    _world.TargetManager.LastAttack == mobile ||
                    _world.TargetManager.SelectedTarget == mobile ||
                    _world.TargetManager.NewTargetSystemSerial == mobile)
                {
                    newTargSystem = useNewTargetSystem && _world.TargetManager.NewTargetSystemSerial == mobile;
                    passive = false;
                    forceDraw = true;
                }

                int current = mobile.Hits;
                int max = mobile.HitsMax;

                if (!newTargSystem)
                {
                    if (max == 0)
                    {
                        continue;
                    }

                    if (showWhen == 1 && current == max)
                    {
                        continue;
                    }
                }

                Point p = mobile.RealScreenPosition;
                p.X += (int)mobile.Offset.X + 22 + 5;
                p.Y += (int)(mobile.Offset.Y - mobile.Offset.Z) + 22 + 5;
                var offsetY = 0;

                if (isEnabled)
                {
                    if (mode != 1 && !mobile.IsDead)
                    {
                        if (showWhen == 2 && current != max || showWhen <= 1)
                        {
                            if (mobile.HitsPercentage != 0)
                            {
                                animations.GetAnimationDimensions(
                                    mobile.AnimIndex,
                                    mobile.GetGraphicForAnimation(),
                                    /*(byte) m.GetDirectionForAnimation()*/
                                    0,
                                    /*Mobile.GetGroupForAnimation(m, isParent:true)*/
                                    0,
                                    mobile.IsMounted,
                                    /*(byte) m.AnimIndex*/
                                    0,
                                    out int centerX,
                                    out int centerY,
                                    out int width,
                                    out int height
                                );

                                Point p1 = p;
                                p1.Y -= height + centerY + 8 + 22;

                                if (mobile.IsGargoyle && mobile.IsFlying)
                                {
                                    p1.Y -= 22;
                                }
                                else if (!mobile.IsMounted)
                                {
                                    p1.Y += 22;
                                }

                                p1 = Client.Game.Scene.Camera.WorldToScreen(p1, true);
                                p1.X -= (mobile.HitsTexture.Width >> 1) + 5;
                                p1.Y -= mobile.HitsTexture.Height;

                                if (mobile.ObjectHandlesStatus == ObjectHandlesStatus.DISPLAYING)
                                {
                                    int ohHeight = Constants.OBJECT_HANDLES_GUMP_HEIGHT
                                        + (ProfileManager.CurrentProfile.NameOverheadShowHpBar
                                            ? Constants.OBJECT_HANDLES_HP_BAR_HEIGHT + 1 : 0);
                                    p1.Y -= ohHeight + 5;
                                    offsetY += ohHeight + 5;
                                }

                                if (
                                    !(
                                        p1.X < 0
                                        || p1.X > camera.Bounds.Width - mobile.HitsTexture.Width
                                        || p1.Y < 0
                                        || p1.Y > camera.Bounds.Height
                                    )
                                )
                                {
                                    mobile.HitsTexture.Draw(batcher, p1.X, p1.Y, layerDepth);
                                }

                                if (newTargSystem)
                                {
                                    offsetY += mobile.HitsTexture.Height;
                                }
                            }
                        }
                    }
                }

                p.X -= 5;
                p = Client.Game.Scene.Camera.WorldToScreen(p, true);

                p.X -= BAR_WIDTH_HALF;
                p.Y -= BAR_HEIGHT_HALF;

                if (p.X < 0 || p.X > camera.Bounds.Width - BAR_WIDTH)
                {
                    continue;
                }

                if (p.Y < 0 || p.Y > camera.Bounds.Height - BAR_HEIGHT)
                {
                    continue;
                }

                if ((isEnabled && mode >= 1) || newTargSystem || forceDraw)
                {
                    var prof = ProfileManager.CurrentProfile;

                    if (prof != null && prof.UseOldHealthBars)
                    {
                        DrawOldHealthLine(
                            batcher,
                            mobile,
                            p.X,
                            p.Y + 4,
                            passive && !newTargSystem,
                            layerDepth
                        );
                    }
                    else
                    {
                        DrawHealthLine(batcher, mobile, p.X, p.Y, offsetY, passive, newTargSystem, layerDepth);
                    }
                }
            }
        }

        private static (Color hpcolor, int hpw, int manaw, int stamw) CalcUnderlines(
            Mobile mobile,
            int barWidth,
            float alphaMod
        )
        {
            Color hpcolor;

            if (mobile.IsParalyzed)
            {
                hpcolor = Color.AliceBlue;
            }
            else if (mobile.IsYellowHits)
            {
                hpcolor = Color.Orange;
            }
            else if (mobile.IsPoisoned)
            {
                hpcolor = Color.LimeGreen;
            }
            else
            {
                hpcolor = Color.CornflowerBlue;
            }

            hpcolor *= alphaMod;

            int currenthp = mobile.Hits;
            int maxhp = mobile.HitsMax;
            int currentmana = mobile.Mana;
            int maxmana = mobile.ManaMax;
            int currentstam = mobile.Stamina;
            int maxstam = mobile.StaminaMax;

            int hpw = 0;
            int manaw = 0;
            int stamw = 0;

            if (maxhp > 0)
            {
                hpw = currenthp * 100 / maxhp;
                hpw = hpw > 100 ? 100 : hpw;
                if (hpw > 1)
                {
                    hpw = barWidth * hpw / 100;
                }
            }

            if (maxmana > 0)
            {
                manaw = currentmana * 100 / maxmana;
                manaw = manaw > 100 ? 100 : manaw;
                if (manaw > 1)
                {
                    manaw = barWidth * manaw / 100;
                }
            }

            if (maxstam > 0)
            {
                stamw = currentstam * 100 / maxstam;
                stamw = stamw > 100 ? 100 : stamw;
                if (stamw > 1)
                {
                    stamw = barWidth * stamw / 100;
                }
            }

            return (hpcolor, hpw, manaw, stamw);
        }

        private void DrawOldHealthLine(
            UltimaBatcher2D batcher,
            Mobile mobile,
            int x,
            int y,
            bool passive,
            float layerDepth
        )
        {
            if (mobile == null)
            {
                return;
            }

            var prof = ProfileManager.CurrentProfile;
            float alphaMod = prof != null
                ? Math.Clamp(prof.MultipleUnderlinesSelfPartyTransparency / 10f, 0.1f, 1f)
                : 1f;

            Vector3 hueVec = ShaderHueTranslator.GetHueVector(0, false, 1f);

            bool multi =
                prof != null
                && prof.MultipleUnderlinesSelfParty
                && (mobile == _world.Player || _world.Party.Contains(mobile.Serial));

            if (multi)
            {
                int bigW;
                int bigH;
                int bigHalf;
                int ySpacing = 1;

                if (prof.MultipleUnderlinesSelfPartyBigBars)
                {
                    bigW = 50;
                    bigH = 4;
                    bigHalf = bigW / 2 - 17;
                }
                else
                {
                    bigW = 34;
                    bigH = 3;
                    bigHalf = bigW / 2 - 17;
                }

                Texture2D edgeH = SolidColorTextureCache.GetTexture(Color.Black * alphaMod);
                Texture2D backH = SolidColorTextureCache.GetTexture(Color.Red * alphaMod);
                Texture2D edgeM = SolidColorTextureCache.GetTexture(Color.Black * alphaMod);
                Texture2D backM = SolidColorTextureCache.GetTexture(Color.Red * alphaMod);
                Texture2D edgeS = SolidColorTextureCache.GetTexture(Color.Black * alphaMod);
                Texture2D backS = SolidColorTextureCache.GetTexture(Color.Red * alphaMod);

                (Color hpcolor, int maxhp, int maxmana, int maxstam) = CalcUnderlines(mobile, bigW, alphaMod);
                Color manaColor = Color.CornflowerBlue * alphaMod;

                batcher.Draw(
                    edgeH,
                    new Rectangle(x - 1 - bigHalf, y - 1, bigW + 2, bigH + 1),
                    hueVec,
                    layerDepth
                );
                batcher.Draw(
                    backH,
                    new Rectangle(x - bigHalf + maxhp, y, bigW - maxhp, bigH),
                    hueVec,
                    layerDepth
                );
                batcher.Draw(
                    SolidColorTextureCache.GetTexture(hpcolor),
                    new Rectangle(x - bigHalf, y, maxhp, bigH),
                    hueVec,
                    layerDepth
                );

                batcher.Draw(
                    edgeM,
                    new Rectangle(x - 1 - bigHalf, y + bigH + ySpacing - 1, bigW + 2, bigH + 1),
                    hueVec,
                    layerDepth
                );
                batcher.Draw(
                    backM,
                    new Rectangle(x - bigHalf + maxmana, y + bigH + ySpacing, bigW - maxmana, bigH),
                    hueVec,
                    layerDepth
                );
                batcher.Draw(
                    SolidColorTextureCache.GetTexture(manaColor),
                    new Rectangle(x - bigHalf, y + bigH + ySpacing, maxmana, bigH),
                    hueVec,
                    layerDepth
                );

                batcher.Draw(
                    edgeS,
                    new Rectangle(
                        x - 1 - bigHalf,
                        y + bigH + bigH + ySpacing + ySpacing - 1,
                        bigW + 2,
                        bigH + 2
                    ),
                    hueVec,
                    layerDepth
                );
                batcher.Draw(
                    backS,
                    new Rectangle(
                        x - bigHalf + maxstam,
                        y + bigH + bigH + ySpacing + ySpacing,
                        bigW - maxstam,
                        bigH
                    ),
                    hueVec,
                    layerDepth
                );
                batcher.Draw(
                    SolidColorTextureCache.GetTexture(Color.Goldenrod * alphaMod), // stamina (Dust-Light reused the mana colour here)
                    new Rectangle(x - bigHalf, y + bigH + bigH + ySpacing + ySpacing, maxstam, bigH),
                    hueVec,
                    layerDepth
                );

                return;
            }

            batcher.Draw(
                _oldEdge,
                new Rectangle(x - 1, y - 1, BAR_WIDTH + 2, OLD_BAR_HEIGHT + 2),
                hueVec,
                layerDepth
            );
            batcher.Draw(
                _oldBack,
                new Rectangle(x, y, BAR_WIDTH, OLD_BAR_HEIGHT),
                hueVec,
                layerDepth
            );

            Color color;

            if (mobile.IsParalyzed)
            {
                color = Color.AliceBlue;
            }
            else if (mobile.IsYellowHits)
            {
                color = Color.Orange;
            }
            else if (mobile.IsPoisoned)
            {
                color = Color.LimeGreen;
            }
            else
            {
                color = Color.CornflowerBlue;
            }

            int per = BAR_WIDTH * mobile.HitsPercentage / 100;

            batcher.Draw(
                SolidColorTextureCache.GetTexture(color),
                new Rectangle(x, y, per, OLD_BAR_HEIGHT),
                hueVec,
                layerDepth
            );
        }

        private void DrawHealthLine(
            UltimaBatcher2D batcher,
            Entity entity,
            int x,
            int y,
            int offsetY,
            bool passive,
            bool newTargetSystem,
            float layerDepth
        )
        {
            if (entity == null)
            {
                return;
            }

            int per = BAR_WIDTH * entity.HitsPercentage / 100;

            Mobile mobile = entity as Mobile;

            float alpha = passive && !newTargetSystem ? 0.5f : 1.0f;
            ushort hue =
                mobile != null
                    ? Notoriety.GetHue(mobile.NotorietyFlag)
                    : Notoriety.GetHue(NotorietyFlag.Gray);

            //Vector3 hueVec = ShaderHueTranslator.GetHueVector(hue, false, alpha);
            Vector3 hueVecZero = ShaderHueTranslator.GetHueVector(0, false, alpha);
            Vector3 hueVecNoto = ShaderHueTranslator.GetHueVector(hue, false, alpha);

            if (mobile == null)
            {
                y += 22;
            }

            const int MULTIPLER = 1;

            if (newTargetSystem && mobile != null && mobile.Serial != _world.Player.Serial)
            {
                Client.Game.UO.Animations.GetAnimationDimensions(
                    mobile.AnimIndex,
                    mobile.GetGraphicForAnimation(),
                    (byte) mobile.GetDirectionForAnimation(),
                    Mobile.GetGroupForAnimation(mobile, isParent: true),
                    mobile.IsMounted,
                    0, //mobile.AnimIndex,
                    out int centerX,
                    out int centerY,
                    out int width,
                    out int height
                );

                uint topGump;
                uint bottomGump;
                //uint gumpHue = 0x7570;
                uint gumpHue = 0x7572; // gray

                if (mobile != null)
                {
                    if (mobile.NotorietyFlag == NotorietyFlag.Innocent)
                        gumpHue = 0x7570; // blue

                    else if (mobile.NotorietyFlag == NotorietyFlag.Ally)
                        gumpHue = 0x7571; // green

                    else if (mobile.NotorietyFlag == NotorietyFlag.Criminal || mobile.NotorietyFlag == NotorietyFlag.Gray)
                        gumpHue = 0x7572; // grey

                    else if (mobile.NotorietyFlag == NotorietyFlag.Enemy)
                        gumpHue = 0x7573; // orange

                    if (mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
                        gumpHue = 0x7575; // yellow

                    else if (mobile.NotorietyFlag == NotorietyFlag.Murderer)
                        gumpHue = 0x7577; // red
                }

                if (width >= 80)
                {
                    topGump = 0x756D;
                    bottomGump = 0x756A;
                }
                else if (width >= 40)
                {
                    topGump = 0x756E;
                    bottomGump = 0x756B;
                }
                else
                {
                    topGump = 0x756F;
                    bottomGump = 0x756C;
                }

                ref readonly var hueGumpInfo = ref Client.Game.UO.Gumps.GetGump(gumpHue);
                var targetX = x + BAR_WIDTH_HALF - hueGumpInfo.UV.Width / 2f;
                var topTargetY = height + centerY + 8 + 22 + offsetY;

                ref readonly var newTargGumpInfo = ref Client.Game.UO.Gumps.GetGump(topGump);
                if (newTargGumpInfo.Texture != null)
                    batcher.Draw(
                        newTargGumpInfo.Texture,
                        new Vector2(targetX, y - topTargetY),
                        newTargGumpInfo.UV,
                        hueVecZero,
                        layerDepth
                    );

                if (hueGumpInfo.Texture != null)
                    batcher.Draw(
                        hueGumpInfo.Texture,
                        new Vector2(targetX, y - topTargetY),
                        hueGumpInfo.UV,
                        hueVecZero,
                        layerDepth
                    );

                y += 7 + newTargGumpInfo.UV.Height / 2 - centerY;

                newTargGumpInfo = ref Client.Game.UO.Gumps.GetGump(bottomGump);
                if (newTargGumpInfo.Texture != null)
                    batcher.Draw(
                        newTargGumpInfo.Texture,
                        new Vector2(targetX, y - 1 - newTargGumpInfo.UV.Height / 2f),
                        newTargGumpInfo.UV,
                        hueVecZero,
                        layerDepth
                    );
            }


            ref readonly var gumpInfo = ref Client.Game.UO.Gumps.GetGump(BACKGROUND_GRAPHIC);

            batcher.Draw(
                gumpInfo.Texture,
                new Rectangle(x, y, gumpInfo.UV.Width * MULTIPLER, gumpInfo.UV.Height * MULTIPLER),
                gumpInfo.UV,
                hueVecNoto,
                layerDepth
            );

            hueVecNoto.X = 0x21;

            if (entity.Hits != entity.HitsMax || entity.HitsMax == 0)
            {
                int offset = 2;

                if (per >> 2 == 0)
                {
                    offset = per;
                }

                gumpInfo = ref Client.Game.UO.Gumps.GetGump(HP_GRAPHIC);

                batcher.DrawTiled(
                    gumpInfo.Texture,
                    new Rectangle(
                        x + per * MULTIPLER - offset,
                        y,
                        (BAR_WIDTH - per) * MULTIPLER - offset / 2,
                        gumpInfo.UV.Height * MULTIPLER
                    ),
                    gumpInfo.UV,
                    hueVecNoto,
                    layerDepth
                );
            }

            hue = 90;

            if (per > 0)
            {
                if (mobile != null)
                {
                    if (mobile.IsPoisoned)
                    {
                        hue = 63;
                    }
                    else if (mobile.IsYellowHits)
                    {
                        hue = 53;
                    }
                }

                hueVecNoto.X = hue;

                gumpInfo = ref Client.Game.UO.Gumps.GetGump(HP_GRAPHIC);
                batcher.DrawTiled(
                    gumpInfo.Texture,
                    new Rectangle(x, y, per * MULTIPLER, gumpInfo.UV.Height * MULTIPLER),
                    gumpInfo.UV,
                    hueVecNoto,
                    layerDepth
                );
            }
        }
    }
}

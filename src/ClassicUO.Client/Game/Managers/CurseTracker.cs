// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game;

namespace ClassicUO.Game.Managers
{
    internal enum TrackedCurseKind
    {
        CorpseSkin,
        Curse,
        HitCurse
    }

    internal readonly struct TrackedCurseSnapshot
    {
        public TrackedCurseSnapshot(TrackedCurseKind kind, uint remaining, uint duration)
        {
            Kind = kind;
            Remaining = remaining;
            Duration = duration;
        }

        public TrackedCurseKind Kind { get; }
        public uint Remaining { get; }
        public uint Duration { get; }
    }

    internal static class CurseTracker
    {
        private const int CurseSpellIndex = 27;
        private const ushort CurseRemovalGraphic = 0x3709;
        private const ushort CorpseSkinGraphic = 0x373A;
        private const ushort CorpseSkinHue = 67;
        private const ushort CurseEffectGraphic = 0x374A;
        private const ushort CurseSpellSound = 0x01E1;
        private const ushort HitCurseSound = 0x01EA;
        private const float AssumedTargetResist = 110.1f;
        private const float AssumedSpiritSpeak = 80.0f;
        private const int AssumedEvalIntFixed = 1200;
        private const uint HitCurseDurationMs = 30_000;
        private const uint PendingCurseEffectWindowMs = 1_250;

        private static readonly Dictionary<uint, CurseState> _states = new Dictionary<uint, CurseState>();
        private static readonly PendingCurseEffect[] _pendingCurseEffects = new PendingCurseEffect[6];
        private static int _pendingCurseEffectCursor;

        public static void NotifyTargetSelected(World world, uint targetSerial)
        {
            if (!IsEnabled(world) || !SerialHelper.IsMobile(targetSerial) || targetSerial == world.Player.Serial)
            {
                return;
            }

            Entity entity = world.Get(targetSerial);

            if (entity is not Mobile || entity.IsDestroyed)
            {
                return;
            }

            switch (GameActions.LastSpellIndex)
            {
                case CurseSpellIndex:
                    Start(targetSerial, TrackedCurseKind.Curse, GetCurseDuration());
                    break;
            }
        }

        public static void NotifyGraphicEffect(World world, uint source, uint target, ushort graphic, ushort hue, ushort srcX, ushort srcY, ushort targetX, ushort targetY)
        {
            if (!IsEnabled(world))
            {
                return;
            }

            if (graphic == CurseRemovalGraphic)
            {
                ClearIfMobile(world, source);
                ClearIfMobile(world, target);

                return;
            }

            if (graphic == CorpseSkinGraphic && hue == CorpseSkinHue)
            {
                StartCorpseSkinIfMobile(world, source);
                StartCorpseSkinIfMobile(world, target);
            }
            else if (graphic == CurseEffectGraphic)
            {
                uint serial = GetEffectMobileSerial(world, source, target);

                if (serial != 0)
                {
                    StorePendingCurseEffect(serial, srcX, srcY, targetX, targetY);
                }
            }
        }

        public static void NotifySoundEffect(World world, ushort sound, ushort x, ushort y)
        {
            if (!IsEnabled(world) || sound != CurseSpellSound && sound != HitCurseSound)
            {
                return;
            }

            if (TryConsumePendingCurseEffect(x, y, out uint serial))
            {
                Start(serial, sound == HitCurseSound ? TrackedCurseKind.HitCurse : TrackedCurseKind.Curse, sound == HitCurseSound ? HitCurseDurationMs : GetCurseDuration());
            }
        }

        public static void NotifyCliloc(World world, uint serial, string text)
        {
            if (!IsEnabled(world) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string normalized = text.ToLowerInvariant();

            if (normalized.Contains("hit curse") || normalized.Contains("cursed by your weapon"))
            {
                Start(serial, TrackedCurseKind.HitCurse, HitCurseDurationMs);
            }
        }

        public static bool TryGetSnapshots(uint serial, TrackedCurseSnapshot[] snapshots, out int count)
        {
            count = 0;

            if (!ProfileManager.CurrentProfile.TrackCurses || !_states.TryGetValue(serial, out CurseState state))
            {
                return false;
            }

            AddSnapshot(snapshots, ref count, TrackedCurseKind.CorpseSkin, state.CorpseSkin);
            AddSnapshot(snapshots, ref count, TrackedCurseKind.Curse, state.Curse);
            AddSnapshot(snapshots, ref count, TrackedCurseKind.HitCurse, state.HitCurse);

            if (count == 0)
            {
                _states.Remove(serial);
            }

            return count > 0;
        }

        private static void Start(uint serial, TrackedCurseKind kind, uint duration)
        {
            if (duration == 0)
            {
                return;
            }

            if (!_states.TryGetValue(serial, out CurseState state))
            {
                state = new CurseState();
                _states[serial] = state;
            }

            TrackedCurseTimer timer = new TrackedCurseTimer(duration, Time.Ticks + duration);

            switch (kind)
            {
                case TrackedCurseKind.CorpseSkin:
                    state.CorpseSkin = timer;
                    break;

                case TrackedCurseKind.Curse:
                    state.Curse = timer;
                    break;

                case TrackedCurseKind.HitCurse:
                    state.HitCurse = timer;
                    break;
            }
        }

        private static void StartCorpseSkinIfMobile(World world, uint serial)
        {
            if (serial == 0 || serial == world.Player.Serial || !SerialHelper.IsMobile(serial))
            {
                return;
            }

            Entity entity = world.Get(serial);

            if (entity is Mobile && !entity.IsDestroyed)
            {
                Start(serial, TrackedCurseKind.CorpseSkin, GetCorpseSkinDuration());
            }
        }

        private static uint GetEffectMobileSerial(World world, uint source, uint target)
        {
            if (IsTrackableMobile(world, source))
            {
                return source;
            }

            if (IsTrackableMobile(world, target))
            {
                return target;
            }

            return 0;
        }

        private static bool IsTrackableMobile(World world, uint serial)
        {
            if (serial == 0 || serial == world.Player.Serial || !SerialHelper.IsMobile(serial))
            {
                return false;
            }

            Entity entity = world.Get(serial);

            return entity is Mobile && !entity.IsDestroyed;
        }

        private static void StorePendingCurseEffect(uint serial, ushort srcX, ushort srcY, ushort targetX, ushort targetY)
        {
            _pendingCurseEffects[_pendingCurseEffectCursor] = new PendingCurseEffect(serial, srcX, srcY, targetX, targetY, Time.Ticks + PendingCurseEffectWindowMs);
            _pendingCurseEffectCursor = (_pendingCurseEffectCursor + 1) % _pendingCurseEffects.Length;
        }

        private static bool TryConsumePendingCurseEffect(ushort x, ushort y, out uint serial)
        {
            for (int i = 0; i < _pendingCurseEffects.Length; i++)
            {
                PendingCurseEffect pending = _pendingCurseEffects[i];

                if (!pending.IsActive || pending.ExpiresAt < Time.Ticks)
                {
                    continue;
                }

                if (pending.Matches(x, y))
                {
                    serial = pending.Serial;
                    _pendingCurseEffects[i] = default;

                    return true;
                }
            }

            serial = 0;

            return false;
        }

        private static bool IsEnabled(World world)
        {
            return world?.Player != null && ProfileManager.CurrentProfile.TrackCurses;
        }

        private static void ClearIfMobile(World world, uint serial)
        {
            if (serial != 0 && SerialHelper.IsMobile(serial) && serial != world.Player.Serial)
            {
                _states.Remove(serial);
            }
        }

        private static uint GetCorpseSkinDuration()
        {
            double seconds = ((AssumedSpiritSpeak - AssumedTargetResist) / 2.5d) + 40.0d;

            return (uint)Math.Max(1_000, seconds * 1_000);
        }

        private static uint GetCurseDuration()
        {
            int seconds = ((6 * AssumedEvalIntFixed) / 50) + 1;

            return (uint)Math.Max(1_000, seconds * 1_000);
        }

        private static void AddSnapshot(TrackedCurseSnapshot[] snapshots, ref int count, TrackedCurseKind kind, TrackedCurseTimer timer)
        {
            if (!timer.IsActive)
            {
                return;
            }

            uint remaining = timer.EndAt > Time.Ticks ? timer.EndAt - Time.Ticks : 0;

            if (remaining == 0)
            {
                return;
            }

            snapshots[count++] = new TrackedCurseSnapshot(kind, remaining, timer.Duration);
        }

        private sealed class CurseState
        {
            public TrackedCurseTimer CorpseSkin;
            public TrackedCurseTimer Curse;
            public TrackedCurseTimer HitCurse;
        }

        private readonly struct TrackedCurseTimer
        {
            public TrackedCurseTimer(uint duration, uint endAt)
            {
                Duration = duration;
                EndAt = endAt;
            }

            public uint Duration { get; }
            public uint EndAt { get; }
            public bool IsActive => Duration != 0 && EndAt > Time.Ticks;
        }

        private readonly struct PendingCurseEffect
        {
            public PendingCurseEffect(uint serial, ushort srcX, ushort srcY, ushort targetX, ushort targetY, uint expiresAt)
            {
                Serial = serial;
                SrcX = srcX;
                SrcY = srcY;
                TargetX = targetX;
                TargetY = targetY;
                ExpiresAt = expiresAt;
            }

            public uint Serial { get; }
            public ushort SrcX { get; }
            public ushort SrcY { get; }
            public ushort TargetX { get; }
            public ushort TargetY { get; }
            public uint ExpiresAt { get; }
            public bool IsActive => Serial != 0;

            public bool Matches(ushort x, ushort y)
            {
                return IsNear(SrcX, SrcY, x, y) || IsNear(TargetX, TargetY, x, y);
            }

            private static bool IsNear(ushort ax, ushort ay, ushort bx, ushort by)
            {
                return Math.Abs((int)ax - bx) <= 1 && Math.Abs((int)ay - by) <= 1;
            }
        }
    }
}

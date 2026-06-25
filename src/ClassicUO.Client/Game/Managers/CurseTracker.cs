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
        private const float AssumedTargetResist = 110.1f;
        private const float AssumedSpiritSpeak = 80.0f;
        private const int AssumedEvalIntFixed = 1200;
        private const uint HitCurseDurationMs = 10_000;

        private static readonly Dictionary<uint, CurseState> _states = new Dictionary<uint, CurseState>();

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

        public static void NotifyGraphicEffect(World world, uint source, uint target, ushort graphic, ushort hue)
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
    }
}

// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
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
        private const int CorpseSkinSpellIndex = 103;
        private const ushort CurseRemovalGraphic = 0x3709;
        private const ushort CorpseSkinGraphic = 0x373A;
        private const ushort CorpseSkinHue = 67;
        private const float AssumedTargetResist = 110.1f;
        private const uint HitCurseDurationMs = 10_000;
        private const uint PendingCorpseSkinWindowMs = 5_000;

        private static readonly Dictionary<uint, CurseState> _states = new Dictionary<uint, CurseState>();
        private static uint _pendingCorpseSkinTarget;
        private static uint _pendingCorpseSkinExpiresAt;

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
                case CorpseSkinSpellIndex:
                    _pendingCorpseSkinTarget = targetSerial;
                    _pendingCorpseSkinExpiresAt = Time.Ticks + PendingCorpseSkinWindowMs;
                    break;

                case CurseSpellIndex:
                    Start(targetSerial, TrackedCurseKind.Curse, GetCurseDuration(world));
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
                ConfirmCorpseSkinEffect(world, source);
                ConfirmCorpseSkinEffect(world, target);
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

        private static void ConfirmCorpseSkinEffect(World world, uint serial)
        {
            if (serial == 0 || serial != _pendingCorpseSkinTarget || Time.Ticks > _pendingCorpseSkinExpiresAt)
            {
                return;
            }

            Start(serial, TrackedCurseKind.CorpseSkin, GetCorpseSkinDuration(world));
            _pendingCorpseSkinTarget = 0;
            _pendingCorpseSkinExpiresAt = 0;
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

        private static uint GetCorpseSkinDuration(World world)
        {
            float spiritSpeak = GetSkillValue(world, SkillEntry.HardCodedName.SpiritSpeak);
            double seconds = ((spiritSpeak - AssumedTargetResist) / 2.5d) + 40.0d;

            return (uint)Math.Max(1_000, seconds * 1_000);
        }

        private static uint GetCurseDuration(World world)
        {
            int evalFixed = GetSkillFixed(world, SkillEntry.HardCodedName.EvaluateIntelligence);
            int seconds = ((6 * evalFixed) / 50) + 1;

            return (uint)Math.Max(1_000, seconds * 1_000);
        }

        private static float GetSkillValue(World world, SkillEntry.HardCodedName skill)
        {
            int index = (int)skill;

            if (world.Player?.Skills == null || index < 0 || index >= world.Player.Skills.Length)
            {
                return 0;
            }

            return world.Player.Skills[index].Value;
        }

        private static int GetSkillFixed(World world, SkillEntry.HardCodedName skill)
        {
            int index = (int)skill;

            if (world.Player?.Skills == null || index < 0 || index >= world.Player.Skills.Length)
            {
                return 0;
            }

            return world.Player.Skills[index].ValueFixed;
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

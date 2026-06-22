// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers
{
    internal static class MovementTimingManager
    {
        public static int GetTurnDelay()
        {
            return ProfileManager.CurrentProfile?.MovementTurnDelay ?? Constants.TURN_DELAY;
        }

        public static int GetTurnDelayFast()
        {
            return ProfileManager.CurrentProfile?.MovementTurnDelayFast ?? Constants.TURN_DELAY_FAST;
        }
    }
}

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers
{
    // Invisible (and transparent) houses are switched off while you're standing inside a house's footprint, so stairs,
    // upper floors and walls around you still draw. Worked out once per frame.
    internal static class InvisibleHousesState
    {
        private static bool _lastActive, _lastInside;
        private static int _lastX = -1, _lastY = -1, _lastHouseCount = -1;
        private static bool _inside;

        public static bool Active { get; private set; }

        // True while the player stands inside a house and the "off inside a house" option is on.
        public static bool PlayerInsideHouse { get; private set; }

        public static void Update(World world)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || world?.Player == null)
            {
                Active = false;
                PlayerInsideHouse = false;
                return;
            }

            bool active = profile.InvisibleHousesEnabled;
            bool inside = false;

            if ((profile.InvisibleHousesEnabled || profile.TransparentHousesEnabled) && profile.InvisibleHousesOffInsideHouse)
            {
                int houseCount = world.HouseManager.Houses.Count;

                if (world.Player.X != _lastX || world.Player.Y != _lastY || houseCount != _lastHouseCount)
                {
                    _lastX = world.Player.X;
                    _lastY = world.Player.Y;
                    _lastHouseCount = houseCount;
                    _inside = IsInsideAnyHouse(world);
                }

                inside = _inside;
            }

            PlayerInsideHouse = inside;
            active &= !inside;

            if (active != _lastActive || inside != _lastInside)
            {
                _lastActive = active;
                _lastInside = inside;

                // Same as the ToggleInvisibleHouses macro: rebuild meshed chunks.
                foreach (Map.Chunk chunk in world.Map.GetUsedChunks())
                {
                    if (chunk?.Mesh != null)
                    {
                        chunk.Mesh.IsDirty = true;
                    }
                }
            }

            Active = active;
        }

        // A house's footprint changes when it's (re)loaded; force a recheck.
        public static void Invalidate()
        {
            _lastHouseCount = -1;
        }

        private static bool IsInsideAnyHouse(World world)
        {
            int px = world.Player.X;
            int py = world.Player.Y;

            foreach (House house in world.HouseManager.Houses)
            {
                Item multi = world.Items.Get(house.Serial);

                if (multi == null || !multi.MultiInfo.HasValue)
                {
                    continue;
                }

                var info = multi.MultiInfo.Value;

                // Same bounds test as HouseManager.EntityIntoHouse (Width/Height hold max offsets).
                if (
                    px >= multi.X + info.X
                    && px <= multi.X + info.Width
                    && py >= multi.Y + info.Y
                    && py <= multi.Y + info.Height
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
}

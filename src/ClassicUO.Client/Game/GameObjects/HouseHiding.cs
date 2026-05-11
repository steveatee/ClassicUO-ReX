// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.GameObjects
{
    internal static class HouseHiding
    {
        private const uint CACHE_LIFETIME_MS = 100;

        private static readonly List<Rectangle> _houseFootprints = new();
        private static uint _nextRefreshTime;
        private static World _cachedWorld;

        public static bool ShouldHide(World world, GameObject obj)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (!profile.InvisibleHousesEnabled || world?.Player == null || obj is Mobile)
            {
                return false;
            }

            GameObject groundTile = world.Map.GetTile(obj.X, obj.Y);

            if (groundTile == null)
            {
                return false;
            }

            if (
                obj.Z - world.Player.Z <= profile.InvisibleHousesZ
                || obj.Z - groundTile.Z <= profile.DontRemoveHouseBelowZ
            )
            {
                return false;
            }

            if (obj is Item item && item.IsMulti)
            {
                return false;
            }

            if (obj is Multi multi && (multi.IsMovable || multi.IsHousePreview))
            {
                return false;
            }

            return IsInsideKnownHouse(world, obj);
        }

        private static bool IsInsideKnownHouse(World world, GameObject obj)
        {
            RefreshCacheIfNeeded(world);

            for (int i = 0; i < _houseFootprints.Count; i++)
            {
                Rectangle bounds = _houseFootprints[i];

                if (obj.X >= bounds.X && obj.X <= bounds.Width && obj.Y >= bounds.Y && obj.Y <= bounds.Height)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RefreshCacheIfNeeded(World world)
        {
            if (_cachedWorld == world && Time.Ticks < _nextRefreshTime)
            {
                return;
            }

            _cachedWorld = world;
            _nextRefreshTime = Time.Ticks + CACHE_LIFETIME_MS;
            _houseFootprints.Clear();

            foreach (House house in world.HouseManager.Houses)
            {
                Item foundation = world.Items.Get(house.Serial);

                if (foundation == null || foundation.IsDestroyed || !foundation.MultiInfo.HasValue)
                {
                    continue;
                }

                AddFootprint(foundation);
            }

            foreach (Item item in world.Items.Values)
            {
                if (!item.IsMulti || item.IsDestroyed || item.ItemData.IsMultiMovable || !item.MultiInfo.HasValue)
                {
                    continue;
                }

                AddFootprint(item);
            }
        }

        private static void AddFootprint(Item item)
        {
            Rectangle bounds = item.MultiInfo.Value;
            int minX = item.X + bounds.X;
            int maxX = item.X + bounds.Width;
            int minY = item.Y + bounds.Y;
            int maxY = item.Y + bounds.Height;

            for (int i = 0; i < _houseFootprints.Count; i++)
            {
                Rectangle existing = _houseFootprints[i];

                if (existing.X == minX && existing.Width == maxX && existing.Y == minY && existing.Height == maxY)
                {
                    return;
                }
            }

            _houseFootprints.Add(new Rectangle(minX, minY, maxX, maxY));
        }
    }
}

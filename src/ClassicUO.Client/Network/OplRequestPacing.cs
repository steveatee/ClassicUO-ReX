// SPDX-License-Identifier: BSD-2-Clause
// Item property (tooltip / OPL) request pacing, ported from Dust-Light 1.1.1.140 (UoDreamsNetworkOptimizer and
// its PacketHandlers changes). Stock ClassicUO asks the server for the properties of every item it announces,
// 15 per frame with no cooldown, which floods the connection in UO Dreams' item-built event areas.
//
//   * UO Dreams stone roof pieces (0x0577, used as flooring) send huge property lists: never ask, use a stub.
//   * Requests go out every 500 ms (at once if the item under the mouse is waiting), each item at most every 3 s.
//   * Properties of 12 KB or more are kept but never asked for again; lists stop after 48 lines.
//   * Items inside closed containers that aren't yours (closed corpses, chests) aren't asked about.
//   * The queue holds 32 low-priority requests (ground decoration, other containers). Unlike Dust-Light, your own
//     items, mobiles, corpses and items in open containers always get queued: corpse hiding and the potion /
//     apple timers need their properties.
// Option: Options > Custom > Display & Performance, on by default.

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;

namespace ClassicUO.Network
{
    internal static class OplRequestPacing
    {
        public const ushort STONE_ROOF_GRAPHIC = 0x0577;
        public const int OVERSIZED_BYTES = 12288;
        public const int MAX_LINES = 48;

        private const int MAX_LOW_PRIORITY_QUEUE = 32;
        private const uint SEND_INTERVAL_MS = 500;
        private const uint REQUEST_COOLDOWN_MS = 3000;

        private static uint _nextSend;
        private static readonly HashSet<uint> _oversized = new HashSet<uint>();
        private static readonly Dictionary<uint, uint> _cooldown = new Dictionary<uint, uint>();

        public static bool Enabled => ProfileManager.CurrentProfile?.PaceItemPropertyRequests ?? true;

        public static bool IsUoDreams(World world)
        {
            if (world == null)
            {
                return false;
            }

            if (world.ServerName != null && world.ServerName.IndexOf("uodreams", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string ip = Settings.GlobalSettings?.IP;

            return !string.IsNullOrEmpty(ip) && ip.IndexOf("uodreams", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsStoneRoof(World world, Item item) =>
            Enabled && item != null && item.Graphic == STONE_ROOF_GRAPHIC && IsUoDreams(world);

        public static void AddStub(World world, uint serial, Item item, uint revision)
        {
            _oversized.Add(serial);

            string name = item?.Name;

            if (string.IsNullOrEmpty(name) && item != null)
            {
                name = item.ItemData.Name;
            }

            if (string.IsNullOrEmpty(name))
            {
                name = "stone roof";
            }

            world.OPL.Add(serial, revision, name, string.Empty, 0, null);
        }

        public static void NotePacketSize(uint serial, int bytes, int lines)
        {
            if (Enabled && bytes >= OVERSIZED_BYTES && lines > 0)
            {
                _oversized.Add(serial);
            }
        }

        private static bool IsHovered(uint serial)
        {
            if (SelectedObject.Object is Entity entity && entity.Serial == serial)
            {
                return true;
            }

            return UIManager.MouseOverControl?.Tooltip is uint tooltipSerial && tooltipSerial == serial;
        }

        /// <summary>Adds the serial to the request queue, unless pacing says it shouldn't be asked for now.</summary>
        public static void Queue(World world, uint serial, List<uint> queue)
        {
            if (!Enabled || world == null)
            {
                if (!queue.Contains(serial))
                {
                    queue.Add(serial);
                }

                return;
            }

            bool hovered = IsHovered(serial);
            Item item = world.Items.Get(serial);

            if (IsStoneRoof(world, item))
            {
                if (!world.OPL.TryGetRevision(serial, out _))
                {
                    AddStub(world, serial, item, 1);
                }

                return;
            }

            if (_oversized.Contains(serial))
            {
                return;
            }

            int existing = queue.IndexOf(serial);

            if (existing >= 0)
            {
                if (hovered && existing > 0)
                {
                    queue.RemoveAt(existing);
                    queue.Insert(0, serial);
                }

                return;
            }

            if (!hovered && _cooldown.TryGetValue(serial, out uint nextAllowed) && Time.Ticks < nextAllowed)
            {
                return;
            }

            bool important = hovered || IsImportant(world, serial, item);

            if (!important)
            {
                if (queue.Count >= MAX_LOW_PRIORITY_QUEUE || IsInsideClosedForeignContainer(world, item))
                {
                    return;
                }
            }

            if (_cooldown.Count > 20000)
            {
                _cooldown.Clear();
            }

            _cooldown[serial] = Time.Ticks + REQUEST_COOLDOWN_MS;

            if (hovered)
            {
                queue.Insert(0, serial);
            }
            else
            {
                queue.Add(serial);
            }
        }

        public static bool CanSendNow(List<uint> queue)
        {
            if (!Enabled)
            {
                return true;
            }

            if (Time.Ticks >= _nextSend || queue.Count != 0 && IsHovered(queue[0]))
            {
                _nextSend = Time.Ticks + SEND_INTERVAL_MS;
                return true;
            }

            return false;
        }

        private static bool IsImportant(World world, uint serial, Item item)
        {
            if (SerialHelper.IsMobile(serial))
            {
                return true;
            }

            if (item == null)
            {
                return false;
            }

            if (item.IsCorpse)
            {
                return true;
            }

            if (item.OnGround)
            {
                return false;
            }

            if (world.Player != null && item.RootContainer == world.Player.Serial)
            {
                return true; // in your backpack or worn
            }

            return world.Items.Get(item.Container) is Item container && container.Opened;
        }

        private static bool IsInsideClosedForeignContainer(World world, Item item)
        {
            if (item == null || world.Player == null)
            {
                return false;
            }

            Item current = item;

            for (int i = 0; i < 32; i++)
            {
                if (!SerialHelper.IsValid(current.Container))
                {
                    return false;
                }

                Entity parent = world.Get(current.Container);

                if (parent is Mobile)
                {
                    return false;
                }

                if (parent is Item container)
                {
                    if (container.Serial == world.Player.FindItemByLayer(Layer.Backpack)?.Serial)
                    {
                        return false;
                    }

                    if (!container.Opened && (container.IsCorpse || container.ItemData.IsContainer))
                    {
                        return true;
                    }

                    current = container;
                    continue;
                }

                return false;
            }

            return false;
        }
    }
}

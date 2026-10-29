// SPDX-License-Identifier: BSD-2-Clause
// Ported from Dust765/TazUO reference - simplified for Dust765-light

using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace ClassicUO.Game.UI.Gumps
{
    internal class GridContainer : ResizableGump
    {
        #region CONSTANTS
        private const int X_SPACING = 1, Y_SPACING = 1;
        private const int TOP_BAR_HEIGHT = 20;
        #endregion

        #region private static vars
        private static int lastX = 100, lastY = 100, lastCorpseX = 100, lastCorpseY = 100;
        private static int gridItemSize { get { return (int)Math.Round(50 * (ProfileManager.CurrentProfile.GridContainersScale / 100f)); } }
        private static int borderWidth = 4;
        #endregion

        #region private readonly vars
        private readonly AlphaBlendControl background;
        private readonly Label containerNameLabel;
        private readonly StbTextBox searchBox;
        private readonly GumpPic openRegularGump, sortContents;
        private readonly StaticPic quickDropBackpack;
        private readonly GumpPicTiled backgroundTexture;
        private readonly NiceButton setLootBag;
        private readonly bool isCorpse = false;
        #endregion

        #region private vars
        private Item container { get { return World.Items.Get(LocalSerial); } }
        private int _lastGridCellSize = -1;
        private int lastWidth = GetWidth(), lastHeight = GetHeight();
        private bool quickLootThisContainer = false;
        private bool? UseOldContainerStyle = null;
        private bool autoSortContainer = false;
        private bool firstItemsLoaded = false;
        private bool skipSave = false;
        private readonly ushort originalContainerItemGraphic;
        private ScrollArea scrollArea;
        private GridSlotManager gridSlotManager;
        #endregion

        #region private tooltip vars
        private string quickLootStatus { get { return ProfileManager.CurrentProfile.CorpseSingleClickLoot ? "<basefont color=\"green\">Enabled" : "<basefont color=\"red\">Disabled"; } }
        private string quickLootTooltip
        {
            get
            {
                if (isCorpse)
                    return $"Drop an item here to send it to your backpack.<br><br>Click to enable/disable single-click looting for corpses.<br>   Currently {quickLootStatus}";
                else
                    return $"Drop an item here to send it to your backpack.<br><br>Click to enable/disable single-click loot for this container.<br>   Currently " + (quickLootThisContainer ? "<basefont color=\"green\">Enabled" : "<basefont color=\"red\">Disabled");
            }
        }
        private string sortButtonTooltip
        {
            get
            {
                string status = autoSortContainer ? "<basefont color=\"green\">Enabled" : "<basefont color=\"red\">Disabled";
                return $"Sort this container.<br>Alt + Click to enable auto sort<br>Auto sort currently {status}";
            }
        }
        #endregion

        #region internal vars
        internal readonly bool IsPlayerBackpack = false;
        internal GridSlotManager GetGridSlotManager { get { return gridSlotManager; } }
        internal List<Item> GetContents { get { return gridSlotManager.ContainerContents; } }
        internal bool SkipSave { get { return skipSave; } set { skipSave = value; } }
        #endregion

        internal GridContainer(World world, uint local, ushort originalContainerGraphic, bool? useGridStyle = null)
            : base(world, GetWidth(), GetHeight(), GetWidth(2), GetHeight(1), local, 0)
        {
            if (container == null)
            {
                Dispose();
                return;
            }

            #region SET VARS
            isCorpse = container.IsCorpse || container.Graphic == 0x0009;
            if (useGridStyle != null)
                UseOldContainerStyle = !useGridStyle;

            IsPlayerBackpack = World.Player.FindItemByLayer(Layer.Backpack)?.Serial == LocalSerial;

            autoSortContainer = GridSaveSystem.Instance.AutoSortContainer(LocalSerial);

            Point lastPos = IsPlayerBackpack ? ProfileManager.CurrentProfile.BackpackGridPosition : GridSaveSystem.Instance.GetLastPosition(LocalSerial);
            Point savedSize = IsPlayerBackpack ? ProfileManager.CurrentProfile.BackpackGridSize : GridSaveSystem.Instance.GetLastSize(LocalSerial);
            lastWidth = Width = savedSize.X;
            lastHeight = Height = savedSize.Y;

            X = isCorpse ? lastCorpseX : lastX = lastPos.X;
            Y = isCorpse ? lastCorpseY : lastY = lastPos.Y;

            if (isCorpse)
            {
                World.Player.ManualOpenedCorpses.Remove(LocalSerial);

                if (World.Player.AutoOpenedCorpses.Contains(LocalSerial)
                    && ProfileManager.CurrentProfile != null
                    && ProfileManager.CurrentProfile.SkipEmptyCorpse
                    && container.IsEmpty)
                {
                    IsVisible = false;
                    Dispose();
                    return;
                }
            }

            originalContainerItemGraphic = originalContainerGraphic;

            CanMove = true;
            AcceptMouseInput = true;
            #endregion

            #region background
            background = new AlphaBlendControl()
            {
                Width = Width - (borderWidth * 2),
                Height = Height - (borderWidth * 2),
                X = borderWidth,
                Y = borderWidth,
                Alpha = (float)ProfileManager.CurrentProfile.ContainerOpacity / 100,
                Hue = ProfileManager.CurrentProfile.Grid_UseContainerHue ? container.Hue : ProfileManager.CurrentProfile.AltGridContainerBackgroundHue
            };
            backgroundTexture = new GumpPicTiled(0);
            #endregion

            #region TOP BAR
            containerNameLabel = new Label(GetContainerName(), true, 0x0481, ishtml: true)
            {
                X = borderWidth,
                Y = -20
            };

            searchBox = new StbTextBox(0xFF, 20, 150, true, FontStyle.None, 0x0481)
            {
                X = borderWidth,
                Y = borderWidth,
                Multiline = false,
                Width = 150,
                Height = 20
            };
            searchBox.TextChanged += (sender, e) => { UpdateItems(); };

            var regularGumpIcon = Client.Game.UO.Gumps.GetGump(5839).Texture;
            openRegularGump = new GumpPic(background.Width - 25 - borderWidth, borderWidth, regularGumpIcon == null ? (ushort)1209 : (ushort)5839, 0);
            openRegularGump.MouseUp += (sender, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                {
                    UseOldContainerStyle = true;
                    OpenOldContainer(LocalSerial);
                }
            };
            openRegularGump.MouseEnter += (sender, e) => { openRegularGump.Graphic = regularGumpIcon == null ? (ushort)1210 : (ushort)5840; };
            openRegularGump.MouseExit += (sender, e) => { openRegularGump.Graphic = regularGumpIcon == null ? (ushort)1209 : (ushort)5839; };
            openRegularGump.SetTooltip("Open the original style container.\n\nCtrl+Click to lock item | Alt+Click quick move");

            Item backpack = World.Player.FindItemByLayer(Layer.Backpack);
            quickDropBackpack = new StaticPic(backpack?.DisplayedGraphic ?? 0x0E75, 0)
            {
                X = Width - openRegularGump.Width - 20 - borderWidth,
                Y = borderWidth,
                Width = 20,
                Height = 20
            };
            quickDropBackpack.MouseUp += (sender, e) =>
            {
                if (e.Button == MouseButtonType.Left && quickDropBackpack.MouseIsOver)
                {
                    if (Client.Game.UO.GameCursor.ItemHold.Enabled)
                    {
                        Item bp = World.Player.FindItemByLayer(Layer.Backpack);
                        if (bp != null)
                            GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, 0xFFFF, 0xFFFF, 0, bp.Serial);
                    }
                    else if (isCorpse)
                    {
                        ProfileManager.CurrentProfile.CorpseSingleClickLoot ^= true;
                        quickDropBackpack.SetTooltip(quickLootTooltip);
                    }
                    else
                    {
                        quickLootThisContainer ^= true;
                        quickDropBackpack.SetTooltip(quickLootTooltip);
                    }
                }
            };
            quickDropBackpack.MouseEnter += (sender, e) => { quickDropBackpack.Hue = 0x34; };
            quickDropBackpack.MouseExit += (sender, e) => { quickDropBackpack.Hue = 0; };
            quickDropBackpack.SetTooltip(quickLootTooltip);

            sortContents = new GumpPic(quickDropBackpack.X - 20, borderWidth, 1210, 0);
            sortContents.MouseUp += (sender, e) =>
            {
                if (e.Button == MouseButtonType.Left && Keyboard.Alt)
                {
                    autoSortContainer ^= true;
                    sortContents.SetTooltip(sortButtonTooltip);
                }
                UpdateItems(true);
            };
            sortContents.MouseEnter += (sender, e) => { sortContents.Graphic = 1209; };
            sortContents.MouseExit += (sender, e) => { sortContents.Graphic = 1210; };
            sortContents.SetTooltip(sortButtonTooltip);
            #endregion

            #region Scroll Area
            scrollArea = new ScrollArea(
                background.X,
                TOP_BAR_HEIGHT + background.Y,
                background.Width,
                background.Height - (containerNameLabel.Height + 1),
                true
            );
            scrollArea.MouseUp += ScrollArea_MouseUp;
            #endregion

            #region Set loot bag
            setLootBag = new NiceButton(0, Height - 20, 100, 20, ButtonAction.Default, "Set loot bag") { IsSelectable = false };
            setLootBag.IsVisible = isCorpse;
            setLootBag.SetTooltip("For double click looting only");
            setLootBag.MouseUp += (s, e) =>
            {
                GameActions.Print(World, ResGumps.TargetContainerToGrabItemsInto);
                World.TargetManager.SetTargeting(CursorTarget.SetGrabBag, 0, TargetType.Neutral);
            };
            #endregion

            #region Add controls
            Add(background);
            Add(backgroundTexture);
            Add(containerNameLabel);
            searchBox.Add(new AlphaBlendControl(0.5f)
            {
                Hue = 0x0481,
                Width = searchBox.Width,
                Height = searchBox.Height
            });
            Add(searchBox);
            Add(openRegularGump);
            Add(quickDropBackpack);
            Add(sortContents);
            Add(scrollArea);
            Add(setLootBag);
            #endregion

            AlwaysShowResizeHandle = true;
            ResizeCompleted += (s, e) => UpdateItems();

            gridSlotManager = new GridSlotManager(World, LocalSerial, this, scrollArea);

            if (GridSaveSystem.Instance.UseOriginalContainerGump(LocalSerial) && (UseOldContainerStyle == null || UseOldContainerStyle == true))
            {
                skipSave = true;
                OpenOldContainer(local);
                return;
            }

            BuildBorder();
            ResizeWindow(savedSize);
            OnResize();
            BringResizeButtonToFront();
        }

        public override GumpType GumpType => GumpType.GridContainer;

        public override void OnResize()
        {
            base.OnResize();

            if (IsDisposed || gridSlotManager == null || background == null || scrollArea == null)
            {
                return;
            }

            lastWidth = Width;
            lastHeight = Height;
            background.Width = Width - (borderWidth * 2);
            background.Height = Height - (borderWidth * 2);
            scrollArea.Width = background.Width;
            scrollArea.Height = background.Height - (containerNameLabel.Height + 1);
            sortContents.X = Width - openRegularGump.Width - 40 - borderWidth;
            quickDropBackpack.X = Width - openRegularGump.Width - 20 - borderWidth;
            openRegularGump.X = background.Width - 25 - borderWidth;
            setLootBag.Y = Height - 20;
            BuildBorder();
            UpdateItems();
        }

        private static int GetWidth(int columns = -1)
        {
            if (columns < 0)
                columns = ProfileManager.CurrentProfile.Grid_DefaultColumns;
            return (borderWidth * 2) + 15 + (gridItemSize * columns) + (X_SPACING * columns);
        }

        private static int GetHeight(int rows = -1)
        {
            if (rows < 0)
                rows = ProfileManager.CurrentProfile.Grid_DefaultRows;
            return TOP_BAR_HEIGHT + (borderWidth * 2) + ((gridItemSize + Y_SPACING) * rows);
        }

        public override void Save(XmlTextWriter writer)
        {
            base.Save(writer);

            if (!skipSave)
                GridSaveSystem.Instance.SaveContainer(LocalSerial, gridSlotManager.GridSlots, Width, Height, X, Y, UseOldContainerStyle, autoSortContainer);

            if (IsPlayerBackpack)
            {
                ProfileManager.CurrentProfile.BackpackGridPosition = Location;
                ProfileManager.CurrentProfile.BackpackGridSize = new Point(Width, Height);
            }

            writer.WriteAttributeString("ogContainer", originalContainerItemGraphic.ToString());
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);
            GameActions.DoubleClickQueued(LocalSerial);
        }

        private void ScrollArea_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtonType.Left && scrollArea.MouseIsOver)
            {
                if (Client.Game.UO.GameCursor.ItemHold.Enabled)
                    GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, 0xFFFF, 0xFFFF, 0, LocalSerial);
                else if (World.TargetManager.IsTargeting)
                    World.TargetManager.Target(LocalSerial);
            }
            else if (e.Button == MouseButtonType.Right)
            {
                InvokeMouseCloseGumpWithRClick();
            }
        }

        private void OpenOldContainer(uint serial)
        {
            UIManager.GetGump<ContainerGump>(serial)?.Dispose();

            ushort graphic = originalContainerItemGraphic;
            if (Client.Game.UO.Version >= Utility.ClientVersion.CV_706000 && ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.UseLargeContainerGumps)
            {
                switch (graphic)
                {
                    case 0x0048: if (Client.Game.UO.Gumps.GetGump(0x06E8).Texture != null) graphic = 0x06E8; break;
                    case 0x0049: if (Client.Game.UO.Gumps.GetGump(0x9CDF).Texture != null) graphic = 0x9CDF; break;
                    case 0x0051: if (Client.Game.UO.Gumps.GetGump(0x06E7).Texture != null) graphic = 0x06E7; break;
                    case 0x003E: if (Client.Game.UO.Gumps.GetGump(0x06E9).Texture != null) graphic = 0x06E9; break;
                    case 0x004D: if (Client.Game.UO.Gumps.GetGump(0x06EA).Texture != null) graphic = 0x06EA; break;
                    case 0x004E: if (Client.Game.UO.Gumps.GetGump(0x06E6).Texture != null) graphic = 0x06E6; break;
                    case 0x004F: if (Client.Game.UO.Gumps.GetGump(0x06E5).Texture != null) graphic = 0x06E5; break;
                    case 0x004A: if (Client.Game.UO.Gumps.GetGump(0x9CDD).Texture != null) graphic = 0x9CDD; break;
                    case 0x0044: if (Client.Game.UO.Gumps.GetGump(0x9CE3).Texture != null) graphic = 0x9CE3; break;
                }
            }

            World.ContainerManager.CalculateContainerPosition(serial, graphic);

            UIManager.Add(new ContainerGump(World, container.Serial, graphic, true)
            {
                X = World.ContainerManager.X,
                Y = World.ContainerManager.Y,
                InvalidateContents = true
            });
            Dispose();
        }

        private void UpdateItems(bool overrideSort = false)
        {
            if (container == null)
            {
                Dispose();
                return;
            }
            UpdateContainerName();

            if (autoSortContainer) overrideSort = true;

            string searchText = searchBox.Text?.Trim() ?? "";
            List<Item> sortedContents = (ProfileManager.CurrentProfile is null || ProfileManager.CurrentProfile.GridContainerSearchMode == 0)
                ? gridSlotManager.SearchResults(searchText)
                : GridSlotManager.GetItemsInContainer(container);
            gridSlotManager.RebuildContainer(sortedContents, searchText, overrideSort);

            InvalidateContents = false;
        }

        internal static bool FindContainer(uint serial, out GridContainer gridContainer) => (gridContainer = UIManager.GetGump<GridContainer>(serial)) != null;

        protected override void UpdateContents()
        {
            if (InvalidateContents && !IsDisposed && IsVisible)
                UpdateItems();
            firstItemsLoaded = true;
        }

        protected override void OnMouseExit(int x, int y)
        {
            if (isCorpse && container != null && container == SelectedObject.CorpseObject)
                SelectedObject.CorpseObject = null;
        }

        public override void Dispose()
        {
            if (isCorpse) { lastCorpseX = X; lastCorpseY = Y; }
            else { lastX = X; lastY = Y; }

            Item _c = container;
            if (_c != null)
            {
                if (_c == SelectedObject.CorpseObject)
                    SelectedObject.CorpseObject = null;

                Item bank = World.Player.FindItemByLayer(Layer.Bank);
                if (bank != null && (_c.Serial == bank.Serial || _c.Container == bank.Serial))
                {
                    for (LinkedObject i = _c.Items; i != null; i = i.Next)
                    {
                        Item child = (Item)i;
                        if (child.Container == _c)
                        {
                            UIManager.GetGump<GridContainer>(child)?.Dispose();
                            UIManager.GetGump<ContainerGump>(child)?.Dispose();
                        }
                    }
                }
            }

            if (gridSlotManager != null && !skipSave)
                if (gridSlotManager.ItemPositions.Count > 0 && !isCorpse)
                    GridSaveSystem.Instance.SaveContainer(LocalSerial, gridSlotManager.GridSlots, Width, Height, X, Y, UseOldContainerStyle, autoSortContainer);

            base.Dispose();
        }

        public override void Update()
        {
            base.Update();
            if (IsDisposed) return;

            if (container == null || container.IsDestroyed)
            {
                Dispose();
                return;
            }

            if (isCorpse && container.Distance > 3)
            {
                Dispose();
                return;
            }

            int cellNow = gridItemSize;
            if (_lastGridCellSize != cellNow)
            {
                _lastGridCellSize = cellNow;
                UpdateItems();
            }

        }

        private string GetContainerName()
        {
            if (container == null) return string.Empty;
            return World.OPL.TryGetNameAndData(container.Serial, out string name, out _) ? name : (container.Name ?? "Container");
        }

        private void UpdateContainerName()
        {
            if (containerNameLabel != null)
                containerNameLabel.Text = GetContainerName();
        }

        private void BuildBorder()
        {
            ShowBorder = !ProfileManager.CurrentProfile.Grid_HideBorder;
        }

        internal class GridSlotManager
        {
            private Dictionary<int, GridItem> gridSlots = new Dictionary<int, GridItem>();
            private Item container;
            internal List<Item> containerContents;
            private int amount = 125;
            private Control area;
            private Dictionary<int, uint> itemPositions = new Dictionary<int, uint>();
            private List<uint> itemLocks = new List<uint>();
            private World world;

            internal Dictionary<int, GridItem> GridSlots { get { return gridSlots; } }
            internal List<Item> ContainerContents { get { return containerContents; } }
            internal Dictionary<int, uint> ItemPositions { get { return itemPositions; } }

            internal GridSlotManager(World _world, uint thisContainer, GridContainer gridContainer, Control controlArea)
            {
                world = _world;
                area = controlArea;
                foreach (GridSaveSystem.GridItemSlotSaveData item in GridSaveSystem.Instance.GetItemSlots(thisContainer))
                {
                    ItemPositions.Add(item.Slot, item.Serial);
                    if (item.IsLocked)
                        itemLocks.Add(item.Serial);
                }
                container = world.Items.Get(thisContainer);
                UpdateItems();
                if (containerContents.Count > 125)
                    amount = containerContents.Count;

                for (int i = 0; i < amount; i++)
                {
                    GridItem GI = new GridItem(0, gridItemSize, container, gridContainer, i);
                    gridSlots.Add(i, GI);
                    area.Add(GI);
                }
            }

            internal void AddLockedItemSlot(uint serial, int specificSlot)
            {
                int removeSlot = -1;
                foreach (KeyValuePair<int, uint> kv in ItemPositions)
                {
                    if (kv.Value == serial) { removeSlot = kv.Key; break; }
                }
                if (removeSlot >= 0) ItemPositions.Remove(removeSlot);
                if (ItemPositions.ContainsKey(specificSlot)) ItemPositions.Remove(specificSlot);
                ItemPositions.Add(specificSlot, serial);
            }

            internal GridItem FindItem(uint serial)
            {
                foreach (var slot in gridSlots)
                    if (slot.Value.LocalSerial == serial)
                        return slot.Value;
                return null;
            }

            internal void RebuildContainer(List<Item> filteredItems, string searchText = "", bool overrideSort = false)
            {
                foreach (var slot in gridSlots)
                    slot.Value.SetGridItem(null);

                // Clean up stale itemPositions entries for items no longer in this container
                List<int> staleSlots = null;
                foreach (var spot in itemPositions)
                {
                    Item i = world.Items.Get(spot.Value);
                    if (i == null || i.Container != container.Serial)
                    {
                        staleSlots ??= new List<int>();
                        staleSlots.Add(spot.Key);
                    }
                }
                if (staleSlots != null)
                {
                    foreach (int s in staleSlots)
                    {
                        if (itemPositions.TryGetValue(s, out uint staleSerial))
                        {
                            itemLocks.Remove(staleSerial);
                            itemPositions.Remove(s);
                        }
                    }
                }

                foreach (var spot in itemPositions)
                {
                    Item i = world.Items.Get(spot.Value);
                    if (i != null && filteredItems.Contains(i) && (!overrideSort || itemLocks.Contains(spot.Value)))
                    {
                        if (spot.Key < gridSlots.Count)
                        {
                            gridSlots[spot.Key].SetGridItem(i);
                            if (itemLocks.Contains(spot.Value))
                                gridSlots[spot.Key].ItemGridLocked = true;
                            filteredItems.Remove(i);
                        }
                    }
                }

                foreach (Item i in filteredItems)
                {
                    foreach (var slot in gridSlots)
                    {
                        if (slot.Value.SlotItem != null) continue;
                        slot.Value.SetGridItem(i);
                        AddLockedItemSlot(i.Serial, slot.Key);
                        break;
                    }
                }

                bool hideMode = ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.GridContainerSearchMode == 0;
                bool highlightMode = ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.GridContainerSearchMode == 1;
                string searchTermLower = (searchText ?? "").Trim().ToLowerInvariant();
                foreach (var slot in gridSlots)
                {
                    slot.Value.IsVisible = !(!string.IsNullOrWhiteSpace(searchText) && hideMode);
                    if (slot.Value.SlotItem != null && !string.IsNullOrEmpty(searchTermLower))
                    {
                        if (SearchItemNameAndProps(searchTermLower, slot.Value.SlotItem))
                        {
                            slot.Value.Hightlight = highlightMode;
                            slot.Value.IsVisible = true;
                        }
                    }
                }
                SetGridPositions();
            }

            internal void SetLockedSlot(int slot, bool locked)
            {
                if (gridSlots[slot].SlotItem == null) return;
                gridSlots[slot].ItemGridLocked = locked;
                if (!locked) itemLocks.Remove(gridSlots[slot].SlotItem.Serial);
                else itemLocks.Add(gridSlots[slot].SlotItem.Serial);
            }

            internal void SetGridPositions()
            {
                int x = X_SPACING, y = 0;
                foreach (var slot in gridSlots)
                {
                    if (!slot.Value.IsVisible) continue;
                    if (x + gridItemSize >= area.Width - 14)
                    {
                        x = X_SPACING;
                        y += gridItemSize + Y_SPACING;
                    }
                    slot.Value.X = x;
                    slot.Value.Y = y;
                    slot.Value.Resize();
                    x += gridItemSize + X_SPACING;
                }
            }

            internal List<Item> SearchResults(string search)
            {
                UpdateItems();
                if (string.IsNullOrWhiteSpace(search)) return containerContents;
                if (ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.GridContainerSearchMode != 0)
                    return containerContents;
                List<Item> filteredContents = new List<Item>();
                string term = search.Trim().ToLowerInvariant();
                foreach (Item i in containerContents)
                {
                    if (SearchItemNameAndProps(term, i))
                        filteredContents.Add(i);
                }
                return filteredContents;
            }

            private bool SearchItemNameAndProps(string searchTermLower, Item item)
            {
                if (item == null || string.IsNullOrEmpty(searchTermLower)) return false;
                if (world.OPL.TryGetNameAndData(item.Serial, out string name, out string data))
                {
                    if (name != null && name.ToLowerInvariant().Contains(searchTermLower)) return true;
                    if (data != null && data.ToLowerInvariant().Contains(searchTermLower)) return true;
                }
                else
                {
                    if (item.Name != null && item.Name.ToLowerInvariant().Contains(searchTermLower)) return true;
                    if (item.ItemData.Name != null && item.ItemData.Name.ToLowerInvariant().Contains(searchTermLower)) return true;
                }
                return false;
            }

            internal void UpdateItems()
            {
                containerContents = GetItemsInContainer(container);
            }

            internal static List<Item> GetItemsInContainer(Item _container)
            {
                List<Item> contents = new List<Item>();
                for (LinkedObject i = _container.Items; i != null; i = i.Next)
                {
                    Item item = (Item)i;
                    var layer = (Layer)item.ItemData.Layer;
                    if (_container.IsCorpse && item.Layer > 0 && !Constants.BAD_CONTAINER_LAYERS[(int)layer]) continue;
                    if (item.ItemData.IsWearable && (layer == Layer.Face || layer == Layer.Beard || layer == Layer.Hair)) continue;
                    contents.Add(item);
                }
                contents.Sort(static (a, b) => { int g = a.Graphic.CompareTo(b.Graphic); return g != 0 ? g : a.Hue.CompareTo(b.Hue); });
                return contents;
            }
        }

        internal class GridItem : Control
        {
            private Item _item;
            private readonly HitBox hit;
            private Label count;
            private GridContainer gridContainer;
            private Item container;
            private int slot;
            private bool mousePressedWhenEntered = false;
            private GridContainerPreview preview;

            internal bool Hightlight = false;
            internal bool ItemGridLocked = false;

            internal Item SlotItem { get { return _item; } }

            internal GridItem(uint serial, int size, Item _container, GridContainer _gridContainer, int _slot)
            {
                container = _container;
                gridContainer = _gridContainer;
                slot = _slot;
                LocalSerial = serial;

                Width = size;
                Height = size;
                WantUpdateSize = false;
                AcceptMouseInput = true;
                CanMove = false;

                hit = new HitBox(0, 0, size, size, null, 0f);
                Add(hit);

                hit.MouseUp += _hit_MouseUp;
                hit.MouseDoubleClick += _hit_MouseDoubleClick;
                hit.MouseEnter += _hit_MouseEnter;
                hit.MouseExit += _hit_MouseExit;
            }

            internal void SetGridItem(Item item)
            {
                if (item == null)
                {
                    _item = null;
                    LocalSerial = 0;
                    hit.ClearTooltip();
                    Hightlight = false;
                    count?.Dispose();
                    count = null;
                    ItemGridLocked = false;
                }
                else
                {
                    _item = item;
                    LocalSerial = item.Serial;
                    int itemAmt = (_item.ItemData.IsStackable ? _item.Amount : 1);
                    if (itemAmt > 1)
                    {
                        count?.Dispose();
                        count = new Label(itemAmt.ToString(), true, 0x0481, align: TEXT_ALIGN_TYPE.TS_LEFT);
                        count.X = 1;
                        count.Y = Height - count.Height;
                    }
                    else
                    {
                        count?.Dispose();
                        count = null;
                    }
                    hit.SetTooltip(_item);
                }
            }

            internal void Resize()
            {
                int size = gridItemSize;
                Width = size; Height = size;
                hit.Width = size; hit.Height = size;
                if (count != null) count.Y = Height - count.Height;
            }

            private void _hit_MouseDoubleClick(object sender, MouseDoubleClickEventArgs e)
            {
                if (e.Button != MouseButtonType.Left || gridContainer.World.TargetManager.IsTargeting || _item == null)
                    return;

                if (!Keyboard.Ctrl && ProfileManager.CurrentProfile.DoubleClickToLootInsideContainers && gridContainer.isCorpse
                    && !_item.IsDestroyed && !_item.ItemData.IsContainer
                    && container != gridContainer.World.Player.FindItemByLayer(Layer.Backpack)
                    && !_item.IsLocked && _item.IsLootable)
                {
                    GameActions.GrabItem(gridContainer.World, _item.Serial, _item.Amount);
                }
                else
                {
                    GameActions.DoubleClick(gridContainer.World, LocalSerial);
                }
                e.Result = true;
            }

            private void _hit_MouseUp(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtonType.Left)
                {
                    if (Client.Game.UO.GameCursor.ItemHold.Enabled)
                    {
                        if (_item != null && _item.ItemData.IsContainer)
                        {
                            GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, 0xFFFF, 0xFFFF, 0, _item.Serial);
                            Mouse.CancelDoubleClick = true;
                        }
                        else if (_item != null && _item.ItemData.IsStackable && _item.Graphic == Client.Game.UO.GameCursor.ItemHold.Graphic)
                        {
                            GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, _item.X, _item.Y, 0, _item.Serial);
                            Mouse.CancelDoubleClick = true;
                        }
                        else
                        {
                            Rectangle containerBounds = gridContainer.World.ContainerManager.Get(container.Graphic).Bounds;
                            gridContainer.gridSlotManager.AddLockedItemSlot(Client.Game.UO.GameCursor.ItemHold.Serial, slot);
                            GameActions.DropItem(Client.Game.UO.GameCursor.ItemHold.Serial, containerBounds.Width / 2, containerBounds.Height / 2, 0, container.Serial);
                            Mouse.CancelDoubleClick = true;
                        }
                    }
                    else if (gridContainer.World.TargetManager.IsTargeting)
                    {
                        if (_item != null)
                        {
                            gridContainer.World.TargetManager.Target(_item.Serial);
                            if (gridContainer.World.TargetManager.TargetingState == CursorTarget.SetTargetClientSide)
                                UIManager.Add(new InspectorGump(gridContainer.World, _item));
                        }
                        else
                            gridContainer.World.TargetManager.Target(container.Serial);
                        Mouse.CancelDoubleClick = true;
                    }
                    else if (Keyboard.Ctrl)
                    {
                        gridContainer.gridSlotManager.SetLockedSlot(slot, !ItemGridLocked);
                        Mouse.CancelDoubleClick = true;
                    }
                    else if (_item != null)
                    {
                        Point offset = Mouse.LDragOffset;
                        if (Math.Abs(offset.X) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS && Math.Abs(offset.Y) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS)
                        {
                            if ((gridContainer.isCorpse && ProfileManager.CurrentProfile.CorpseSingleClickLoot) || gridContainer.quickLootThisContainer)
                            {
                                GameActions.GrabItem(gridContainer.World, _item.Serial, _item.Amount);
                                Mouse.CancelDoubleClick = true;
                            }
                            else
                            {
                                if (gridContainer.World.ClientFeatures.TooltipsEnabled)
                                    gridContainer.World.DelayedObjectClickManager.Set(_item.Serial, gridContainer.X, gridContainer.Y - 80, Time.Ticks + Mouse.MOUSE_DELAY_DOUBLE_CLICK);
                                else
                                    GameActions.SingleClick(gridContainer.World, _item.Serial);
                            }
                        }
                    }
                }
            }

            private void _hit_MouseExit(object sender, MouseEventArgs e)
            {
                if (Mouse.LButtonPressed && !mousePressedWhenEntered)
                {
                    Point offset = Mouse.LDragOffset;
                    if (Math.Abs(offset.X) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS || Math.Abs(offset.Y) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS)
                    {
                        if (_item != null)
                        {
                            if (!Keyboard.Alt)
                                GameActions.PickUp(gridContainer.World, _item.Serial, e.X, e.Y);
                        }
                        else
                        {
                            if (ProfileManager.CurrentProfile.HoldAltToMoveGumps)
                            {
                                if (Keyboard.Alt)
                                    UIManager.AttemptDragControl(gridContainer);
                            }
                            else
                                UIManager.AttemptDragControl(gridContainer);
                        }
                    }
                }

                GridContainerPreview g;
                while ((g = UIManager.GetGump<GridContainerPreview>()) != null)
                    g.Dispose();
            }

            private void _hit_MouseEnter(object sender, MouseEventArgs e)
            {
                SelectedObject.Object = gridContainer.World.Get(LocalSerial);
                mousePressedWhenEntered = Mouse.LButtonPressed;

                if (_item != null)
                {
                    if (_item.ItemData.IsContainer && _item.Items != null && ProfileManager.CurrentProfile.GridEnableContPreview)
                    {
                        preview = new GridContainerPreview(gridContainer.World, _item.Serial, Mouse.Position.X, Mouse.Position.Y);
                        UIManager.Add(preview);
                    }

                    if (!hit.HasTooltip)
                        hit.SetTooltip(_item);
                }
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
                float layerDepth = layerDepthRef;

                if (_item != null)
                {
                    ref readonly var artInfo = ref Client.Game.UO.Arts.GetArt(_item.DisplayedGraphic);
                    var texture = artInfo.Texture;

                    if (texture != null)
                    {
                        var artRect = Client.Game.UO.Arts.GetRealArtBounds(_item.DisplayedGraphic);
                        var sourceRectangle = artInfo.UV;
                        float scale = ProfileManager.CurrentProfile.GridContainersScale / 100f;

                        Point originalSize = new Point(hit.Width, hit.Height);
                        Point point = new Point();

                        if (artRect.Width < hit.Width)
                        {
                            originalSize.X = ProfileManager.CurrentProfile.GridContainerScaleItems ? (int)(artRect.Width * scale) : artRect.Width;
                            point.X = (hit.Width >> 1) - (originalSize.X >> 1);
                        }
                        else if (artRect.Width > hit.Width)
                        {
                            originalSize.X = ProfileManager.CurrentProfile.GridContainerScaleItems ? (int)(hit.Width * scale) : hit.Width;
                            point.X = (hit.Width >> 1) - (originalSize.X >> 1);
                        }

                        if (artRect.Height < hit.Height)
                        {
                            originalSize.Y = ProfileManager.CurrentProfile.GridContainerScaleItems ? (int)(artRect.Height * scale) : artRect.Height;
                            point.Y = (hit.Height >> 1) - (originalSize.Y >> 1);
                        }
                        else if (artRect.Height > hit.Height)
                        {
                            originalSize.Y = ProfileManager.CurrentProfile.GridContainerScaleItems ? (int)(hit.Height * scale) : hit.Height;
                            point.Y = (hit.Height >> 1) - (originalSize.Y >> 1);
                        }

                        Vector3 hueVector = ShaderHueTranslator.GetHueVector(_item.Hue, _item.ItemData.IsPartialHue, 1f);

                        renderLists.AddGumpWithAtlas(batcher =>
                        {
                            batcher.Draw(
                                texture,
                                new Rectangle(x + point.X, y + point.Y + hit.Y, originalSize.X, originalSize.Y),
                                new Rectangle(sourceRectangle.X + artRect.X, sourceRectangle.Y + artRect.Y, artRect.Width, artRect.Height),
                                hueVector,
                                layerDepth
                            );
                            return true;
                        });

                        if (count != null)
                            count.AddToRenderLists(renderLists, x + count.X, y + count.Y, ref layerDepthRef);
                    }
                }

                // Border and highlight
                ushort borderHue = ProfileManager.CurrentProfile.GridBorderHue;
                float borderAlpha = (float)ProfileManager.CurrentProfile.GridBorderAlpha / 100;
                if (ItemGridLocked) borderHue = 0x2;
                if (Hightlight) { borderHue = 0x34; borderAlpha = 1f; }

                Vector3 borderHueVector = ShaderHueTranslator.GetHueVector(borderHue, false, borderAlpha);

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    batcher.DrawRectangle(SolidColorTextureCache.GetTexture(Color.White), x, y, Width, Height, borderHueVector, layerDepth);

                    if (hit.MouseIsOver && _item != null)
                    {
                        Vector3 hov = ShaderHueTranslator.GetHueVector(borderHue, false, 0.3f);
                        batcher.Draw(SolidColorTextureCache.GetTexture(Color.White), new Rectangle(x + 1, y, Width - 1, Height), hov, layerDepth);
                    }
                    return true;
                });

                return true;
            }
        }

        private class GridContainerPreview : Gump
        {
            private readonly AlphaBlendControl _background;
            private readonly Item _container;

            private const int WIDTH = 170;
            private const int HEIGHT = 150;
            private const int GRIDSIZE = 50;

            internal GridContainerPreview(World world, uint serial, int x, int y) : base(world, serial, 0)
            {
                _container = world.Items.Get(serial);
                if (_container == null) { Dispose(); return; }

                X = x - WIDTH - 20;
                Y = y - HEIGHT - 20;
                _background = new AlphaBlendControl() { Width = WIDTH, Height = HEIGHT };

                CanCloseWithRightClick = true;
                Add(_background);
                InvalidateContents = true;
            }

            protected override void UpdateContents()
            {
                base.UpdateContents();
                if (InvalidateContents && !IsDisposed && IsVisible && _container?.Items != null)
                {
                    int currentCount = 0, lastX = 0, lastY = 0;
                    for (LinkedObject i = _container.Items; i != null; i = i.Next)
                    {
                        Item item = (Item)i;
                        if (item == null || currentCount > 8) break;

                        StaticPic gridItem = new StaticPic(item.DisplayedGraphic, item.Hue);
                        gridItem.X = lastX;
                        if (gridItem.X + GRIDSIZE > WIDTH) { gridItem.X = 0; lastX = 0; lastY += GRIDSIZE; }
                        lastX += GRIDSIZE;
                        gridItem.Y = lastY;
                        Add(gridItem);
                        currentCount++;
                    }
                }
            }

            public override void Update()
            {
                if (IsDisposed) return;
                if (_container == null || _container.IsDestroyed || (_container.OnGround && _container.Distance > 3)) { Dispose(); return; }
                base.Update();
            }
        }

        private class GridSaveSystem
        {
            private const long TIME_CUTOFF = ((60 * 60) * 24) * 60;
            private string gridSavePath = Path.Combine(ProfileManager.ProfilePath, "GridContainers.xml");
            private XDocument saveDocument;
            private XElement rootElement;
            private bool enabled = false;

            private static GridSaveSystem instance;
            internal static GridSaveSystem Instance
            {
                get
                {
                    if (instance == null) instance = new GridSaveSystem();
                    return instance;
                }
            }

            private GridSaveSystem()
            {
                if (!SaveFileCheck()) { enabled = false; return; }
                try { saveDocument = XDocument.Load(gridSavePath); }
                catch { saveDocument = new XDocument(); }

                rootElement = saveDocument.Element("grid_gumps");
                if (rootElement == null)
                {
                    saveDocument.Add(new XElement("grid_gumps"));
                    rootElement = saveDocument.Root;
                }
                enabled = true;
            }

            internal bool SaveContainer(uint serial, Dictionary<int, GridItem> gridSlots, int width, int height, int lx = 100, int ly = 100, bool? useOriginalContainer = false, bool autoSort = false)
            {
                if (!enabled) return false;
                if (useOriginalContainer == null) useOriginalContainer = false;

                XElement thisContainer = rootElement.Element("container_" + serial.ToString());
                if (thisContainer == null) { thisContainer = new XElement("container_" + serial.ToString()); rootElement.Add(thisContainer); }
                else thisContainer.RemoveNodes();

                thisContainer.SetAttributeValue("last_opened", DateTimeOffset.Now.ToUnixTimeSeconds().ToString());
                thisContainer.SetAttributeValue("width", width.ToString());
                thisContainer.SetAttributeValue("height", height.ToString());
                thisContainer.SetAttributeValue("lastX", lx.ToString());
                thisContainer.SetAttributeValue("lastY", ly.ToString());
                thisContainer.SetAttributeValue("useOriginalContainer", useOriginalContainer.ToString());
                thisContainer.SetAttributeValue("autoSort", autoSort.ToString());

                foreach (var slot in gridSlots)
                {
                    if (slot.Value.SlotItem == null) continue;
                    XElement item_slot = new XElement("item");
                    item_slot.SetAttributeValue("serial", slot.Value.SlotItem.Serial.ToString());
                    item_slot.SetAttributeValue("locked", slot.Value.ItemGridLocked.ToString());
                    item_slot.SetAttributeValue("slot", slot.Key.ToString());
                    thisContainer.Add(item_slot);
                }
                RemoveOldContainers();
                saveDocument.Save(gridSavePath);
                return true;
            }

            internal List<GridItemSlotSaveData> GetItemSlots(uint container)
            {
                List<GridItemSlotSaveData> items = new List<GridItemSlotSaveData>();
                if (!enabled) return items;

                XElement thisContainer = rootElement.Element("container_" + container.ToString());
                if (thisContainer != null)
                {
                    foreach (XElement itemSlot in thisContainer.Elements("item"))
                    {
                        XAttribute slot = itemSlot.Attribute("slot");
                        XAttribute serial = itemSlot.Attribute("serial");
                        XAttribute isLockedAttribute = itemSlot.Attribute("locked");
                        if (slot != null && serial != null)
                        {
                            if (int.TryParse(slot.Value, out int slotV) && uint.TryParse(serial.Value, out uint serialV))
                            {
                                bool isLocked = isLockedAttribute != null && bool.TryParse(isLockedAttribute.Value, out bool il) && il;
                                items.Add(new GridItemSlotSaveData(slotV, serialV, isLocked));
                            }
                        }
                    }
                }
                return items;
            }

            internal class GridItemSlotSaveData
            {
                internal readonly int Slot;
                internal readonly uint Serial;
                internal readonly bool IsLocked;
                internal GridItemSlotSaveData(int slot, uint serial, bool isLocked) { Slot = slot; Serial = serial; IsLocked = isLocked; }
            }

            internal Point GetLastSize(uint container)
            {
                Point lastSize = new Point(GetWidth(), GetHeight());
                if (!enabled) return lastSize;
                XElement thisContainer = rootElement.Element("container_" + container.ToString());
                if (thisContainer != null)
                {
                    XAttribute width = thisContainer.Attribute("width");
                    XAttribute height = thisContainer.Attribute("height");
                    if (width != null && height != null)
                    {
                        int.TryParse(width.Value, out lastSize.X);
                        int.TryParse(height.Value, out lastSize.Y);
                    }
                }
                return lastSize;
            }

            internal Point GetLastPosition(uint container)
            {
                Point LastPos = new Point(GridContainer.lastX, GridContainer.lastY);
                if (!enabled) return LastPos;
                XElement thisContainer = rootElement.Element("container_" + container.ToString());
                if (thisContainer != null)
                {
                    XAttribute lx = thisContainer.Attribute("lastX");
                    XAttribute ly = thisContainer.Attribute("lastY");
                    if (lx != null && ly != null)
                    {
                        int.TryParse(lx.Value, out LastPos.X);
                        int.TryParse(ly.Value, out LastPos.Y);
                    }
                }
                return LastPos;
            }

            internal bool UseOriginalContainerGump(uint container)
            {
                bool useOriginalContainer = false;
                if (!enabled) return useOriginalContainer;
                XElement thisContainer = rootElement.Element("container_" + container.ToString());
                if (thisContainer?.Attribute("useOriginalContainer") is XAttribute attr)
                    bool.TryParse(attr.Value, out useOriginalContainer);
                return useOriginalContainer;
            }

            internal bool AutoSortContainer(uint container)
            {
                bool autoSort = false;
                if (!enabled) return autoSort;
                XElement thisContainer = rootElement.Element("container_" + container.ToString());
                if (thisContainer?.Attribute("autoSort") is XAttribute attr)
                    bool.TryParse(attr.Value, out autoSort);
                return autoSort;
            }

            private void RemoveOldContainers()
            {
                long cutOffTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - TIME_CUTOFF;
                List<XElement> removeMe = new List<XElement>();
                foreach (XElement container in rootElement.Elements())
                {
                    if (container.Attribute("last_opened") is XAttribute lo)
                    {
                        long.TryParse(lo.Value, out long loVal);
                        if (loVal < cutOffTime) removeMe.Add(container);
                    }
                }
                foreach (XElement container in removeMe) container.Remove();
            }

            private bool SaveFileCheck()
            {
                try
                {
                    if (!File.Exists(gridSavePath))
                        File.Create(gridSavePath).Dispose();
                }
                catch { return false; }
                return true;
            }

            internal void Clear() { instance = null; }
        }
    }
}

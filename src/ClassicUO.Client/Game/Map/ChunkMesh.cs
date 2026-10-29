// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Runtime.CompilerServices;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static ClassicUO.Renderer.UltimaBatcher2D;

namespace ClassicUO.Game.Map
{
    /// <summary>
    /// Tracks per-texture sprite counts and write cursors for bucket-insert building.
    /// Avoids dictionary overhead by using linear search (typically &lt; 20 unique textures per chunk).
    /// </summary>
    internal struct TextureBucketTracker
    {
        private Texture2D[] _textures;
        private int[] _counts;
        private int[] _cursors;
        private int _bucketCount;

        public TextureBucketTracker(int initialCapacity)
        {
            _textures = new Texture2D[initialCapacity];
            _counts = new int[initialCapacity];
            _cursors = new int[initialCapacity];
            _bucketCount = 0;
        }

        public void Clear()
        {
            if (_bucketCount > 0)
                Array.Clear(_textures, 0, _bucketCount);
            _bucketCount = 0;
        }

        /// <summary>
        /// Registers a texture during pass 1 (counting phase).
        /// </summary>
        public void Count(Texture2D texture)
        {
            for (int i = 0; i < _bucketCount; i++)
            {
                if (_textures[i] == texture)
                {
                    _counts[i]++;
                    return;
                }
            }

            if (_bucketCount >= _textures.Length)
            {
                int newSize = _textures.Length * 2;
                Array.Resize(ref _textures, newSize);
                Array.Resize(ref _counts, newSize);
                Array.Resize(ref _cursors, newSize);
            }

            _textures[_bucketCount] = texture;
            _counts[_bucketCount] = 1;
            _bucketCount++;
        }

        /// <summary>
        /// Computes prefix-sum offsets from the counts. Returns total sprite count.
        /// After this, GetNextIndex() can be called during pass 2.
        /// </summary>
        public int ComputeOffsets()
        {
            int offset = 0;
            for (int i = 0; i < _bucketCount; i++)
            {
                _cursors[i] = offset;
                offset += _counts[i];
            }
            return offset;
        }

        /// <summary>
        /// Returns the next write index for this texture and advances its cursor.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetNextIndex(Texture2D texture)
        {
            for (int i = 0; i < _bucketCount; i++)
            {
                if (_textures[i] == texture)
                    return _cursors[i]++;
            }

            // Should never happen if pass 1 counted correctly
            return -1;
        }
    }

    internal sealed class ChunkMesh
    {
        public readonly MeshLayer Land = new();
        public readonly MeshLayer Statics = new();

        public bool IsDirty = true;

        // Server items (event-area floors, walls...) arrive and leave in bursts while you move. Rebuilding the
        // chunk for every one of them would cost more than it saves, so item changes are batched: rebuild once
        // things settle for 250 ms, or at the latest after 1 s. Until then new items simply draw the normal
        // (per-object) way, and removed items are never drawn because nothing marks their quad visible.
        private const uint ITEM_SETTLE_MS = 250;
        private const uint ITEM_MAX_WAIT_MS = 1000;
        private bool _itemsChanged;
        private uint _firstItemChange, _lastItemChange;

        public bool NeedsBuild =>
            IsDirty
            || _itemsChanged
                && (Time.Ticks - _lastItemChange >= ITEM_SETTLE_MS || Time.Ticks - _firstItemChange >= ITEM_MAX_WAIT_MS);

        public void NoteItemChanged()
        {
            if (!_itemsChanged)
            {
                _itemsChanged = true;
                _firstItemChange = Time.Ticks;
            }

            _lastItemChange = Time.Ticks;
        }

        private bool _animatedWaterEffect;
        private TextureBucketTracker _landBuckets = new(16);
        private TextureBucketTracker _staticsBuckets = new(32);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirtyIfNeeded(GameObject obj)
        {
            if (obj is Item item)
            {
                // A batched item that moves/leaves must not reuse its old quad: draw it per-object until rebuilt.
                if (item.InChunkMesh)
                {
                    item.InChunkMesh = false;
                    item.MeshSpriteIndex = -1;
                }

                if (BatchGroundItemsEnabled)
                    NoteItemChanged();

                return;
            }

            if (!IsDirty && obj is GameObjects.Land or Static or Multi)
                IsDirty = true;
        }

        public static bool BatchGroundItemsEnabled => ProfileManager.CurrentProfile?.BatchGroundItems ?? false;

        /// <summary>
        /// Plain decoration items (event-area floors, walls, roofs...) that can be drawn from the chunk mesh like
        /// map statics: on the ground, not movable, a single non-animated graphic, no containers/doors/corpses,
        /// nothing that ReX re-draws specially (fields, traps, stealth art, gravestone replacement).
        /// </summary>
        public static bool IsBatchableItem(Item item, out ushort graphic)
        {
            graphic = 0;

            if (
                item.IsDestroyed
                || !item.AllowedToDraw
                || !item.OnGround
                || item.IsMulti
                || item.IsCorpse
                || item.IsHidden
                || (item.Flags & Flags.Movable) != 0
            )
            {
                return false;
            }

            graphic = item.DisplayedGraphic;

            if (graphic != item.Graphic || graphic >= Client.Game.UO.FileManager.TileData.StaticData.Length)
            {
                return false; // animated frame, coin stack...
            }

            ref StaticTiles data = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];

            if (data.IsDoor || data.IsContainer || data.IsWet && data.IsAnimated)
            {
                return false;
            }

            if (item.Amount > 1 && data.IsStackable)
            {
                return false; // drawn twice (stack offset)
            }

            if (
                StaticFilters.IsField(graphic)
                || StaticFilters.IsWallOfStone(graphic)
                || DustLightVisuals.IsStealthArt(graphic)
                || StaticArtReplacements.ReplaceFixedGroundItem(graphic, false) != graphic
            )
            {
                return false;
            }

            return !IsStaticExcludedFromMesh(graphic, ref data);
        }

        public void Build(Chunk chunk, World world, GraphicsDevice graphicsDevice)
        {
            Land.Reset();
            Statics.Reset();
            IsDirty = false;
            _itemsChanged = false;
            bool batchItems = BatchGroundItemsEnabled;

            var profile = ProfileManager.CurrentProfile;
            if (profile == null)
                return;

            _animatedWaterEffect = profile.AnimatedWaterEffect;

            // Pass 1: count sprites per texture
            _landBuckets.Clear();
            _staticsBuckets.Clear();

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (var obj = chunk.GetHeadObject(x, y); obj != null; obj = obj.TNext)
                    {
                        obj.InChunkMesh = false;
                        obj.MeshSpriteIndex = -1;

                        switch (obj)
                        {
                            case GameObjects.Land land:
                                CountLand(land);
                                break;

                            case Static staticObj:
                                CountStatic(staticObj);
                                break;

                            case Multi multi:
                                CountMulti(multi);
                                break;

                            case Item item when batchItems && IsBatchableItem(item, out ushort itemGraphic):
                                CountStaticLike(ref Client.Game.UO.FileManager.TileData.StaticData[itemGraphic], itemGraphic);
                                break;
                        }
                    }
                }
            }

            int landTotal = _landBuckets.ComputeOffsets();
            int staticsTotal = _staticsBuckets.ComputeOffsets();

            Land.EnsureCapacity(landTotal);
            Land.Count = landTotal;
            Statics.EnsureCapacity(staticsTotal);
            Statics.Count = staticsTotal;

            // Pass 2: write sprites at their final sorted positions
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (var obj = chunk.GetHeadObject(x, y); obj != null; obj = obj.TNext)
                    {
                        switch (obj)
                        {
                            case GameObjects.Land land:
                                TryAddLand(land);
                                break;

                            case Static staticObj:
                                TryAddStatic(staticObj);
                                break;

                            case Multi multi:
                                TryAddMulti(multi);
                                break;

                            case Item item when batchItems && IsBatchableItem(item, out ushort itemGraphic):
                                TryAddStaticLike(item, ref Client.Game.UO.FileManager.TileData.StaticData[itemGraphic], itemGraphic, item.Hue);

                                if (item.InChunkMesh)
                                {
                                    item.MeshedGraphic = itemGraphic;
                                    item.MeshedHue = item.Hue;
                                }
                                break;
                        }
                    }
                }
            }

            Land.UploadVertexBuffer(graphicsDevice);
            Statics.UploadVertexBuffer(graphicsDevice);
        }

        public void Clear()
        {
            Land.Reset();
            Land.Dispose();
            Statics.Reset();
            Statics.Dispose();
            IsDirty = true;
        }

        /// <summary>
        /// Resets mesh state for pool reuse. Keeps GPU buffers and arrays allocated.
        /// </summary>
        public void SoftClear()
        {
            Land.SoftReset();
            Statics.SoftReset();
            _landBuckets.Clear();
            _staticsBuckets.Clear();
            IsDirty = true;
            _itemsChanged = false;
        }

        #region Pass 1: Texture counting

        private void CountLand(GameObjects.Land land)
        {
            if (!land.AllowedToDraw || land.IsDestroyed)
                return;

            if (_animatedWaterEffect && land.TileData.IsWet)
                return;

            Texture2D texture;
            if (land.IsStretched)
            {
                ref readonly var texmapInfo = ref Client.Game.UO.Texmaps.GetTexmap(
                    Client.Game.UO.FileManager.TileData.LandData[land.Graphic].TexID
                );

                if (texmapInfo.Texture != null)
                    texture = texmapInfo.Texture;
                else
                {
                    ref readonly var artInfo = ref Client.Game.UO.Arts.GetLand(land.Graphic);
                    if (artInfo.Texture == null)
                        return;
                    texture = artInfo.Texture;
                }
            }
            else
            {
                ref readonly var artInfo = ref Client.Game.UO.Arts.GetLand(land.Graphic);
                if (artInfo.Texture == null)
                    return;
                texture = artInfo.Texture;
            }

            _landBuckets.Count(texture);
        }

        private void CountStatic(Static staticObj)
        {
            if (!staticObj.AllowedToDraw || staticObj.IsDestroyed)
                return;

            if (ShouldHideVegetation(staticObj))
                return;

            ushort graphic = StaticArtReplacements.ReplaceFixedStatic(staticObj.Graphic);
            ref StaticTiles itemData = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];
            CountStaticLike(ref itemData, graphic);
        }

        private void CountMulti(Multi multi)
        {
            if (!multi.AllowedToDraw || multi.IsDestroyed)
                return;

            if (multi.State != 0)
                return;

            if (ShouldHideVegetation(multi))
                return;

            ushort graphic = StaticArtReplacements.Replace(multi.Graphic);
            ref StaticTiles itemData = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];
            CountStaticLike(ref itemData, graphic);
        }

        private void CountStaticLike(ref StaticTiles itemData, ushort graphic)
        {
            if (IsStaticExcludedFromMesh(graphic, ref itemData))
                return;

            ref readonly var artInfo = ref Client.Game.UO.Arts.GetArt(graphic);
            if (artInfo.Texture == null)
                return;

            _staticsBuckets.Count(artInfo.Texture);
        }

        #endregion

        #region Pass 2: Vertex building

        private void TryAddLand(GameObjects.Land land)
        {
            if (!land.AllowedToDraw || land.IsDestroyed)
                return;

            if (_animatedWaterEffect && land.TileData.IsWet)
                return;

            ushort hue = land.Hue;
            Vector3 hueVec;
            if (hue != 0)
            {
                hueVec.X = hue - 1;
                hueVec.Y = land.IsStretched
                    ? ShaderHueTranslator.SHADER_LAND_HUED
                    : ShaderHueTranslator.SHADER_HUED;
            }
            else
            {
                hueVec.X = 0;
                hueVec.Y = land.IsStretched
                    ? ShaderHueTranslator.SHADER_LAND
                    : ShaderHueTranslator.SHADER_NONE;
            }
            hueVec.Z = 1f;

            float depth = land.CalculateDepthZ() + 0.5f;
            int baseX = (land.X - land.Y) * 22 - 22;
            int baseY = (land.X + land.Y) * 22 - (land.Z << 2) - 22;

            if (land.IsStretched)
            {
                ref readonly var texmapInfo = ref Client.Game.UO.Texmaps.GetTexmap(
                    Client.Game.UO.FileManager.TileData.LandData[land.Graphic].TexID
                );

                if (texmapInfo.Texture != null)
                {
                    int idx = _landBuckets.GetNextIndex(texmapInfo.Texture);
                    land.MeshSpriteIndex = idx;
                    WriteStretchedLand(
                        idx,
                        texmapInfo.Texture,
                        texmapInfo.UV,
                        baseX,
                        baseY + (land.Z << 2),
                        ref land.YOffsets,
                        ref land.NormalTop,
                        ref land.NormalRight,
                        ref land.NormalLeft,
                        ref land.NormalBottom,
                        hueVec,
                        depth
                    );
                    land.InChunkMesh = true;
                }
                else
                {
                    ref readonly var artInfo = ref Client.Game.UO.Arts.GetLand(land.Graphic);
                    if (artInfo.Texture != null)
                    {
                        int idx = _landBuckets.GetNextIndex(artInfo.Texture);
                        land.MeshSpriteIndex = idx;
                        Land.WriteQuadAt(idx, artInfo.Texture, artInfo.UV,
                            baseX, baseY + (land.Z << 2), hueVec, depth);
                        land.InChunkMesh = true;
                    }
                }
            }
            else
            {
                ref readonly var artInfo = ref Client.Game.UO.Arts.GetLand(land.Graphic);
                if (artInfo.Texture != null)
                {
                    int idx = _landBuckets.GetNextIndex(artInfo.Texture);
                    land.MeshSpriteIndex = idx;
                    Land.WriteQuadAt(idx, artInfo.Texture, artInfo.UV, baseX, baseY, hueVec, depth);
                    land.InChunkMesh = true;
                }
            }
        }

        /// <summary>
        /// Checks if a static/multi should be excluded from the chunk mesh.
        /// </summary>
        private static bool IsStaticExcludedFromMesh(ushort graphic, ref StaticTiles itemData)
        {
            if (itemData.IsInternal)
                return true;

            if (itemData.IsAnimated)
                return true;

            if (itemData.IsFoliage)
                return true;

            Profile profile = ProfileManager.CurrentProfile;

            if (profile != null && profile.ShowTrapTiles && StaticFilters.IsTrapMarker(graphic, out _))
                return true;

            if (StaticFilters.IsTree(graphic, out _))
                return true;

            if (StaticFilters.IsRock(graphic))
                return true;

            return false;
        }

        private void TryAddStatic(Static staticObj)
        {
            if (!staticObj.AllowedToDraw || staticObj.IsDestroyed)
                return;

            if (ShouldHideVegetation(staticObj))
                return;

            ushort graphic = StaticArtReplacements.ReplaceFixedStatic(staticObj.Graphic);
            ref StaticTiles itemData = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];
            TryAddStaticLike(staticObj, ref itemData, graphic, staticObj.Hue);
        }

        private void TryAddMulti(Multi multi)
        {
            if (!multi.AllowedToDraw || multi.IsDestroyed)
                return;

            if (multi.State != 0)
                return;

            if (ShouldHideVegetation(multi))
                return;

            ushort graphic = StaticArtReplacements.Replace(multi.Graphic);
            ref StaticTiles itemData = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];
            TryAddStaticLike(multi, ref itemData, graphic, multi.Hue);
        }

        private void TryAddStaticLike(GameObject obj, ref StaticTiles itemData, ushort graphic, ushort hue)
        {
            if (IsStaticExcludedFromMesh(graphic, ref itemData))
                return;

            Vector3 hueVec = ShaderHueTranslator.GetHueVector(hue, itemData.IsPartialHue, 1f);

            float depth = obj.CalculateDepthZ() + 0.5f;
            int baseX = (obj.X - obj.Y) * 22 - 22;
            int baseY = (obj.X + obj.Y) * 22 - (obj.Z << 2) - 22;

            ref readonly var artInfo = ref Client.Game.UO.Arts.GetArt(graphic);
            if (artInfo.Texture == null)
                return;

            ref var artIndex = ref Client.Game.UO.FileManager.Arts.File.GetValidRefEntry(graphic + 0x4000);
            artIndex.Width = (short)((artInfo.UV.Width >> 1) - 22);
            artIndex.Height = (short)(artInfo.UV.Height - 44);

            int posX = baseX - artIndex.Width;
            int posY = baseY - artIndex.Height;

            int idx = _staticsBuckets.GetNextIndex(artInfo.Texture);
            obj.MeshSpriteIndex = idx;
            Statics.WriteQuadAt(idx, artInfo.Texture, artInfo.UV, posX, posY, hueVec, depth);
            obj.InChunkMesh = true;
        }

        private static bool ShouldHideVegetation(Static staticObj)
        {
            Profile profile = ProfileManager.CurrentProfile;

            return profile != null
                   && profile.HideVegetation
                   && !staticObj.ItemData.IsMultiMovable
                   && !staticObj.ItemData.IsImpassable
                   && staticObj.IsVegetation;
        }

        private static bool ShouldHideVegetation(Multi multi)
        {
            Profile profile = ProfileManager.CurrentProfile;

            return profile != null
                   && profile.HideVegetation
                   && !multi.ItemData.IsMultiMovable
                   && !multi.ItemData.IsImpassable
                   && multi.IsVegetation;
        }

        private void WriteStretchedLand(
            int index,
            Texture2D texture, Rectangle sourceRect,
            int posX, int posY,
            ref YOffsets yOffsets,
            ref Vector3 normalTop, ref Vector3 normalRight,
            ref Vector3 normalLeft, ref Vector3 normalBottom,
            Vector3 hue, float depth)
        {
            ref var vertex = ref Land.Vertices[index];

            // Half-pixel inset to prevent texture bleeding at tile seams
            float sourceX = (sourceRect.X + 0.5f) / (float)texture.Width;
            float sourceY = (sourceRect.Y + 0.5f) / (float)texture.Height;
            float sourceW = (sourceRect.Width - 1f) / (float)texture.Width;
            float sourceH = (sourceRect.Height - 1f) / (float)texture.Height;

            vertex.TextureCoordinate0.X = sourceX;
            vertex.TextureCoordinate0.Y = sourceY;
            vertex.TextureCoordinate0.Z = 0;
            vertex.TextureCoordinate1.X = sourceW + sourceX;
            vertex.TextureCoordinate1.Y = sourceY;
            vertex.TextureCoordinate1.Z = 0;
            vertex.TextureCoordinate2.X = sourceX;
            vertex.TextureCoordinate2.Y = sourceH + sourceY;
            vertex.TextureCoordinate2.Z = 0;
            vertex.TextureCoordinate3.X = sourceW + sourceX;
            vertex.TextureCoordinate3.Y = sourceH + sourceY;
            vertex.TextureCoordinate3.Z = 0;

            vertex.Normal0 = normalTop;
            vertex.Normal1 = normalRight;
            vertex.Normal2 = normalLeft;
            vertex.Normal3 = normalBottom;

            vertex.Position0.X = posX + 22;
            vertex.Position0.Y = posY - yOffsets.Top;
            vertex.Position1.X = posX + 44;
            vertex.Position1.Y = posY + (22 - yOffsets.Right);
            vertex.Position2.X = posX;
            vertex.Position2.Y = posY + (22 - yOffsets.Left);
            vertex.Position3.X = posX + 22;
            vertex.Position3.Y = posY + (44 - yOffsets.Bottom);

            vertex.Position0.Z = vertex.Position1.Z = vertex.Position2.Z = vertex.Position3.Z = depth;

            vertex.Hue0 = vertex.Hue1 = vertex.Hue2 = vertex.Hue3 = hue;

            Land.Textures[index] = texture;
        }

        #endregion
    }
}

/*
 * Copyright (c) 2006-2008, openmetaverse.org
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the openmetaverse.org nor the names
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using OpenMetaverse;
using OpenMetaverse.Packets;
using OpenMetaverse.Imaging;
using OpenMetaverse.Assets;

namespace OpenMetaverse
{
    #region Enums

    /// <summary>
    /// Index of TextureEntry slots for avatar appearances
    /// </summary>
    public enum AvatarTextureIndex
    {
        Unknown = -1,
        HeadBodypaint = 0,
        UpperShirt,
        LowerPants,
        EyesIris,
        Hair,
        UpperBodypaint,
        LowerBodypaint,
        LowerShoes,
        HeadBaked,
        UpperBaked,
        LowerBaked,
        EyesBaked,
        LowerSocks,
        UpperJacket,
        LowerJacket,
        UpperGloves,
        UpperUndershirt,
        LowerUnderpants,
        Skirt,
        SkirtBaked,
        HairBaked
    }

    /// <summary>
    /// Bake layers for avatar appearance
    /// </summary>
    public enum BakeType
    {
        Unknown = -1,
        Head = 0,
        UpperBody = 1,
        LowerBody = 2,
        Eyes = 3,
        Skirt = 4,
        Hair = 5
    }

    #endregion Enums

    public class AppearanceManager
    {
        #region Constants

        /// <summary>Maximum number of concurrent downloads for wearable assets and textures</summary>
        const int MAX_CONCURRENT_DOWNLOADS = 5;
        /// <summary>Maximum number of concurrent uploads for baked textures</summary>
        const int MAX_CONCURRENT_UPLOADS = 3;
        /// <summary>Timeout for fetching inventory listings</summary>
        const int INVENTORY_TIMEOUT = 1000 * 20;
        /// <summary>Timeout for fetching a single wearable</summary>
        const int WEARABLE_TIMEOUT = 1000 * 10;
        /// <summary>Timeout for fetching a single texture</summary>
        const int TEXTURE_TIMEOUT = 1000 * 30;
        /// <summary>Timeout for uploading a single baked texture</summary>
        const int UPLOAD_TIMEOUT = 1000 * 30;

        /// <summary>Total number of wearables for each avatar</summary>
        public const int WEARABLE_COUNT = 13;
        /// <summary>Total number of baked textures on each avatar</summary>
        public const int BAKED_TEXTURE_COUNT = 6;
        /// <summary>Total number of wearables per bake layer</summary>
        public const int WEARABLES_PER_LAYER = 7;
        /// <summary>Total number of textures on an avatar, baked or not</summary>
        public const int AVATAR_TEXTURE_COUNT = 21;
        /// <summary>Map of what wearables are included in each bake</summary>
        public static readonly WearableType[][] WEARABLE_BAKE_MAP = new WearableType[][]
        {
            new WearableType[] { WearableType.Shape, WearableType.Skin,    WearableType.Hair,    WearableType.Invalid, WearableType.Invalid, WearableType.Invalid,    WearableType.Invalid    },
            new WearableType[] { WearableType.Shape, WearableType.Skin,    WearableType.Shirt,   WearableType.Jacket,  WearableType.Gloves,  WearableType.Undershirt, WearableType.Invalid    },
            new WearableType[] { WearableType.Shape, WearableType.Skin,    WearableType.Pants,   WearableType.Shoes,   WearableType.Socks,   WearableType.Jacket,     WearableType.Underpants },
            new WearableType[] { WearableType.Eyes,  WearableType.Invalid, WearableType.Invalid, WearableType.Invalid, WearableType.Invalid, WearableType.Invalid,    WearableType.Invalid    },
            new WearableType[] { WearableType.Skirt, WearableType.Invalid, WearableType.Invalid, WearableType.Invalid, WearableType.Invalid, WearableType.Invalid,    WearableType.Invalid    },
            new WearableType[] { WearableType.Hair,  WearableType.Invalid, WearableType.Invalid, WearableType.Invalid, WearableType.Invalid, WearableType.Invalid,    WearableType.Invalid    }
        };
        /// <summary>Magic values to finalize the cache check hashes for each
        /// bake</summary>
        public static readonly UUID[] BAKED_TEXTURE_HASH = new UUID[]
        {
            new UUID("18ded8d6-bcfc-e415-8539-944c0f5ea7a6"),
            new UUID("338c29e3-3024-4dbb-998d-7c04cf4fa88f"),
            new UUID("91b4a2c7-1b1a-ba16-9a16-1f8f8dcc1c3f"),
            new UUID("b2cf28af-b840-1071-3c6a-78085d8128b5"),
            new UUID("ea800387-ea1a-14e0-56cb-24f2022f969a"),
            new UUID("0af1ef7c-ad24-11dd-8790-001f5bf833e8")
        };
        /// <summary>Default avatar texture, used to detect when a custom
        /// texture is not set for a face</summary>
        public static readonly UUID DEFAULT_AVATAR_TEXTURE = new UUID("c228d1cf-4b5d-4ba8-84f4-899a0796aa97");

        #endregion Constants

        #region Structs / Classes

        /// <summary>
        /// Contains information about a wearable inventory item
        /// </summary>
        public class WearableData
        {
            /// <summary>Inventory ItemID of the wearable</summary>
            public UUID ItemID;
            /// <summary>AssetID of the wearable asset</summary>
            public UUID AssetID;
            /// <summary>WearableType of the wearable</summary>
            public WearableType WearableType;
            /// <summary>AssetType of the wearable</summary>
            public AssetType AssetType;
            /// <summary>Asset data for the wearable</summary>
            public AssetWearable Asset;

            public override string ToString()
            {
                return String.Format("ItemID: {0}, AssetID: {1}, WearableType: {2}, AssetType: {3}, Asset: {4}",
                    ItemID, AssetID, WearableType, AssetType, Asset != null ? Asset.Name : "(null)");
            }
        }

        /// <summary>
        /// A tuple containing a TextureID and a texture asset. Used to keep track
        /// of currently worn textures and the corresponding texture data for baking
        /// </summary>
        private struct TextureData
        {
            /// <summary>A texture AssetID</summary>
            public UUID TextureID;
            /// <summary>Asset data for the texture</summary>
            public AssetTexture Texture;
            /// <summary>Collection of alpha masks that needs applying</summary>
            public Dictionary<VisualAlphaParam, float> AlphaMasks;
            /// <summary>Collection of color params used for calculating texture tint</summary>
            public Dictionary<VisualColorParam, float> ColorParams;

            public override string ToString()
            {
                return String.Format("TextureID: {0}, Texture: {1}",
                    TextureID, Texture != null ? Texture.AssetData.Length + " bytes" : "(null)");
            }
        }

        #endregion Structs / Classes

        #region Delegates / Events

        /// <summary>Triggered when an AgentWearablesUpdate packet is received,
        /// telling us what our avatar is currently wearing</summary>
        public delegate void AgentWearablesCallback();
        /// <summary>Triggered when an AgentCachedTextureResponse packet is
        /// received, giving a list of cached bakes that were found on the
        /// server</summary>
        public delegate void AgentCachedBakesCallback();

        /// <summary>Triggered when an AgentWearablesUpdate packet is received,
        /// telling us what our avatar is currently wearing</summary>
        public event AgentWearablesCallback OnAgentWearables;
        /// <summary>Triggered when an AgentCachedTextureResponse packet is
        /// received, giving a list of cached bakes that were found on the
        /// server</summary>
        public event AgentCachedBakesCallback OnAgentCachedBakes;

        #endregion Delegates / Events

        #region Private Members

        /// <summary>A cache of wearables currently being worn</summary>
        private Dictionary<WearableType, WearableData> Wearables = new Dictionary<WearableType, WearableData>();
        /// <summary>A cache of textures currently being worn</summary>
        private TextureData[] Textures = new TextureData[AVATAR_TEXTURE_COUNT];
        /// <summary>Incrementing serial number for AgentCachedTexture packets</summary>
        private int CacheCheckSerialNum = -1;
        /// <summary>Incrementing serial number for AgentSetAppearance packets</summary>
        private int SetAppearanceSerialNum = 0;
        /// <summary>Indicates whether or not the appearance thread is currently
        /// running, to prevent multiple appearance threads from running
        /// simultaneously</summary>
        private int AppearanceThreadRunning = 0;
        /// <summary>Reference to our agent</summary>
        private GridClient Client;

        #endregion Private Members

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="client">A reference to our agent</param>
        public AppearanceManager(GridClient client)
        {
            Client = client;

            Client.Network.RegisterCallback(PacketType.AgentWearablesUpdate, AgentWearablesUpdateHandler);
            Client.Network.RegisterCallback(PacketType.AgentCachedTextureResponse, AgentCachedTextureResponseHandler);
            //Client.Network.RegisterCallback(PacketType.RebakeAvatarTextures, RebakeAvatarTexturesHandler);

            Client.Network.OnEventQueueRunning += Network_OnEventQueueRunning;
        }

        #region Publics Methods

        /// <summary>
        /// Obsolete method for setting appearance. This function no longer does anything.
        /// Use RequestSetAppearance() to manually start the appearance thread
        /// </summary>
        [Obsolete("Appearance is now handled automatically")]
        public void SetPreviousAppearance()
        {
        }

        /// <summary>
        /// Obsolete method for setting appearance. This function no longer does anything.
        /// Use RequestSetAppearance() to manually start the appearance thread
        /// </summary>
        /// <param name="allowBake">Unused parameter</param>
        [Obsolete("Appearance is now handled automatically")]
        public void SetPreviousAppearance(bool allowBake)
        {
        }

        /// <summary>
        /// Starts the appearance setting thread
        /// </summary>
        public void RequestSetAppearance()
        {
            RequestSetAppearance(false);
        }

        /// <summary>
        /// Starts the appearance setting thread
        /// </summary>
        /// <param name="forceRebake">True to force rebaking, otherwise false</param>
        public void RequestSetAppearance(bool forceRebake)
        {
            if (Interlocked.CompareExchange(ref AppearanceThreadRunning, 1, 0) != 0)
            {
                Logger.Log("Appearance thread is already running, skipping", Helpers.LogLevel.Warning);
                return;
            }

            // This is the first time setting appearance, run through the entire sequence
            Thread appearanceThread = new Thread(
                delegate()
                {
                    try
                    {
                        if (forceRebake)
                        {
                            // Set all of the baked textures to UUID.Zero to force rebaking
                            for (int bakedIndex = 0; bakedIndex < BAKED_TEXTURE_COUNT; bakedIndex++)
                                Textures[(int)BakeTypeToAgentTextureIndex((BakeType)bakedIndex)].TextureID = UUID.Zero;
                        }

                        if (SetAppearanceSerialNum == 0)
                        {
                            // Fetch a list of the current agent wearables
                            if (!GetAgentWearables())
                            {
                                Logger.Log("Failed to retrieve a list of current agent wearables, appearance cannot be set",
                                    Helpers.LogLevel.Error, Client);
                                return;
                            }
                        }

                        // Download and parse all of the agent wearables
                        if (!DownloadWearables())
                        {
                            Logger.Log("One or more agent wearables failed to download, appearance will be incomplete",
                                Helpers.LogLevel.Warning, Client);
                        }

                        // If this is the first time setting appearance and we're not forcing rebakes, check the server
                        // for cached bakes
                        if (SetAppearanceSerialNum == 0 && !forceRebake)
                        {
                            // Compute hashes for each bake layer and compare against what the simulator currently has
                            if (!GetCachedBakes())
                            {
                                Logger.Log("Failed to get a list of cached bakes from the simulator, appearance will be rebaked",
                                    Helpers.LogLevel.Warning, Client);
                            }
                        }

                        // Download textures, compute bakes, and upload for any cache misses
                        if (!CreateBakes())
                        {
                            Logger.Log("Failed to create or upload one or more bakes, appearance will be incomplete",
                                Helpers.LogLevel.Warning, Client);
                        }

                        // Send the appearance packet
                        SendAgentSetAppearance();
                    }
                    finally
                    {
                        AppearanceThreadRunning = 0;
                    }
                }
            );
            appearanceThread.Name = "Appearance";
            appearanceThread.IsBackground = true;
            appearanceThread.Start();
        }

        /// <summary>
        /// Ask the server what textures our agent is currently wearing
        /// </summary>
        public void RequestAgentWearables()
        {
            AgentWearablesRequestPacket request = new AgentWearablesRequestPacket();
            request.AgentData.AgentID = Client.Self.AgentID;
            request.AgentData.SessionID = Client.Self.SessionID;

            Client.Network.SendPacket(request);
        }

        /// <summary>
        /// Build hashes out of the texture assetIDs for each baking layer to
        /// ask the simulator whether it has cached copies of each baked texture
        /// </summary>
        public void RequestCachedBakes()
        {
            List<AgentCachedTexturePacket.WearableDataBlock> hashes = new List<AgentCachedTexturePacket.WearableDataBlock>();

            // Build hashes for each of the bake layers from the individual components
            lock (Wearables)
            {
                for (int bakedIndex = 0; bakedIndex < BAKED_TEXTURE_COUNT; bakedIndex++)
                {
                    // Don't do a cache request for a skirt bake if we're not wearing a skirt
                    if (bakedIndex == (int)BakeType.Skirt && !Wearables.ContainsKey(WearableType.Skirt))
                        continue;

                    // Build a hash of all the texture asset IDs in this baking layer
                    UUID hash = UUID.Zero;
                    for (int wearableIndex = 0; wearableIndex < WEARABLES_PER_LAYER; wearableIndex++)
                    {
                        WearableType type = WEARABLE_BAKE_MAP[bakedIndex][wearableIndex];

                        WearableData wearable;
                        if (type != WearableType.Invalid && Wearables.TryGetValue(type, out wearable))
                            hash ^= wearable.AssetID;
                    }

                    if (hash != UUID.Zero)
                    {
                        // Hash with our secret value for this baked layer
                        hash ^= BAKED_TEXTURE_HASH[bakedIndex];

                        // Add this to the list of hashes to send out
                        AgentCachedTexturePacket.WearableDataBlock block = new AgentCachedTexturePacket.WearableDataBlock();
                        block.ID = hash;
                        block.TextureIndex = (byte)bakedIndex;
                        hashes.Add(block);

                        Logger.DebugLog("Checking cache for " + (BakeType)block.TextureIndex + ", hash=" + block.ID, Client);
                    }
                }
            }

            // Only send the packet out if there's something to check
            if (hashes.Count > 0)
            {
                AgentCachedTexturePacket cache = new AgentCachedTexturePacket();
                cache.AgentData.AgentID = Client.Self.AgentID;
                cache.AgentData.SessionID = Client.Self.SessionID;
                cache.AgentData.SerialNum = Interlocked.Increment(ref CacheCheckSerialNum);

                cache.WearableData = hashes.ToArray();

                Client.Network.SendPacket(cache);
            }
        }

        /// <summary>
        /// Returns the AssetID of the asset that is currently being worn in a 
        /// given WearableType slot
        /// </summary>
        /// <param name="type">WearableType slot to get the AssetID for</param>
        /// <returns>The UUID of the asset being worn in the given slot, or
        /// UUID.Zero if no wearable is attached to the given slot or wearables
        /// have not been downloaded yet</returns>
        public UUID GetWearableAsset(WearableType type)
        {
            WearableData wearable;

            if (Wearables.TryGetValue(type, out wearable))
                return wearable.AssetID;
            else
                return UUID.Zero;
        }

        /// <summary>
        /// Replace the current outfit with a list of wearables and set appearance
        /// </summary>
        /// <param name="wearableItems">List of wearable inventory items that
        /// define a new outfit</param>
        public void WearOutfit(List<InventoryItem> wearableItems)
        {
            List<InventoryWearable> wearables = new List<InventoryWearable>();
            List<InventoryItem> attachments = new List<InventoryItem>();

            for (int i = 0; i < wearableItems.Count; i++)
            {
            }
        }

        /// <summary>
        /// Checks if an inventory item is currently being worn
        /// </summary>
        /// <param name="item">The inventory item to check against the agent
        /// wearables</param>
        /// <returns>The WearableType slot that the item is being worn in,
        /// or WearbleType.Invalid if it is not currently being worn</returns>
        public WearableType IsItemWorn(InventoryItem item)
        {
            lock (Wearables)
            {
                foreach (KeyValuePair<WearableType, WearableData> entry in Wearables)
                {
                    if (entry.Value.ItemID == item.UUID)
                        return entry.Key;
                }
            }

            return WearableType.Invalid;
        }

        /// <summary>
        /// Returns a copy of the agents currently worn wearables
        /// </summary>
        /// <returns>A copy of the agents currently worn wearables</returns>
        /// <remarks>Avoid calling this function multiple times as it will make
        /// a copy of all of the wearable data each time</remarks>
        public Dictionary<WearableType, WearableData> GetWearables()
        {
            lock (Wearables)
                return new Dictionary<WearableType, WearableData>(Wearables);
        }

        #endregion Publics Methods

        #region Attachments

        /// <summary>
        /// Adds a list of attachments to our agent
        /// </summary>
        /// <param name="attachments">A List containing the attachments to add</param>
        /// <param name="removeExistingFirst">If true, tells simulator to remove existing attachment
        /// first</param>
        public void AddAttachments(List<InventoryItem> attachments, bool removeExistingFirst)
        {
            // Use RezMultipleAttachmentsFromInv  to clear out current attachments, and attach new ones
            RezMultipleAttachmentsFromInvPacket attachmentsPacket = new RezMultipleAttachmentsFromInvPacket();
            attachmentsPacket.AgentData.AgentID = Client.Self.AgentID;
            attachmentsPacket.AgentData.SessionID = Client.Self.SessionID;

            attachmentsPacket.HeaderData.CompoundMsgID = UUID.Random();
            attachmentsPacket.HeaderData.FirstDetachAll = removeExistingFirst;
            attachmentsPacket.HeaderData.TotalObjects = (byte)attachments.Count;

            attachmentsPacket.ObjectData = new RezMultipleAttachmentsFromInvPacket.ObjectDataBlock[attachments.Count];
            for (int i = 0; i < attachments.Count; i++)
            {
                if (attachments[i] is InventoryAttachment)
                {
                    InventoryAttachment attachment = (InventoryAttachment)attachments[i];
                    attachmentsPacket.ObjectData[i] = new RezMultipleAttachmentsFromInvPacket.ObjectDataBlock();
                    attachmentsPacket.ObjectData[i].AttachmentPt = (byte)attachment.AttachmentPoint;
                    attachmentsPacket.ObjectData[i].EveryoneMask = (uint)attachment.Permissions.EveryoneMask;
                    attachmentsPacket.ObjectData[i].GroupMask = (uint)attachment.Permissions.GroupMask;
                    attachmentsPacket.ObjectData[i].ItemFlags = (uint)attachment.Flags;
                    attachmentsPacket.ObjectData[i].ItemID = attachment.UUID;
                    attachmentsPacket.ObjectData[i].Name = Utils.StringToBytes(attachment.Name);
                    attachmentsPacket.ObjectData[i].Description = Utils.StringToBytes(attachment.Description);
                    attachmentsPacket.ObjectData[i].NextOwnerMask = (uint)attachment.Permissions.NextOwnerMask;
                    attachmentsPacket.ObjectData[i].OwnerID = attachment.OwnerID;
                }
                else if (attachments[i] is InventoryObject)
                {
                    InventoryObject attachment = (InventoryObject)attachments[i];
                    attachmentsPacket.ObjectData[i] = new RezMultipleAttachmentsFromInvPacket.ObjectDataBlock();
                    attachmentsPacket.ObjectData[i].AttachmentPt = 0;
                    attachmentsPacket.ObjectData[i].EveryoneMask = (uint)attachment.Permissions.EveryoneMask;
                    attachmentsPacket.ObjectData[i].GroupMask = (uint)attachment.Permissions.GroupMask;
                    attachmentsPacket.ObjectData[i].ItemFlags = (uint)attachment.Flags;
                    attachmentsPacket.ObjectData[i].ItemID = attachment.UUID;
                    attachmentsPacket.ObjectData[i].Name = Utils.StringToBytes(attachment.Name);
                    attachmentsPacket.ObjectData[i].Description = Utils.StringToBytes(attachment.Description);
                    attachmentsPacket.ObjectData[i].NextOwnerMask = (uint)attachment.Permissions.NextOwnerMask;
                    attachmentsPacket.ObjectData[i].OwnerID = attachment.OwnerID;
                }
                else
                {
                    Logger.Log("Cannot attach inventory item " + attachments[i].Name, Helpers.LogLevel.Warning, Client);
                }
            }

            Client.Network.SendPacket(attachmentsPacket);
        }

        /// <summary>
        /// Attach an item to our agent at a specific attach point
        /// </summary>
        /// <param name="item">A <seealso cref="OpenMetaverse.InventoryItem"/> to attach</param>
        /// <param name="attachPoint">the <seealso cref="OpenMetaverse.AttachmentPoint"/> on the avatar 
        /// to attach the item to</param>
        public void Attach(InventoryItem item, AttachmentPoint attachPoint)
        {
            Attach(item.UUID, item.OwnerID, item.Name, item.Description, item.Permissions, item.Flags,
                attachPoint);
        }

        /// <summary>
        /// Attach an item to our agent specifying attachment details
        /// </summary>
        /// <param name="itemID">The <seealso cref="OpenMetaverse.UUID"/> of the item to attach</param>
        /// <param name="ownerID">The <seealso cref="OpenMetaverse.UUID"/> attachments owner</param>
        /// <param name="name">The name of the attachment</param>
        /// <param name="description">The description of the attahment</param>
        /// <param name="perms">The <seealso cref="OpenMetaverse.Permissions"/> to apply when attached</param>
        /// <param name="itemFlags">The <seealso cref="OpenMetaverse.InventoryItemFlags"/> of the attachment</param>
        /// <param name="attachPoint">The <seealso cref="OpenMetaverse.AttachmentPoint"/> on the agent
        /// to attach the item to</param>
        public void Attach(UUID itemID, UUID ownerID, string name, string description,
            Permissions perms, uint itemFlags, AttachmentPoint attachPoint)
        {
            // TODO: At some point it might be beneficial to have AppearanceManager track what we
            // are currently wearing for attachments to make enumeration and detachment easier
            RezSingleAttachmentFromInvPacket attach = new RezSingleAttachmentFromInvPacket();

            attach.AgentData.AgentID = Client.Self.AgentID;
            attach.AgentData.SessionID = Client.Self.SessionID;

            attach.ObjectData.AttachmentPt = (byte)attachPoint;
            attach.ObjectData.Description = Utils.StringToBytes(description);
            attach.ObjectData.EveryoneMask = (uint)perms.EveryoneMask;
            attach.ObjectData.GroupMask = (uint)perms.GroupMask;
            attach.ObjectData.ItemFlags = itemFlags;
            attach.ObjectData.ItemID = itemID;
            attach.ObjectData.Name = Utils.StringToBytes(name);
            attach.ObjectData.NextOwnerMask = (uint)perms.NextOwnerMask;
            attach.ObjectData.OwnerID = ownerID;

            Client.Network.SendPacket(attach);
        }

        /// <summary>
        /// Detach an item from our agent using an <seealso cref="OpenMetaverse.InventoryItem"/> object
        /// </summary>
        /// <param name="item">An <seealso cref="OpenMetaverse.InventoryItem"/> object</param>
        public void Detach(InventoryItem item)
        {
            Detach(item.UUID);
        }

        /// <summary>
        /// Detach an item from our agent
        /// </summary>
        /// <param name="itemID">The inventory itemID of the item to detach</param>
        public void Detach(UUID itemID)
        {
            DetachAttachmentIntoInvPacket detach = new DetachAttachmentIntoInvPacket();
            detach.ObjectData.AgentID = Client.Self.AgentID;
            detach.ObjectData.ItemID = itemID;

            Client.Network.SendPacket(detach);
        }

        #endregion Attachments

        #region Appearance Helpers

        /// <summary>
        /// Blocking method to populate the Wearables dictionary
        /// </summary>
        /// <returns>True on success, otherwise false</returns>
        bool GetAgentWearables()
        {
            AutoResetEvent wearablesEvent = new AutoResetEvent(false);
            AgentWearablesCallback wearablesCallback = delegate() { wearablesEvent.Set(); };

            OnAgentWearables += wearablesCallback;

            RequestAgentWearables();

            bool success = wearablesEvent.WaitOne(1000 * 10);

            OnAgentWearables -= wearablesCallback;

            return success;
        }

        /// <summary>
        /// Blocking method to populate the Textures array with cached bakes
        /// </summary>
        /// <returns>True on success, otherwise false</returns>
        bool GetCachedBakes()
        {
            AutoResetEvent cacheCheckEvent = new AutoResetEvent(false);
            AgentCachedBakesCallback cacheCallback = delegate() { cacheCheckEvent.Set(); };

            OnAgentCachedBakes += cacheCallback;

            RequestCachedBakes();

            bool success = cacheCheckEvent.WaitOne(1000 * 10);

            OnAgentCachedBakes -= cacheCallback;

            return success;
        }

        /// <summary>
        /// Blocking method to download and parse currently worn wearable assets
        /// </summary>
        /// <returns>True on success, otherwise false</returns>
        private bool DownloadWearables()
        {
            bool success = true;

            // Make a copy of the wearables dictionary to enumerate over
            Dictionary<WearableType, WearableData> wearables;
            lock (Wearables)
                wearables = new Dictionary<WearableType, WearableData>(Wearables);

            int pendingWearables = wearables.Count;
            foreach (WearableData wearable in wearables.Values)
            {
                if (wearable.Asset != null)
                    --pendingWearables;
            }

            if (pendingWearables == 0)
                return true;

            Logger.DebugLog("Downloading " + pendingWearables + " wearable assets");

            Parallel.ForEach<WearableData>(Math.Min(pendingWearables, MAX_CONCURRENT_DOWNLOADS), wearables.Values,
                delegate(WearableData wearable)
                {
                    if (wearable.Asset == null)
                    {
                        AutoResetEvent downloadEvent = new AutoResetEvent(false);

                        // Fetch this wearable asset
                        Client.Assets.RequestAsset(wearable.AssetID, wearable.AssetType, true,
                            delegate(AssetDownload transfer, Asset asset)
                            {
                                if (transfer.Success && asset is AssetWearable)
                                {
                                    // Update this wearable with the freshly downloaded asset 
                                    wearable.Asset = (AssetWearable)asset;

                                    if (wearable.Asset.Decode())
                                    {
                                        Logger.DebugLog("Downloaded wearable asset " + wearable.WearableType + " with " + wearable.Asset.Params.Count +
                                            " visual params and " + wearable.Asset.Textures.Count + " textures", Client);

                                        Dictionary<VisualAlphaParam, float> alphaMasks = new Dictionary<VisualAlphaParam, float>();
                                        Dictionary<VisualColorParam, float> colorParams = new Dictionary<VisualColorParam, float>();

                                        // Populate collection of alpha masks from visual params
                                        // also add color tinting information
                                        foreach (KeyValuePair<int, float> kvp in wearable.Asset.Params)
                                        {
                                            VisualParam p = VisualParams.Params[kvp.Key];

                                            // Color params
                                            if (p.ColorParams.HasValue)
                                            {
                                                // If this is not skin, just add params directly
                                                if (wearable.WearableType != WearableType.Skin)
                                                {
                                                    colorParams.Add(p.ColorParams.Value, kvp.Value);
                                                }
                                                else
                                                {
                                                    // For skin we skip makeup params for now and use only the 3
                                                    // that are used to determine base skin tone
                                                    // Param 108 - Rainbow Color
                                                    // Param 110 - Red Skin (Ruddiness)
                                                    // Param 111 - Pigment
                                                    if (kvp.Key == 108 || kvp.Key == 110 || kvp.Key == 111)
                                                    {
                                                        colorParams.Add(p.ColorParams.Value, kvp.Value);
                                                    }
                                                }
                                            }

                                            // Alhpa masks are specified in sub "driver" params
                                            // TODO pull bump data too to implement things like
                                            // clothes "bagginess"
                                            if (p.Drivers != null)
                                            {
                                                for (int i = 0; i < p.Drivers.Length; i++)
                                                {
                                                    if (VisualParams.Params.ContainsKey(p.Drivers[i]))
                                                    {
                                                        VisualParam driver = VisualParams.Params[p.Drivers[i]];
                                                        if (driver.AlphaParams.HasValue && driver.AlphaParams.Value.TGAFile != string.Empty && !driver.IsBumpAttribute)
                                                        {
                                                            alphaMasks.Add(driver.AlphaParams.Value, kvp.Value);
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        // Loop through all of the texture IDs in this decoded asset and put them in our cache of worn textures
                                        foreach (KeyValuePair<AvatarTextureIndex, UUID> entry in wearable.Asset.Textures)
                                        {
                                            int i = (int)entry.Key;

                                            // If this texture changed, update the TextureID and clear out the old cached texture asset
                                            if (Textures[i].TextureID != entry.Value)
                                            {
                                                // Treat DEFAULT_AVATAR_TEXTURE as null
                                                if (entry.Value != DEFAULT_AVATAR_TEXTURE)
                                                    Textures[i].TextureID = entry.Value;
                                                else
                                                    Textures[i].TextureID = UUID.Zero;
                                                Logger.DebugLog("Set " + entry.Key + " to " + Textures[i].TextureID, Client);

                                                Textures[i].AlphaMasks = alphaMasks;
                                                Textures[i].ColorParams = colorParams;
                                                Textures[i].Texture = null;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Logger.Log("Failed to decode asset:" + Environment.NewLine +
                                            Utils.BytesToString(asset.AssetData), Helpers.LogLevel.Error, Client);
                                    }
                                }
                                else
                                {
                                    Logger.Log("Wearable " + wearable.AssetID + "(" + wearable.WearableType + ") failed to download, " +
                                        transfer.Status, Helpers.LogLevel.Warning, Client);
                                }

                                downloadEvent.Set();
                            }
                        );

                        if (!downloadEvent.WaitOne(WEARABLE_TIMEOUT))
                        {
                            Logger.Log("Timed out downloading wearable asset " + wearable.AssetID + " (" + wearable.WearableType + ")",
                                Helpers.LogLevel.Error, Client);
                            success = false;
                        }

                        --pendingWearables;
                    }
                }
            );

            return success;
        }

        /// <summary>
        /// Get a list of all of the textures that need to be downloaded for a
        /// single bake layer
        /// </summary>
        /// <param name="bakeType">Bake layer to get texture AssetIDs for</param>
        /// <returns>A list of texture AssetIDs to download</returns>
        private List<UUID> GetTextureDownloadList(BakeType bakeType)
        {
            List<AvatarTextureIndex> indices = BakeTypeToTextures(bakeType);
            List<UUID> textures = new List<UUID>();

            for (int i = 0; i < indices.Count; i++)
            {
                AvatarTextureIndex index = indices[i];

                if (index == AvatarTextureIndex.Skirt && !Wearables.ContainsKey(WearableType.Skirt))
                    continue;

                AddTextureDownload(index, textures);
            }

            return textures;
        }

        /// <summary>
        /// Helper method to lookup the TextureID for a single layer and add it
        /// to a list if it is not already present
        /// </summary>
        /// <param name="index"></param>
        /// <param name="textures"></param>
        private void AddTextureDownload(AvatarTextureIndex index, List<UUID> textures)
        {
            TextureData textureData = Textures[(int)index];
            // Add the textureID to the list if this layer has a valid textureID set, it has not already
            // been downloaded, and it is not already in the download list
            if (textureData.TextureID != UUID.Zero && textureData.Texture == null && !textures.Contains(textureData.TextureID))
                textures.Add(textureData.TextureID);
        }

        /// <summary>
        /// Blocking method to download all of the textures needed for baking 
        /// the given bake layers
        /// </summary>
        /// <param name="bakeLayers">A list of layers that need baking</param>
        /// <remarks>No return value is given because the baking will happen
        /// whether or not all textures are successfully downloaded</remarks>
        private void DownloadTextures(List<BakeType> bakeLayers)
        {
            List<UUID> textureIDs = new List<UUID>();

            for (int i = 0; i < bakeLayers.Count; i++)
            {
                List<UUID> layerTextureIDs = GetTextureDownloadList(bakeLayers[i]);

                for (int j = 0; j < layerTextureIDs.Count; j++)
                {
                    UUID uuid = layerTextureIDs[j];
                    if (!textureIDs.Contains(uuid))
                        textureIDs.Add(uuid);
                }
            }

            Logger.DebugLog("Downloading " + textureIDs.Count + " textures for baking");

            Parallel.ForEach<UUID>(MAX_CONCURRENT_DOWNLOADS, textureIDs,
                delegate(UUID textureID)
                {
                    AutoResetEvent downloadEvent = new AutoResetEvent(false);

                    Client.Assets.RequestImage(textureID,
                        delegate(TextureRequestState state, AssetTexture assetTexture)
                        {
                            if (state == TextureRequestState.Finished)
                            {
                                assetTexture.Decode();

                                for (int i = 0; i < Textures.Length; i++)
                                {
                                    if (Textures[i].TextureID == textureID)
                                        Textures[i].Texture = assetTexture;
                                }
                            }
                            else
                            {
                                Logger.Log("Texture " + textureID + " failed to download, one or more bakes will be incomplete",
                                    Helpers.LogLevel.Warning);
                            }

                            downloadEvent.Set();
                        }
                    );

                    downloadEvent.WaitOne(TEXTURE_TIMEOUT, false);
                }
            );
        }

        /// <summary>
        /// Blocking method to create and upload baked textures for all of the
        /// missing bakes
        /// </summary>
        /// <returns>True on success, otherwise false</returns>
        private bool CreateBakes()
        {
            bool success = true;
            List<BakeType> pendingBakes = new List<BakeType>(0);

            // Check each bake layer in the Textures array for missing bakes
            for (int bakedIndex = 0; bakedIndex < BAKED_TEXTURE_COUNT; bakedIndex++)
            {
                AvatarTextureIndex textureIndex = BakeTypeToAgentTextureIndex((BakeType)bakedIndex);

                if (Textures[(int)textureIndex].TextureID == UUID.Zero)
                {
                    // If this is the skirt layer and we're not wearing a skirt then skip it
                    if (bakedIndex == (int)BakeType.Skirt && !Wearables.ContainsKey(WearableType.Skirt))
                        continue;

                    pendingBakes.Add((BakeType)bakedIndex);
                }
            }

            if (pendingBakes.Count > 0)
            {
                DownloadTextures(pendingBakes);

                Dictionary<int, float> paramValues = MakeParamValues();

                Parallel.ForEach<BakeType>(pendingBakes,
                    delegate(BakeType bakeType)
                    {
                        if (!CreateBake(bakeType, paramValues))
                            success = false;
                    }
                );
            }

            return success;
        }

        /// <summary>
        /// Blocking method to create and upload a baked texture for a single 
        /// bake layer
        /// </summary>
        /// <param name="bakeType">Layer to bake</param>
        /// <param name="paramValues">Dictionary of current visual param values</param>
        /// <returns>True on success, otherwise false</returns>
        private bool CreateBake(BakeType bakeType, Dictionary<int, float> paramValues)
        {
            List<AvatarTextureIndex> textureIndices = BakeTypeToTextures(bakeType);
            Baker oven = new Baker(Client, bakeType, textureIndices.Count, paramValues);

            for (int i = 0; i < textureIndices.Count; i++)
            {
                AvatarTextureIndex textureIndex = textureIndices[i];
                AssetTexture asset = Textures[(int)textureIndex].Texture;
                bool baked;

                if (asset != null)
                    baked = oven.AddTexture(textureIndex, asset, false);
                else
                    baked = oven.MissingTexture(textureIndex);

                if (baked)
                {
                    UUID newAssetID = UploadBake(oven.BakedTexture.AssetData);
                    Textures[(int)BakeTypeToAgentTextureIndex(bakeType)].TextureID = newAssetID;
                    return newAssetID != UUID.Zero;
                }
            }

            return false;
        }

        /// <summary>
        /// Blocking method to upload a baked texture
        /// </summary>
        /// <param name="textureData">Five channel JPEG2000 texture data to upload</param>
        /// <returns>UUID of the newly created asset on success, otherwise UUID.Zero</returns>
        private UUID UploadBake(byte[] textureData)
        {
            UUID bakeID = UUID.Zero;
            AutoResetEvent uploadEvent = new AutoResetEvent(false);

            Client.Assets.RequestUploadBakedTexture(textureData,
                delegate(UUID newAssetID)
                {
                    bakeID = newAssetID;
                    uploadEvent.Set();
                }
            );

            uploadEvent.WaitOne(UPLOAD_TIMEOUT, false);

            return bakeID;
        }

        /// <summary>
        /// Creates a dictionary of visual param values from the downloaded wearables
        /// </summary>
        /// <returns>A dictionary of visual param indices mapping to visual param
        /// values for our agent that can be fed to the Baker class</returns>
        private Dictionary<int, float> MakeParamValues()
        {
            Dictionary<int, float> paramValues = new Dictionary<int, float>(VisualParams.Params.Count);

            lock (Wearables)
            {
                foreach (KeyValuePair<int, VisualParam> kvp in VisualParams.Params)
                {
                    // Only Group-0 parameters are sent in AgentSetAppearance packets
                    if (kvp.Value.Group == 0)
                    {
                        bool found = false;
                        VisualParam vp = kvp.Value;

                        // Try and find this value in our collection of downloaded wearables
                        foreach (WearableData data in Wearables.Values)
                        {
                            float paramValue;
                            if (data.Asset != null && data.Asset.Params.TryGetValue(vp.ParamID, out paramValue))
                            {
                                paramValues.Add(vp.ParamID, paramValue);
                                found = true;
                                break;
                            }
                        }

                        // Use a default value if we don't have one set for it
                        if (!found) paramValues.Add(vp.ParamID, vp.DefaultValue);
                    }
                }
            }

            return paramValues;
        }

        /// <summary>
        /// Create an AgentSetAppearance packet from Wearables data and the 
        /// Textures array and send it
        /// </summary>
        private void SendAgentSetAppearance()
        {
            AgentSetAppearancePacket set = new AgentSetAppearancePacket();
            set.AgentData.AgentID = Client.Self.AgentID;
            set.AgentData.SessionID = Client.Self.SessionID;
            set.AgentData.SerialNum = (uint)Interlocked.Increment(ref SetAppearanceSerialNum);

            // Visual params used in the agent height calculation
            float agentSizeVPHeight = 0.0f;
            float agentSizeVPHeelHeight = 0.0f;
            float agentSizeVPPlatformHeight = 0.0f;
            float agentSizeVPHeadSize = 0.5f;
            float agentSizeVPLegLength = 0.0f;
            float agentSizeVPNeckLength = 0.0f;
            float agentSizeVPHipLength = 0.0f;

            lock (Wearables)
            {
                #region VisualParam

                int vpIndex = 0;
                set.VisualParam = new AgentSetAppearancePacket.VisualParamBlock[218];

                foreach (KeyValuePair<int, VisualParam> kvp in VisualParams.Params)
                {
                    VisualParam vp = kvp.Value;
                    float paramValue = 0f;
                    bool found = false;

                    // Try and find this value in our collection of downloaded wearables
                    foreach (WearableData data in Wearables.Values)
                    {
                        if (data.Asset != null && data.Asset.Params.TryGetValue(vp.ParamID, out paramValue))
                        {
                            found = true;
                            break;
                        }
                    }

                    // Use a default value if we don't have one set for it
                    if (!found)
                        paramValue = vp.DefaultValue;

                    // Only Group-0 parameters are sent in AgentSetAppearance packets
                    if (kvp.Value.Group == 0)
                    {
                        set.VisualParam[vpIndex] = new AgentSetAppearancePacket.VisualParamBlock();
                        set.VisualParam[vpIndex].ParamValue = Utils.FloatToByte(paramValue, vp.MinValue, vp.MaxValue);
                        ++vpIndex;
                    }

                    // Check if this is one of the visual params used in the agent height calculation
                    switch (vp.ParamID)
                    {
                        case 33:
                            agentSizeVPHeight = paramValue;
                            break;
                        case 198:
                            agentSizeVPHeelHeight = paramValue;
                            break;
                        case 503:
                            agentSizeVPPlatformHeight = paramValue;
                            break;
                        case 682:
                            agentSizeVPHeadSize = paramValue;
                            break;
                        case 692:
                            agentSizeVPLegLength = paramValue;
                            break;
                        case 756:
                            agentSizeVPNeckLength = paramValue;
                            break;
                        case 842:
                            agentSizeVPHipLength = paramValue;
                            break;
                    }
                }

                #endregion VisualParam

                #region TextureEntry

                Primitive.TextureEntry te = new Primitive.TextureEntry(DEFAULT_AVATAR_TEXTURE);

                for (uint i = 0; i < Textures.Length; i++)
                {
                    if (Textures[i].TextureID != UUID.Zero)
                    {
                        Primitive.TextureEntryFace face = te.CreateFace(i);
                        face.TextureID = Textures[i].TextureID;
                        Logger.DebugLog("Sending texture entry for " + (AvatarTextureIndex)i + " to " + Textures[i].TextureID, Client);
                    }
                }

                set.ObjectData.TextureEntry = te.GetBytes();

                #endregion TextureEntry

                #region WearableData

                set.WearableData = new AgentSetAppearancePacket.WearableDataBlock[BAKED_TEXTURE_COUNT];

                // Build hashes for each of the bake layers from the individual components
                for (int bakedIndex = 0; bakedIndex < BAKED_TEXTURE_COUNT; bakedIndex++)
                {
                    UUID hash = UUID.Zero;

                    for (int wearableIndex = 0; wearableIndex < WEARABLES_PER_LAYER; wearableIndex++)
                    {
                        WearableType type = WEARABLE_BAKE_MAP[bakedIndex][wearableIndex];

                        WearableData wearable;
                        if (type != WearableType.Invalid && Wearables.TryGetValue(type, out wearable))
                            hash ^= wearable.AssetID;
                    }

                    if (hash != UUID.Zero)
                    {
                        // Hash with our magic value for this baked layer
                        hash ^= BAKED_TEXTURE_HASH[bakedIndex];
                    }

                    // Tell the server what cached texture assetID to use for each bake layer
                    set.WearableData[bakedIndex] = new AgentSetAppearancePacket.WearableDataBlock();
                    set.WearableData[bakedIndex].TextureIndex = (byte)bakedIndex;
                    set.WearableData[bakedIndex].CacheID = hash;
                    Logger.DebugLog("Sending TextureIndex " + (BakeType)bakedIndex + " with CacheID " + hash, Client);
                }

                #endregion WearableData

                #region Agent Size

                // Takes into account the Shoe Heel/Platform offsets but not the HeadSize offset. Seems to work.
                double agentSizeBase = 1.706;

                // The calculation for the HeadSize scalar may be incorrect, but it seems to work
                double agentHeight = agentSizeBase + (agentSizeVPLegLength * .1918) + (agentSizeVPHipLength * .0375) +
                    (agentSizeVPHeight * .12022) + (agentSizeVPHeadSize * .01117) + (agentSizeVPNeckLength * .038) +
                    (agentSizeVPHeelHeight * .08) + (agentSizeVPPlatformHeight * .07);

                set.AgentData.Size = new Vector3(0.45f, 0.6f, (float)agentHeight);

                #endregion Agent Size
            }

            Client.Network.SendPacket(set);
            Logger.DebugLog("Send AgentSetAppearance packet");
        }

        #endregion Appearance Helpers

        #region Inventory Helpers

        private bool GetFolderWearables(string[] folderPath, out List<InventoryWearable> wearables, out List<InventoryItem> attachments)
        {
            UUID folder = Client.Inventory.FindObjectByPath(
                Client.Inventory.Store.RootFolder.UUID, Client.Self.AgentID, String.Join("/", folderPath), INVENTORY_TIMEOUT);

            if (folder != UUID.Zero)
            {
                return GetFolderWearables(folder, out wearables, out attachments);
            }
            else
            {
                Logger.Log("Failed to resolve outfit folder path " + folderPath, Helpers.LogLevel.Error, Client);
                wearables = null;
                attachments = null;
                return false;
            }
        }

        private bool GetFolderWearables(UUID folder, out List<InventoryWearable> wearables, out List<InventoryItem> attachments)
        {
            wearables = new List<InventoryWearable>();
            attachments = new List<InventoryItem>();
            List<InventoryBase> objects = Client.Inventory.FolderContents(folder, Client.Self.AgentID, false, true,
                InventorySortOrder.ByName, INVENTORY_TIMEOUT);

            if (objects != null)
            {
                foreach (InventoryBase ib in objects)
                {
                    if (ib is InventoryWearable)
                    {
                        Logger.DebugLog("Adding wearable " + ib.Name, Client);
                        wearables.Add((InventoryWearable)ib);
                    }
                    else if (ib is InventoryAttachment)
                    {
                        Logger.DebugLog("Adding attachment (attachment) " + ib.Name, Client);
                        attachments.Add((InventoryItem)ib);
                    }
                    else if (ib is InventoryObject)
                    {
                        Logger.DebugLog("Adding attachment (object) " + ib.Name, Client);
                        attachments.Add((InventoryItem)ib);
                    }
                    else
                    {
                        Logger.DebugLog("Ignoring inventory item " + ib.Name, Client);
                    }
                }
            }
            else
            {
                Logger.Log("Failed to download folder contents of + " + folder, Helpers.LogLevel.Error, Client);
                return false;
            }

            return true;
        }

        #endregion Inventory Helpers

        #region Callbacks

        private void AgentWearablesUpdateHandler(Packet packet, Simulator simulator)
        {
            bool changed = false;
            AgentWearablesUpdatePacket update = (AgentWearablesUpdatePacket)packet;

            lock (Wearables)
            {
                #region Test if anything changed in this update

                for (int i = 0; i < update.WearableData.Length; i++)
                {
                    AgentWearablesUpdatePacket.WearableDataBlock block = update.WearableData[i];

                    if (block.AssetID != UUID.Zero)
                    {
                        WearableData wearable;
                        if (Wearables.TryGetValue((WearableType)block.WearableType, out wearable))
                        {
                            if (wearable.AssetID != block.AssetID || wearable.ItemID != block.ItemID)
                            {
                                // A different wearable is now set for this index
                                changed = true;
                                break;
                            }
                        }
                        else
                        {
                            // A wearable is now set for this index
                            changed = true;
                            break;
                        }
                    }
                    else if (Wearables.ContainsKey((WearableType)block.WearableType))
                    {
                        // This index is now empty
                        changed = true;
                        break;
                    }
                }

                #endregion Test if anything changed in this update

                if (changed)
                {
                    Logger.DebugLog("New wearables received in AgentWearablesUpdate");
                    Wearables.Clear();

                    for (int i = 0; i < update.WearableData.Length; i++)
                    {
                        AgentWearablesUpdatePacket.WearableDataBlock block = update.WearableData[i];

                        if (block.AssetID != UUID.Zero)
                        {
                            WearableType type = (WearableType)block.WearableType;

                            WearableData data = new WearableData();
                            data.Asset = null;
                            data.AssetID = block.AssetID;
                            data.AssetType = WearableTypeToAssetType(type);
                            data.ItemID = block.ItemID;
                            data.WearableType = type;

                            // Add this wearable to our collection
                            Wearables[type] = data;
                        }
                    }
                }
                else
                {
                    Logger.DebugLog("Duplicate AgentWearablesUpdate received, discarding");
                }
            }

            if (changed)
            {
                // Fire the callback
                AgentWearablesCallback callback = OnAgentWearables;
                if (callback != null)
                {
                    try { callback(); }
                    catch (Exception ex) { Logger.Log(ex.Message, Helpers.LogLevel.Error, Client, ex); }
                }
            }
        }

        private void AgentCachedTextureResponseHandler(Packet packet, Simulator simulator)
        {
            AgentCachedTextureResponsePacket response = (AgentCachedTextureResponsePacket)packet;

            for (int i = 0; i < response.WearableData.Length; i++)
            {
                AgentCachedTextureResponsePacket.WearableDataBlock block = response.WearableData[i];
                BakeType bakeType = (BakeType)block.TextureIndex;
                AvatarTextureIndex index = BakeTypeToAgentTextureIndex(bakeType);

                Logger.DebugLog("Cache response for " + bakeType + ", TextureID=" + block.TextureID, Client);

                if (block.TextureID != UUID.Zero)
                {
                    // A simulator has a cache of this bake layer

                    // FIXME: Use this. Right now we don't bother to check if this is a foreign host
                    string host = Utils.BytesToString(block.HostName);

                    Textures[(int)index].TextureID = block.TextureID;
                }
                else
                {
                    // The server does not have a cache of this bake layer
                    // FIXME:
                }
            }

            AgentCachedBakesCallback callback = OnAgentCachedBakes;
            if (callback != null)
            {
                try { callback(); }
                catch (Exception ex) { Logger.Log(ex.Message, Helpers.LogLevel.Error, Client, ex); }
            }
        }

        private void Network_OnEventQueueRunning(Simulator simulator)
        {
            if (simulator == Client.Network.CurrentSim && Client.Settings.SEND_AGENT_APPEARANCE)
            {
                // Update appearance each time we enter a new sim and capabilities have been retrieved
                Client.Appearance.RequestSetAppearance();
            }
        }

        #endregion Callbacks

        #region Static Helpers

        /// <summary>
        /// Converts a WearableType to a bodypart or clothing WearableType
        /// </summary>
        /// <param name="type">A WearableType</param>
        /// <returns>AssetType.Bodypart or AssetType.Clothing or AssetType.Unknown</returns>
        public static AssetType WearableTypeToAssetType(WearableType type)
        {
            switch (type)
            {
                case WearableType.Shape:
                case WearableType.Skin:
                case WearableType.Hair:
                case WearableType.Eyes:
                    return AssetType.Bodypart;
                case WearableType.Shirt:
                case WearableType.Pants:
                case WearableType.Shoes:
                case WearableType.Socks:
                case WearableType.Jacket:
                case WearableType.Gloves:
                case WearableType.Undershirt:
                case WearableType.Underpants:
                case WearableType.Skirt:
                    return AssetType.Clothing;
                default:
                    return AssetType.Unknown;
            }
        }

        /// <summary>
        /// Converts a BakeType to the corresponding baked texture slot in AvatarTextureIndex
        /// </summary>
        /// <param name="index">A BakeType</param>
        /// <returns>The AvatarTextureIndex slot that holds the given BakeType</returns>
        public static AvatarTextureIndex BakeTypeToAgentTextureIndex(BakeType index)
        {
            switch (index)
            {
                case BakeType.Head:
                    return AvatarTextureIndex.HeadBaked;
                case BakeType.UpperBody:
                    return AvatarTextureIndex.UpperBaked;
                case BakeType.LowerBody:
                    return AvatarTextureIndex.LowerBaked;
                case BakeType.Eyes:
                    return AvatarTextureIndex.EyesBaked;
                case BakeType.Skirt:
                    return AvatarTextureIndex.SkirtBaked;
                case BakeType.Hair:
                    return AvatarTextureIndex.HairBaked;
                default:
                    return AvatarTextureIndex.Unknown;
            }
        }

        /// <summary>
        /// Converts a BakeType to a list of the texture slots that make up that bake
        /// </summary>
        /// <param name="bakeType">A BakeType</param>
        /// <returns>A list of texture slots that are inputs for the given bake</returns>
        public static List<AvatarTextureIndex> BakeTypeToTextures(BakeType bakeType)
        {
            List<AvatarTextureIndex> textures = new List<AvatarTextureIndex>();

            switch (bakeType)
            {
                case BakeType.Head:
                    textures.Add(AvatarTextureIndex.HeadBodypaint);
                    //AddTextureDownload(AvatarTextureIndex.Hair, textures);
                    break;
                case BakeType.UpperBody:
                    textures.Add(AvatarTextureIndex.UpperBodypaint);
                    textures.Add(AvatarTextureIndex.UpperGloves);
                    textures.Add(AvatarTextureIndex.UpperUndershirt);
                    textures.Add(AvatarTextureIndex.UpperShirt);
                    textures.Add(AvatarTextureIndex.UpperJacket);
                    break;
                case BakeType.LowerBody:
                    textures.Add(AvatarTextureIndex.LowerBodypaint);
                    textures.Add(AvatarTextureIndex.LowerUnderpants);
                    textures.Add(AvatarTextureIndex.LowerSocks);
                    textures.Add(AvatarTextureIndex.LowerShoes);
                    textures.Add(AvatarTextureIndex.LowerPants);
                    textures.Add(AvatarTextureIndex.LowerJacket);
                    break;
                case BakeType.Eyes:
                    textures.Add(AvatarTextureIndex.EyesIris);
                    break;
                case BakeType.Skirt:
                    textures.Add(AvatarTextureIndex.Skirt);
                    break;
                case BakeType.Hair:
                    textures.Add(AvatarTextureIndex.Hair);
                    break;
            }

            return textures;
        }

        #endregion Static Helpers
    }
}

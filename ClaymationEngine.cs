using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using System.Collections.Generic;

namespace ClayFormer
{
    public class ClaymationEngine
    {
        private ICoreClientAPI capi;
        private BlockEntityClayForm clayForm;
        private long timerId;
        private bool isActive = false;
        private float timeSincePausedMessage = 11f;
        private int lastKnownToolMode = -1;

        private Queue<Vec3i> voxelDifferences;
        private float lastVoxelCheckTime = 0f;
        private const float VOXEL_CHECK_INTERVAL = 0.5f; 
        private const int MAX_ACTIONS_PER_TICK = 4; 
        private const int TICK_INTERVAL_MS = 50; 

        public ClaymationEngine(ICoreClientAPI api, BlockEntityClayForm form)
        {
            this.capi = api;
            this.clayForm = form;
            this.voxelDifferences = new Queue<Vec3i>();
        }

        public void Start()
        {
            if (isActive) return;
            isActive = true;
            lastKnownToolMode = -1;
            lastVoxelCheckTime = 0f;
            voxelDifferences.Clear();

            timerId = capi.World.RegisterGameTickListener(OnGameTick, TICK_INTERVAL_MS);
            capi.ShowChatMessage(Lang.Get("clayformer:msg-started"));
        }

        public void Stop()
        {
            if (isActive)
            {
                isActive = false;
                capi.World.UnregisterGameTickListener(timerId);
                voxelDifferences.Clear();
                ClayFormerMod.UnregisterEngine(clayForm.Pos);
            }
        }

        private void OnGameTick(float dt)
        {
            var currentBlockEntity = capi.World.BlockAccessor.GetBlockEntity(clayForm.Pos);
            if (currentBlockEntity == null || !(currentBlockEntity is BlockEntityClayForm))
            {
                capi.ShowChatMessage(Lang.Get("clayformer:msg-success"));
                Stop();
                return;
            }

            clayForm = (BlockEntityClayForm)currentBlockEntity;

            if (clayForm.SelectedRecipe == null)
            {
                return;
            }

            timeSincePausedMessage += dt;

            ItemSlot activeSlot = capi.World.Player.InventoryManager.ActiveHotbarSlot;
            if (activeSlot.Empty || !activeSlot.Itemstack.Collectible.Code.Path.Contains("clay"))
            {
                if (timeSincePausedMessage > 10f)
                {
                    capi.ShowChatMessage(Lang.Get("clayformer:msg-paused"));
                    timeSincePausedMessage = 0f;
                }
                return;
            }

            if (lastKnownToolMode != 0)
            {
                SetToolMode(0);
            }

            lastVoxelCheckTime += dt;
            if (lastVoxelCheckTime >= VOXEL_CHECK_INTERVAL || voxelDifferences.Count == 0)
            {
                UpdateVoxelDifferences();
                lastVoxelCheckTime = 0f;
            }

            int actionsThisTick = 0;
            while (voxelDifferences.Count > 0 && actionsThisTick < MAX_ACTIONS_PER_TICK)
            {
                var voxelPos = voxelDifferences.Dequeue();
                if (clayForm.Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] !=
                    clayForm.SelectedRecipe.Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z])
                {
                    SendVoxelUpdate(voxelPos);
                    actionsThisTick++;
                }
            }

            if (voxelDifferences.Count == 0 && IsRecipeComplete())
            {
                capi.ShowChatMessage(Lang.Get("clayformer:msg-success"));
                Stop();
            }
        }

        private void UpdateVoxelDifferences()
        {
            voxelDifferences.Clear();

            if (clayForm.SelectedRecipe == null || clayForm.SelectedRecipe.Voxels == null)
                return;

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (clayForm.Voxels[x, y, z] != clayForm.SelectedRecipe.Voxels[x, y, z])
                        {
                            voxelDifferences.Enqueue(new Vec3i(x, y, z));
                        }
                    }
                }
            }
        }

        private bool IsRecipeComplete()
        {
            if (clayForm.SelectedRecipe == null || clayForm.SelectedRecipe.Voxels == null)
                return false;

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (clayForm.Voxels[x, y, z] != clayForm.SelectedRecipe.Voxels[x, y, z])
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private void SendVoxelUpdate(Vec3i voxelPos)
        {
            bool shouldPlace = clayForm.SelectedRecipe.Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z];
            bool currentlyPlaced = clayForm.Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z];

            if (shouldPlace == currentlyPlaced) return;

            BlockFacing facing = GetOptimalFacing(voxelPos);

            if (shouldPlace && !currentlyPlaced)
            {
                clayForm.OnUseOver(capi.World.Player, voxelPos, facing, false);
            }
            else if (!shouldPlace && currentlyPlaced)
            {
                clayForm.OnUseOver(capi.World.Player, voxelPos, facing, true);
            }
        }

        private BlockFacing GetOptimalFacing(Vec3i voxelPos)
        {
            if (voxelPos.Y < 8) return BlockFacing.UP;
            if (voxelPos.Y > 8) return BlockFacing.DOWN;
            if (voxelPos.X < 8) return BlockFacing.EAST;
            if (voxelPos.X > 8) return BlockFacing.WEST;
            if (voxelPos.Z < 8) return BlockFacing.SOUTH;
            return BlockFacing.NORTH;
        }

        private void SetToolMode(int mode)
        {
            ItemSlot slot = capi.World.Player.InventoryManager.ActiveHotbarSlot;
            if (slot.Empty) return;

            int currentMode = slot.Itemstack.Attributes.GetInt("toolMode", -1);
            if (currentMode == mode)
            {
                lastKnownToolMode = mode;
                return;
            }

            slot.Itemstack.Attributes.SetInt("toolMode", mode);

            if (slot.Itemstack.Collectible != null)
            {
                slot.Itemstack.Collectible.SetToolMode(
                    slot,
                    capi.World.Player,
                    new BlockSelection { Position = this.clayForm.Pos },
                    mode
                );
            }

            var packet = new Packet_Client
            {
                Id = 27,
                ToolMode = new Packet_ToolMode
                {
                    Mode = mode,
                    X = clayForm.Pos.X,
                    Y = clayForm.Pos.Y,
                    Z = clayForm.Pos.Z
                }
            };
            capi.Network.SendPacketClient(packet);

            slot.MarkDirty();
            lastKnownToolMode = mode;
        }
    }
}

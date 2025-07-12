using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

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

        public ClaymationEngine(ICoreClientAPI api, BlockEntityClayForm form)
        {
            this.capi = api;
            this.clayForm = form;
        }

        public void Start()
        {
            if (isActive) return;
            isActive = true;
            lastKnownToolMode = -1;

            timerId = capi.World.RegisterGameTickListener(OnGameTick, 0);
            capi.ShowChatMessage(Lang.Get("clayformer:msg-started"));
        }

        public void Stop()
        {
            if (isActive)
            {
                isActive = false;
                capi.World.UnregisterGameTickListener(timerId);
            }
        }

        private void OnGameTick(float dt)
        {
            var currentBlockEntity = capi.World.BlockAccessor.GetBlockEntity(clayForm.Pos);
            if (currentBlockEntity == null || currentBlockEntity.GetType() != typeof(BlockEntityClayForm))
            {
                capi.ShowChatMessage(Lang.Get("clayformer:msg-success"));
                Stop();
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

            int actionsThisTick = 0;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (clayForm.Voxels[x, y, z] != clayForm.SelectedRecipe.Voxels[x, y, z])
                        {
                            var voxelPos = new Vec3i(x, y, z);
                            clayForm.SendUseOverPacket(capi.World.Player, voxelPos, BlockFacing.NORTH, clayForm.Voxels[x, y, z]);
                            actionsThisTick++;
                            if (actionsThisTick >= 16)
                            {
                                return; 
                            }
                        }
                    }
                }
            }

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (clayForm.Voxels[x, y, z] != clayForm.SelectedRecipe.Voxels[x, y, z])
                        {
                            var voxelPos = new Vec3i(x, y, z);
                            clayForm.SendUseOverPacket(capi.World.Player, voxelPos, BlockFacing.NORTH, clayForm.Voxels[x, y, z]);
                            return; 
                        }
                    }
                }
            }

            capi.ShowChatMessage(Lang.Get("clayformer:msg-success"));
            Stop();
        }

        private void SetToolMode(int mode)
        {
            ItemSlot slot = capi.World.Player.InventoryManager.ActiveHotbarSlot;
            if (slot.Empty) return;

            slot.Itemstack.Attributes.SetInt("toolMode", mode);
            slot.Itemstack.Collectible.SetToolMode(slot, capi.World.Player, new BlockSelection { Position = this.clayForm.Pos }, mode);

            var packet = new Packet_Client
            {
                Id = 27,
                ToolMode = new Packet_ToolMode { Mode = mode, X = clayForm.Pos.X, Y = clayForm.Pos.Y, Z = clayForm.Pos.Z }
            };
            capi.Network.SendPacketClient(packet);

            slot.MarkDirty();
            lastKnownToolMode = mode;
        }
    }
}

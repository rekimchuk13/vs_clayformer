using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClayFormer
{
    public struct ClayAction
    {
        public Vec3i Position;
        public int ToolMode;
        public bool IsRemoving;

        public ClayAction(Vec3i pos, int mode, bool removing)
        {
            Position = pos;
            ToolMode = mode;
            IsRemoving = removing;
        }
    }

    public static class RecipeCache
    {
        private static Dictionary<string, List<ClayAction>> cache = new Dictionary<string, List<ClayAction>>();

        public static bool TryGetRecipe(string recipeCode, out List<ClayAction> actions)
        {
            return cache.TryGetValue(recipeCode, out actions);
        }

        public static void SaveRecipe(string recipeCode, List<ClayAction> actions)
        {
            cache[recipeCode] = new List<ClayAction>(actions);
        }

        public static void Clear()
        {
            cache.Clear();
        }
    }

    public class ClaymationEngine
    {
        private ICoreClientAPI capi;
        private BlockEntityClayForm clayForm;
        private long timerId;
        private bool isActive = false;
        private float timeSincePausedMessage = 11f;
        private int lastKnownToolMode = -1;

        private Queue<ClayAction> actionQueue;
        private List<ClayAction> executedActions;
        private int currentLayer = -1;
        private string currentRecipeCode;
        private const int TICK_INTERVAL_MS = 50;
        private const int MAX_ACTIONS_PER_TICK = 2;

        private bool isCorrectingLayer = false;

        public ClaymationEngine(ICoreClientAPI api, BlockEntityClayForm form)
        {
            this.capi = api;
            this.clayForm = form;
            this.actionQueue = new Queue<ClayAction>();
            this.executedActions = new List<ClayAction>();
        }

        public void Start()
        {
            if (isActive) return;
            isActive = true;
            lastKnownToolMode = -1;
            currentLayer = -1;
            actionQueue.Clear();
            executedActions.Clear();

            currentRecipeCode = GetRecipeHash(clayForm.SelectedRecipe);

            if (RecipeCache.TryGetRecipe(currentRecipeCode, out var cachedActions))
            {
                foreach (var action in cachedActions)
                {
                    actionQueue.Enqueue(action);
                }
                capi.ShowChatMessage(Lang.Get("clayformer:msg-started") + " (cached)");
            }
            else
            {
                capi.ShowChatMessage(Lang.Get("clayformer:msg-started") + " (calculating...)");
            }

            timerId = capi.World.RegisterGameTickListener(OnGameTick, TICK_INTERVAL_MS);
        }

        private string GetRecipeHash(ClayFormingRecipe recipe)
        {
            if (recipe == null || recipe.Voxels == null) return "unknown";

            int hash = 17;
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (recipe.Voxels[x, y, z])
                        {
                            hash = hash * 31 + (x * 256 + y * 16 + z);
                        }
                    }
                }
            }
            return hash.ToString();
        }

        public void Stop()
        {
            if (isActive)
            {
                isActive = false;
                capi.World.UnregisterGameTickListener(timerId);
                actionQueue.Clear();
                executedActions.Clear();
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

            if (actionQueue.Count == 0)
            {
                if (isCorrectingLayer)
                {
                    isCorrectingLayer = false;

                    if (!IsLayerComplete(currentLayer))
                    {
                        PlanLayerCorrection(currentLayer);
                        isCorrectingLayer = true;
                        return;
                    }
                }

                int nextLayer = GetNextUnfinishedLayer();
                if (nextLayer == -1)
                {
                    if (!RecipeCache.TryGetRecipe(currentRecipeCode, out _) && executedActions.Count > 0)
                    {
                        RecipeCache.SaveRecipe(currentRecipeCode, executedActions);
                    }

                    capi.ShowChatMessage(Lang.Get("clayformer:msg-success"));
                    Stop();
                    return;
                }

                if (nextLayer != currentLayer)
                {
                    currentLayer = nextLayer;
                    PlanLayer(currentLayer);
                }
            }

            int actionsThisTick = 0;
            while (actionQueue.Count > 0 && actionsThisTick < MAX_ACTIONS_PER_TICK)
            {
                var action = actionQueue.Dequeue();

                bool currentState = clayForm.Voxels[action.Position.X, action.Position.Y, action.Position.Z];
                bool targetState = clayForm.SelectedRecipe.Voxels[action.Position.X, action.Position.Y, action.Position.Z];

                if (currentState != targetState)
                {
                    ExecuteAction(action);
                    executedActions.Add(action);
                    actionsThisTick++;
                }
            }
        }

        private bool IsLayerComplete(int layer)
        {
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    bool current = clayForm.Voxels[x, layer, z];
                    bool target = clayForm.SelectedRecipe.Voxels[x, layer, z];
                    if (current != target)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void PlanLayerCorrection(int layer)
        {
            var changes = GetLayerChanges(layer);

            foreach (var change in changes)
            {
                actionQueue.Enqueue(new ClayAction(change.Key, 0, !change.Value));
            }
        }

        private void PlanLayer(int layer)
        {
            var changes = GetLayerChanges(layer);
            if (changes.Count == 0) return;

            var toAdd = changes.Where(c => c.Value).Select(c => c.Key).ToList();
            var toRemove = changes.Where(c => !c.Value).Select(c => c.Key).ToList();

            PlanRemovalActions(toRemove);
            PlanAdditionActions(toAdd);

            isCorrectingLayer = true;
        }

        private void PlanRemovalActions(List<Vec3i> positions)
        {
            if (positions.Count == 0) return;

            var remaining = new HashSet<Vec3i>(positions);

            while (remaining.Count > 0)
            {
                Vec3i bestPos = Vec3i.Zero;
                int bestToolMode = 0;
                List<Vec3i> bestCovered = null;
                float bestEfficiency = 0;

                foreach (var pos in remaining)
                {
                    var result3x3 = EvaluateTool3x3ForRemoval(pos, remaining);
                    if (result3x3.efficiency > bestEfficiency)
                    {
                        bestPos = pos;
                        bestToolMode = 2;
                        bestCovered = result3x3.covered;
                        bestEfficiency = result3x3.efficiency;
                    }

                    var result2x2 = EvaluateTool2x2ForRemoval(pos, remaining);
                    if (result2x2.efficiency > bestEfficiency)
                    {
                        bestPos = pos;
                        bestToolMode = 1;
                        bestCovered = result2x2.covered;
                        bestEfficiency = result2x2.efficiency;
                    }
                }

                if (bestEfficiency > 0.3f && bestCovered != null && bestCovered.Count > 1)
                {
                    actionQueue.Enqueue(new ClayAction(bestPos, bestToolMode, true));
                    foreach (var p in bestCovered)
                        remaining.Remove(p);
                }
                else
                {
                    var pos = remaining.First();
                    actionQueue.Enqueue(new ClayAction(pos, 0, true));
                    remaining.Remove(pos);
                }
            }
        }

        private void PlanAdditionActions(List<Vec3i> positions)
        {
            if (positions.Count == 0) return;

            var remaining = new HashSet<Vec3i>(positions);

            if (currentLayer > 0)
            {
                TryLayerCopyForAdditions(remaining);
            }

            while (remaining.Count > 0)
            {
                Vec3i bestPos = Vec3i.Zero;
                int bestToolMode = 0;
                List<Vec3i> bestCovered = null;
                float bestEfficiency = 0;

                foreach (var pos in remaining)
                {
                    var result3x3 = EvaluateTool3x3ForAddition(pos, remaining);
                    if (result3x3.efficiency > bestEfficiency)
                    {
                        bestPos = pos;
                        bestToolMode = 2;
                        bestCovered = result3x3.covered;
                        bestEfficiency = result3x3.efficiency;
                    }

                    var result2x2 = EvaluateTool2x2ForAddition(pos, remaining);
                    if (result2x2.efficiency > bestEfficiency)
                    {
                        bestPos = pos;
                        bestToolMode = 1;
                        bestCovered = result2x2.covered;
                        bestEfficiency = result2x2.efficiency;
                    }
                }

                if (bestEfficiency > 0.3f && bestCovered != null && bestCovered.Count > 1)
                {
                    actionQueue.Enqueue(new ClayAction(bestPos, bestToolMode, false));
                    foreach (var p in bestCovered)
                        remaining.Remove(p);
                }
                else
                {
                    var pos = remaining.First();
                    actionQueue.Enqueue(new ClayAction(pos, 0, false));
                    remaining.Remove(pos);
                }
            }
        }

        private (List<Vec3i> covered, float efficiency) EvaluateTool3x3ForAddition(Vec3i center, HashSet<Vec3i> targets)
        {
            var covered = new List<Vec3i>();
            int unwantedCount = 0;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var pos = new Vec3i(center.X + dx, center.Y, center.Z + dz);

                    if (pos.X < 0 || pos.X >= 16 || pos.Z < 0 || pos.Z >= 16)
                        continue;

                    if (targets.Contains(pos))
                    {
                        covered.Add(pos);
                    }
                    else
                    {
                        bool currentState = clayForm.Voxels[pos.X, pos.Y, pos.Z];
                        bool targetState = clayForm.SelectedRecipe.Voxels[pos.X, pos.Y, pos.Z];

                        if (!currentState && !targetState)
                        {
                            unwantedCount++;
                        }
                    }
                }
            }

            float efficiency = (covered.Count - unwantedCount * 2) / 9f;

            if (unwantedCount > covered.Count)
                efficiency = 0;

            return (covered, efficiency);
        }

        private (List<Vec3i> covered, float efficiency) EvaluateTool2x2ForAddition(Vec3i topRight, HashSet<Vec3i> targets)
        {
            var covered = new List<Vec3i>();
            int unwantedCount = 0;

            for (int dx = -1; dx <= 0; dx++)
            {
                for (int dz = -1; dz <= 0; dz++)
                {
                    var pos = new Vec3i(topRight.X + dx, topRight.Y, topRight.Z + dz);

                    if (pos.X < 0 || pos.X >= 16 || pos.Z < 0 || pos.Z >= 16)
                        continue;

                    if (targets.Contains(pos))
                    {
                        covered.Add(pos);
                    }
                    else
                    {
                        bool currentState = clayForm.Voxels[pos.X, pos.Y, pos.Z];
                        bool targetState = clayForm.SelectedRecipe.Voxels[pos.X, pos.Y, pos.Z];

                        if (!currentState && !targetState)
                        {
                            unwantedCount++;
                        }
                    }
                }
            }

            float efficiency = (covered.Count - unwantedCount * 2) / 4f;

            if (unwantedCount > covered.Count)
                efficiency = 0;

            return (covered, efficiency);
        }

        private (List<Vec3i> covered, float efficiency) EvaluateTool3x3ForRemoval(Vec3i center, HashSet<Vec3i> targets)
        {
            var covered = new List<Vec3i>();
            int unwantedCount = 0;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var pos = new Vec3i(center.X + dx, center.Y, center.Z + dz);

                    if (pos.X < 0 || pos.X >= 16 || pos.Z < 0 || pos.Z >= 16)
                        continue;

                    if (targets.Contains(pos))
                    {
                        covered.Add(pos);
                    }
                    else
                    {
                        bool currentState = clayForm.Voxels[pos.X, pos.Y, pos.Z];
                        bool targetState = clayForm.SelectedRecipe.Voxels[pos.X, pos.Y, pos.Z];

                        if (currentState && targetState)
                        {
                            unwantedCount++;
                        }
                    }
                }
            }

            float efficiency = (covered.Count - unwantedCount * 2) / 9f;

            if (unwantedCount > 0)
                efficiency = 0;

            return (covered, efficiency);
        }

        private (List<Vec3i> covered, float efficiency) EvaluateTool2x2ForRemoval(Vec3i topRight, HashSet<Vec3i> targets)
        {
            var covered = new List<Vec3i>();
            int unwantedCount = 0;

            for (int dx = -1; dx <= 0; dx++)
            {
                for (int dz = -1; dz <= 0; dz++)
                {
                    var pos = new Vec3i(topRight.X + dx, topRight.Y, topRight.Z + dz);

                    if (pos.X < 0 || pos.X >= 16 || pos.Z < 0 || pos.Z >= 16)
                        continue;

                    if (targets.Contains(pos))
                    {
                        covered.Add(pos);
                    }
                    else
                    {
                        bool currentState = clayForm.Voxels[pos.X, pos.Y, pos.Z];
                        bool targetState = clayForm.SelectedRecipe.Voxels[pos.X, pos.Y, pos.Z];

                        if (currentState && targetState)
                        {
                            unwantedCount++;
                        }
                    }
                }
            }

            float efficiency = (covered.Count - unwantedCount * 2) / 4f;

            if (unwantedCount > 0)
                efficiency = 0;

            return (covered, efficiency);
        }

        private void TryLayerCopyForAdditions(HashSet<Vec3i> remaining)
        {
            if (currentLayer == 0) return;

            var positions = remaining.ToList();
            var processed = new HashSet<Vec3i>();

            foreach (var pos in positions)
            {
                if (processed.Contains(pos)) continue;

                var copyArea = new List<Vec3i>();
                int validCopyCount = 0;

                for (int dx = -1; dx <= 0; dx++)
                {
                    for (int dz = -1; dz <= 0; dz++)
                    {
                        int x = pos.X + dx;
                        int z = pos.Z + dz;

                        if (x < 0 || x >= 16 || z < 0 || z >= 16) continue;

                        var currentPos = new Vec3i(x, currentLayer, z);

                        bool hasPrevVoxel = clayForm.Voxels[x, currentLayer - 1, z];
                        bool needsVoxel = clayForm.SelectedRecipe.Voxels[x, currentLayer, z];

                        if (hasPrevVoxel && needsVoxel && remaining.Contains(currentPos))
                        {
                            validCopyCount++;
                            copyArea.Add(currentPos);
                        }
                        else if (!hasPrevVoxel && needsVoxel)
                        {
                            validCopyCount = -100;
                            break;
                        }
                    }
                    if (validCopyCount < 0) break;
                }

                if (validCopyCount >= 3 && copyArea.Count >= 3)
                {
                    actionQueue.Enqueue(new ClayAction(pos, 3, false));
                    foreach (var p in copyArea)
                    {
                        remaining.Remove(p);
                        processed.Add(p);
                    }
                }
            }
        }

        private Dictionary<Vec3i, bool> GetLayerChanges(int layer)
        {
            var changes = new Dictionary<Vec3i, bool>();

            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    bool current = clayForm.Voxels[x, layer, z];
                    bool target = clayForm.SelectedRecipe.Voxels[x, layer, z];

                    if (current != target)
                    {
                        changes[new Vec3i(x, layer, z)] = target;
                    }
                }
            }

            return changes;
        }

        private int GetNextUnfinishedLayer()
        {
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (clayForm.Voxels[x, y, z] != clayForm.SelectedRecipe.Voxels[x, y, z])
                        {
                            return y;
                        }
                    }
                }
            }
            return -1;
        }

        private void ExecuteAction(ClayAction action)
        {
            if (lastKnownToolMode != action.ToolMode)
            {
                SetToolMode(action.ToolMode);
            }

            BlockFacing facing = BlockFacing.UP;
            clayForm.OnUseOver(capi.World.Player, action.Position, facing, action.IsRemoving);
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

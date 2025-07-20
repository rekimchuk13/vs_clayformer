using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ClayFormer
{
    public class ClayFormerMod : ModSystem
    {
        private Harmony harmony;
        private static ICoreClientAPI staticCapi;
        private static Dictionary<BlockPos, ClaymationEngine> activeEngines = new Dictionary<BlockPos, ClaymationEngine>();

        public override void StartClientSide(ICoreClientAPI api)
        {
            staticCapi = api;
            harmony = new Harmony("com.rekimchuk13.clayformer");
            harmony.PatchAll();
            api.Logger.Notification("[ClayFormer] Mod loaded with Harmony patches!");
            api.Event.LeftWorld += OnLeftWorld;
        }

        private void OnLeftWorld()
        {
            foreach (var engine in activeEngines.Values)
            {
                engine?.Stop();
            }
            activeEngines.Clear();
        }

        public override void Dispose()
        {
            foreach (var engine in activeEngines.Values)
            {
                engine?.Stop();
            }
            activeEngines.Clear();

            harmony?.UnpatchAll("com.rekimchuk13.clayformer");
            staticCapi = null;
            base.Dispose();
        }

        public static ICoreClientAPI GetClientAPI()
        {
            return staticCapi;
        }

        public static void RegisterEngine(BlockPos pos, ClaymationEngine engine)
        {
            if (activeEngines.ContainsKey(pos))
            {
                activeEngines[pos]?.Stop();
            }
            activeEngines[pos] = engine;
        }

        public static void UnregisterEngine(BlockPos pos) 
        {
            if (activeEngines.ContainsKey(pos))
            {
                activeEngines[pos]?.Stop();
                activeEngines.Remove(pos);
            }
        }
    }

    [HarmonyPatch(typeof(GuiDialogBlockEntityRecipeSelector), MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof(string), typeof(ItemStack[]), typeof(Action<int>), typeof(Action), typeof(BlockPos), typeof(ICoreClientAPI) })]
    public class RecipeGuiPatch
    {
        static void Prefix(BlockPos blockEntityPos, ICoreClientAPI capi, ref Action<int> onSelectedRecipe)
        {
            Action<int> originalOnSelect = onSelectedRecipe;

            Action<int> wrappedOnSelect = (selectedIndex) =>
            {
                originalOnSelect?.Invoke(selectedIndex);
                if (capi?.World?.BlockAccessor == null) return;

                var blockEntity = capi.World.BlockAccessor.GetBlockEntity(blockEntityPos);
                if (blockEntity is BlockEntityClayForm clayForm)
                {
                    ClayFormerMod.UnregisterEngine(blockEntityPos);

                    var newEngine = new ClaymationEngine(capi, clayForm);
                    ClayFormerMod.RegisterEngine(blockEntityPos, newEngine);
                    newEngine.Start();
                }
            };

            onSelectedRecipe = wrappedOnSelect;
        }
    }

    [HarmonyPatch(typeof(BlockEntityClayForm), "OnBlockRemoved")]
    public class ClayFormRemovedPatch
    {
        static void Prefix(BlockEntityClayForm __instance)
        {
            if (__instance?.Pos != null)
            {
                ClayFormerMod.UnregisterEngine(__instance.Pos);
            }
        }
    }
}

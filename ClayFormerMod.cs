using System; 
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

        public override void StartClientSide(ICoreClientAPI api)
        {
            staticCapi = api;
            harmony = new Harmony("com.rekimchuk13.clayformer");
            harmony.PatchAll();
            api.Logger.Notification("[ClayFormer] Mod loaded with Harmony patches!");
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.rekimchuk13.clayformer");
            staticCapi = null;
            base.Dispose();
        }

        public static ICoreClientAPI GetClientAPI()
        {
            return staticCapi;
        }
    }

    [HarmonyPatch(typeof(GuiDialogBlockEntityRecipeSelector), MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof(string), typeof(ItemStack[]), typeof(Action<int>), typeof(Action), typeof(BlockPos), typeof(ICoreClientAPI) })]
    public class RecipeGuiPatch
    {
        private static ClaymationEngine currentEngine;

        static void Prefix(BlockPos blockEntityPos, ICoreClientAPI capi, ref Action<int> onSelectedRecipe)
        {
            Action<int> originalOnSelect = onSelectedRecipe;

            Action<int> wrappedOnSelect = (selectedIndex) =>
            {

                originalOnSelect(selectedIndex);

                if (capi.World.BlockAccessor.GetBlockEntity(blockEntityPos) is BlockEntityClayForm be)
                {
                    currentEngine?.Stop();
                    currentEngine = new ClaymationEngine(capi, be);
                    currentEngine.Start();
                }
            };

            onSelectedRecipe = wrappedOnSelect;
        }
    }
}

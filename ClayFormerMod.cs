using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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

    [HarmonyPatch(typeof(BlockEntityClayForm), "RegenMeshForNextLayer")]
    public class ClayFormRecipeSelectionPatch
    {
        private static int lastNotifiedRecipeId = -2;

        private static ClaymationEngine currentEngine;

        static void Postfix(BlockEntityClayForm __instance)
        {
            var capi = ClayFormerMod.GetClientAPI();
            if (capi == null) return;

            var currentRecipe = __instance.SelectedRecipe;
            if (currentRecipe == null) return;

            if (currentRecipe.RecipeId != lastNotifiedRecipeId)
            {
                lastNotifiedRecipeId = currentRecipe.RecipeId;

                currentEngine?.Stop();

                currentEngine = new ClaymationEngine(capi, __instance);
                currentEngine.Start();
            }
        }
    }
}
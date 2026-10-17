using GiddyUp;
using GiddyUpCore.Compatibility.WhatTheHack;
using GiddyUpCore.Mechanoids;
using HarmonyLib;
using Verse;

namespace GiddyUpMechanoids;

[HarmonyPatch("WhatTheHack.Recipes.Recipe_ModifyMechanoid", "IsValidPawn")]
class WTH_Recipe_ModifyMechanoid_IsValidPawn
{
    public static bool Prepare() =>
        ModSettings_GiddyUp.mechanoidsEnabled && GiddyUpCore.Compatibility.WhatTheHack.Extensions.WhatTheHackEnabled;

    static bool Prefix(RecipeWorker __instance, Pawn pawn, ref bool __result)
    {
        //Only the Giddy-Up module recipe; this used to replace the check for every What The Hack recipe
        if (__instance.recipe != GU_Mech_DefOf.GU_Mech_InstallGiddyUpModule)
            return true;
        __result = pawn.IsHacked() && !pawn.health.hediffSet.HasHediff(__instance.recipe.addsHediff);
        return false;
    }
}
using GiddyUp;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System.Reflection;
using System.Collections.Generic;
using Settings = GiddyUp.ModSettings_GiddyUp;

namespace GiddyUpCaravan.Harmony;

//This patch makes sure that mounted animals won't attempt to follow the caravan after they are parked
//TODO: There's probably a more elegant way to handle this, investigate further
[HarmonyPatch]
internal static class Patch_TradeCaravanAI
{
    private static bool Prepare()
    {
        return Settings.caravansEnabled;
    }

    //JobGiver_AIDefendEscortee has no TryGiveJob of its own, so AccessTools resolved to the base
    //JobGiver_AIDefendPawn.TryGiveJob, which also drives DefendMaster/Overseer/Self; likewise JobGiver_AIFollowPawn is
    //the base for FollowMaster/Overseer. Target the bases explicitly and leave the player's own animals alone.
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(JobGiver_AIFollowPawn), nameof(JobGiver_AIFollowPawn.TryGiveJob));
        yield return AccessTools.Method(typeof(JobGiver_AIDefendPawn), nameof(JobGiver_AIDefendPawn.TryGiveJob));
    }

    private static bool Prefix(ref Job? __result, Pawn pawn)
    {
        if (!pawn.RaceProps.Animal || pawn.Faction is { IsPlayer: true } ||
            pawn.GetExtendedPawnData().ReservedBy == null)
            return true;

        __result = null;
        return false;
    }
}

//A guard riding a pack animal shouldn't pick that animal as the carrier to escort. Give them the next closest one;
//with none left, vanilla falls back to escorting the trader (or leaving) on its own.
//Was patched by simple type name, redirected any ridden carrier to the trader, and dismounted the rider as a side effect.
[HarmonyPatch(typeof(LordToil_ExitMapAndEscortCarriers), nameof(LordToil_ExitMapAndEscortCarriers.GetClosestCarrier))]
internal static class Patch_GetClosestCarrier
{
    private static bool Prepare()
    {
        return Settings.caravansEnabled;
    }

    private static void Postfix(ref Pawn? __result, LordToil_ExitMapAndEscortCarriers __instance, Pawn closestTo)
    {
        if (__result == null)
            return;
        var riderData = closestTo.GetExtendedPawnData();
        var ownMount = riderData.Mount ?? riderData.ReservedMount;
        if (ownMount == null || __result != ownMount)
            return;

        Pawn? best = null;
        var bestDistance = 0f;
        var pawns = __instance.lord.ownedPawns;
        for (var i = 0; i < pawns.Count; i++)
        {
            var carrier = pawns[i];
            if (carrier == ownMount || carrier.GetTraderCaravanRole() != TraderCaravanRole.Carrier)
                continue;
            var distance = carrier.Position.DistanceToSquared(closestTo.Position);
            if (best == null || distance < bestDistance)
            {
                best = carrier;
                bestDistance = distance;
            }
        }
        __result = best;
    }
}
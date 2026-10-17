using HarmonyLib;
using RimWorld;
using Verse;
using System.Collections.Generic;
using GiddyUp;
using GiddyUpCore.Compatibility.WhatTheHack;
using Settings = GiddyUp.ModSettings_GiddyUp;

namespace GiddyUpMechanoids
{
    //Mounts raiders on the hacked mechs that arrived with them
    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.PostProcessSpawnedPawns))]
    public static class Patch_Raid_PostProcessSpawned
    {
        private static readonly List<Pawn> TmpMechs = [];
        private static readonly List<Pawn> TmpRiders = [];

        public static bool Prepare() => Settings.mechanoidsEnabled && Extensions.WhatTheHackEnabled;

        public static void Postfix(IncidentParms parms, List<Pawn> pawns)
        {
            if (parms == null || pawns == null || pawns.Count == 0)
                return;

            var mode = parms.raidArrivalMode;
            if (mode != PawnsArrivalModeDefOf.EdgeWalkIn && mode != PawnsArrivalModeDefOf.EdgeWalkInGroups &&
                mode != PawnsArrivalModeDefOf.EdgeWalkInDarkness)
                return;

            TmpMechs.Clear();
            TmpRiders.Clear();
            foreach (var pawn in pawns)
            {
                if (Extensions.IsHacked(pawn))
                    TmpMechs.Add(pawn);
                else if (pawn.RaceProps.Humanlike && !pawn.IsMounted())
                    TmpRiders.Add(pawn);
            }

            if (Settings.logging)
                Log.Message($"[Giddy-Up] Mech raid: {TmpMechs.Count} hacked mechs, {TmpRiders.Count} possible riders");

            var chance = Settings.mountChance / 100f;
            foreach (var mech in TmpMechs)
            {
                if (TmpRiders.Count == 0)
                    break;
                if (!Settings.MechSelectedCache.Contains(mech.def.shortHash) || !Rand.Chance(chance))
                    continue;
                if (mech.GetExtendedPawnData().ReservedBy != null)
                    continue;

                var rider = TmpRiders[0];
                TmpRiders.RemoveAt(0);

                if (!mech.health.hediffSet.HasHediff(GU_Mech_DefOf.GU_Mech_GiddyUpModule))
                    mech.health.AddHediff(GU_Mech_DefOf.GU_Mech_GiddyUpModule);

                //Was a hand-rolled copy of Mount() that missed isMounted/Rider/drawOffset and then also queued jobs
                rider.GoMount(mech, MountUtility.GiveJobMethod.Instant);
                if (Settings.logging)
                    Log.Message($"[Giddy-Up] {rider.LabelShort} mounted {mech.LabelShort}");
            }

            TmpMechs.Clear();
            TmpRiders.Clear();
        }
    }
}

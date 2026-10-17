using GiddyUpCore.Mechanoids;
using RimWorld;
using System.Collections.Generic;
using GiddyUp;
using GiddyUp.Harmony;
using Verse;
using GiddyUpCore.Compatibility.WhatTheHack;

namespace GiddyUpMechanoids
{
    public class GiddyUpMechanoids_MountingProvider : FloatMenuOptionProvider
    {
        public override bool Drafted => true;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool CanSelfTarget => false;
        public override bool RequiresManipulation => true;
        public override bool MechanoidCanDo => true;



        public override bool TargetPawnValid(Pawn targetPawn, FloatMenuContext context)
        {
            if (!ModSettings_GiddyUp.mechanoidsEnabled)
                return false;

            if (!base.TargetPawnValid(targetPawn, context))
                 return false;

            if (!targetPawn.RaceProps.IsMechanoid)
                return false;

            if (!targetPawn.IsHacked() && targetPawn.Faction != Faction.OfPlayer)
                return false;

            return true;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn target, FloatMenuContext context)
        {
            if (!ModSettings_GiddyUp.mechanoidsEnabled)
                return [];

            var pawn = context.FirstSelectedPawn;
            if (pawn == null)
                return [];

            // 1 — Is mechanoid?
            if (!target.RaceProps.IsMechanoid)
                return [];

            // 2 — Hacked?
            if (!target.IsHacked(pawn) && target.Faction != Faction.OfPlayer)
                return [];

            // 3 — Mounted turret?
            var hasMountedTurret = target.health.hediffSet.HasHediff(WhatTheHackDefOf.WTH_MountedTurret);
            if (hasMountedTurret)
                return [ new FloatMenuOption("GU_BME_Reason_Turret".Translate(), null) ];

            // 4 — What The Hack mechs need the GiddyUp module; overseer (Biotech) mechs don't.
            //     Was a boolean == comparison, which also rejected a Biotech mech that had the module.
            var needsModule = target.health.hediffSet.HasHediff(WhatTheHackDefOf.WTH_TargetingHacked) ||
                              target.health.hediffSet.HasHediff(WhatTheHackDefOf.WTH_TargetingHackedPoorly);
            if (needsModule && !target.health.hediffSet.HasHediff(GU_Mech_DefOf.GU_Mech_GiddyUpModule))
                return [ new FloatMenuOption("GU_BME_Reason_NoModule".Translate(), null)];

            // 5 — Check "mountable in mod options" for the target mech
            var allowedMount = ModSettings_GiddyUp.MechSelectedCache.Contains(target.def.shortHash);
            if (!allowedMount)
            {
                var notInOptionsList = new List<FloatMenuOption>();
                notInOptionsList.GenerateFloatMenuOption("GUC_NotInModOptions".Translate());
                return notInOptionsList;
            }

            // 6 — Activated?
            var activated = target.IsActivated();
            if (!activated)
                return [new FloatMenuOption("GU_BME_Reason_NotActivated".Translate(), null)];


            // 7 — Build float menu options via our utility
            var list = new List<FloatMenuOption>();
            try
            {
                Utility.AddMountingOptionsMech(target, pawn, list);
            }
            catch (System.Exception ex)
            {
                Log.Error("[GiddyUp-Mechs] ERROR inside AddMountingOptionsMech: " + ex);
            }

            return list;
        }

    }
}

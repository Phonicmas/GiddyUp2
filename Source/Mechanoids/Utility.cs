using GiddyUp;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using GiddyUp.Harmony;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using static GiddyUp.IsMountableUtility;

namespace GiddyUpMechanoids
{
    public static class Utility
    {
        /// <summary>
        /// Mech mountability check, kept for existing callers. Uses Core's IsMountable; mechs have no training.
        /// </summary>
        public static bool IsMountableMech(this Pawn mech, out Reason reason, Pawn rider, bool checkState = true,
            bool checkFaction = false) =>
            mech.IsMountable(out reason, rider, checkState, checkFaction, false);

        #region FloatMenu

        public static bool AddMountingOptionsMech(Pawn mech, Pawn rider, List<FloatMenuOption> opts)
        {
            if (!ModSettings_GiddyUp.mechanoidsEnabled)
                return false;

            if (mech == null || rider == null || opts == null) return false;

            var riderData = rider.GetExtendedPawnData();
            var mechData = mech.GetExtendedPawnData();

            if (riderData == null || mechData == null) return false;

            // Already mounted → provide dismount option
            if (mech == riderData.Mount)
            {
                return opts.GenerateFloatMenuOption("GUC_Dismount".Translate(), () =>
                {
                    DismountMech(rider, mech, riderData);
                });
            }

            if (!rider.IsCapableOfRiding(out var riderReason))
            {
                var text = riderReason switch
                {
                    Reason.TooYoung => "GU_TooYoung".Translate(),
                    _ => "GU_CannotRide".Translate()
                };
                return opts.GenerateFloatMenuOption(text);
            }

            //The old mech-only check cleared the reservation of any mech that was being ridden; Core's check handles mechs
            bool isMountable = mech.IsMountable(out var mechReason, rider, true, true, false);

            if (!isMountable)
            {
                string reasonText = mechReason switch
                {
                    Reason.IsBusy or Reason.MountedByAnother or Reason.IsReserved or Reason.IsBusyWithCaravan =>
                        "GUC_AnimalBusy".Translate(),
                    Reason.NotInModOptions => "GUC_NotInModOptions".Translate(),
                    Reason.IsRoped => "GUC_IsRoped".Translate(),
                    Reason.IsPoorCondition => "GUC_IsPoorCondition".Translate(),
                    Reason.TooHeavy => "GUC_TooHeavy".Translate(),
                    _ => ""
                };

                return opts.GenerateFloatMenuOption(reasonText);
            }

            string menuText = riderData.Mount == null ? "GUC_Mount".Translate() : "GUC_SwitchMount".Translate();
            
            return opts.GenerateFloatMenuOption(menuText, () =>
            {
                // Dismount old mount if present
                if (riderData.Mount != null)
                {
                    DismountMech(rider, riderData.Mount, riderData);
                }

                // Force mount new mech
                ForceMountMech(rider, mech, riderData);
            });
        }


        #endregion

        #region Mount Logic


        // TODO Why does this exist?
        public static void ForceMountMech(Pawn rider, Pawn mech, ExtendedPawnData riderData)
        {
            if (rider == null || mech == null || riderData == null)
                return;

            if (rider.Map != mech.Map)
                return;

            var mechData = mech.GetExtendedPawnData();
            if (mechData == null)
            {
                Log.Error("[GiddyUpMechanoids] mechData null");
                return;
            }

            // ------------------------------------------------------------
            // 1. Break ropes (GU does this first)
            // ------------------------------------------------------------
            var rope = mech.roping;
            if (rope != null && rope.IsRoped)
                rope.BreakAllRopes();

            // ------------------------------------------------------------
            // 2. Reserve relationship (like GU core)
            // ------------------------------------------------------------
            riderData.ReservedMount = mech;
            mechData.ReservedBy = rider;

            // ------------------------------------------------------------
            // 3. Stop mech movement only (do NOT give Mounted job)
            // ------------------------------------------------------------
            mech.pather?.StopDead();

            // DO NOT StopAll() unless absolutely necessary.
            // That can break GU's internal expectations.

            // ------------------------------------------------------------
            // 4. Inject Mount job to rider (same pattern as GoMount Inject)
            // ------------------------------------------------------------
            Job mountJob = new Job(ResourceBank.JobDefOf.Mount, mech)
            {
                count = 1
            };

            rider.jobs?.StartJob(
                mountJob,
                JobCondition.InterruptOptional,
                resumeCurJobAfterwards: false,
                cancelBusyStances: false,
                keepCarryingThingOverride: true
            );

            // ------------------------------------------------------------
            // IMPORTANT:
            // Do NOT:
            //   - Set riderData.mount
            //   - Issue Mounted job to mech
            //   - Update ExtendedDataStorage.isMounted
            //
            // GU handles all of that inside Mount()
            // ------------------------------------------------------------
        }

        public static void DismountMech(Pawn rider, Pawn mech, ExtendedPawnData riderData)
        {
            if (rider == null || mech == null || riderData == null)
                return;

            rider.Dismount(mech, riderData, ropeIfNeeded: false, waitForRider: false);

            // ------------------------------------------------------------------
            // 3. Clean up queued jobs (Mounted / Wait)
            // ------------------------------------------------------------------
            if (mech.jobs?.jobQueue != null)
            {
                for (int i = mech.jobs.jobQueue.Count - 1; i >= 0; i--)
                {
                    var elem = mech.jobs.jobQueue[i];
                    if (elem?.job != null &&
                        (elem.job.def == ResourceBank.JobDefOf.Mounted ||
                         elem.job.def == JobDefOf.Wait))
                    {
                        mech.jobs.jobQueue.Extract(elem.job);
                    }
                }
            }

            // End any active Mounted/Wait job
            if (mech.jobs?.curJob != null &&
                (mech.jobs.curJob.def == ResourceBank.JobDefOf.Mounted ||
                 mech.jobs.curJob.def == JobDefOf.Wait))
            {
                mech.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            }

            // Give mech a sane idle job
            mech.jobs?.StartJob(
                JobMaker.MakeJob(JobDefOf.Wait, mech.Position),
                JobCondition.InterruptOptional);

            // ------------------------------------------------------------------
            // 4. End rider's Mount job if still running
            // ------------------------------------------------------------------
            if (rider.jobs?.curJob != null &&
                rider.jobs.curJob.def == ResourceBank.JobDefOf.Mount &&
                rider.jobs.curJob.targetA == mech)
            {
                rider.jobs.EndCurrentJob(JobCondition.Succeeded, true);
            }
        }
        #endregion
    }
}

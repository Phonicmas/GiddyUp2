using GiddyUp;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Settings = GiddyUp.ModSettings_GiddyUp;
using static GiddyUp.IsMountableUtility;

namespace GiddyUpCaravan.Harmony;

[HarmonyPatch(typeof(TransferableOneWayWidget), nameof(TransferableOneWayWidget.DoRow))]
internal static class Patch_TransferableOneWayWidget
{
    private static bool Prepare()
    {
        return Settings.caravansEnabled;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var flag = false;
        var done = false;
        foreach (var code in instructions)
        {
            yield return code;

            if (done)
                continue;
            if (code.OperandIs(AccessTools.Method(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion),
                    new Type[] { typeof(Rect), typeof(TipSignal) })))
                flag = true;
            //Only the first TipRegion → stloc.0 (the count column). DoRow has two more such sites, so slave rows
            //used to get the selector injected twice.
            if (flag && code.opcode == OpCodes.Stloc_0)
            {
                done = true;
                //TODO: Improve this to be less fragile
                yield return new CodeInstruction(OpCodes.Ldarg_0); //Load instance
                yield return new CodeInstruction(OpCodes.Ldloc_0); //load count local variable
                yield return new CodeInstruction(OpCodes.Ldarg_1); //Load rectangle
                yield return new CodeInstruction(OpCodes.Ldarg_2); //Load trad
                yield return new CodeInstruction(OpCodes.Call,
                    typeof(Patch_TransferableOneWayWidget).GetMethod(nameof(AddMountSelector)));
                yield return new CodeInstruction(OpCodes.Stloc_0); //store count local variable
                flag = false;
            }
        }
    }

    //TransferableOneWayWidget is also used by the transporter and portal dialogs; the mount selector only belongs in
    //the caravan ones.
    private static bool InCaravanDialog() =>
        Find.WindowStack.IsOpen<Dialog_FormCaravan>() || Find.WindowStack.IsOpen<Dialog_SplitCaravan>();

    //Pawn lists are built once per widget per frame instead of once per row
    private static TransferableOneWayWidget? cachedWidget;
    private static int cachedFrame = -1;
    private static readonly List<Pawn> CachedRiders = [];
    private static readonly List<Pawn> CachedAllPawns = [];
    //Last seen CountToTransfer per row, so reservations are only touched when the player changes a selection
    private static readonly Dictionary<TransferableOneWay, int> LastCounts = new();

    public static float AddMountSelector(TransferableOneWayWidget widget, float num, Rect rect, TransferableOneWay trad)
    {
        var buttonWidth = 75f;

        if (trad.AnyThing is not Pawn pawn || !InCaravanDialog())
            return num; //not an animal, return; 

        var buttonRect = new Rect(num - buttonWidth, 0f, buttonWidth, rect.height);
        if (widget != cachedWidget)
        {
            cachedWidget = widget;
            cachedFrame = -1;
            LastCounts.Clear();
        }
        if (cachedFrame != Time.frameCount)
        {
            cachedFrame = Time.frameCount;
            CollectPawns(widget);
        }
        var pawns = CachedRiders;

        //It quacks like a duck, so it is one!
        var count = trad.CountToTransfer;
        if (!LastCounts.TryGetValue(trad, out var lastCount))
        {
            LastCounts[trad] = count;
            SetSelectedForCaravan(pawn, trad, CachedAllPawns, false);
        }
        else if (lastCount != count)
        {
            LastCounts[trad] = count;
            SetSelectedForCaravan(pawn, trad, CachedAllPawns, lastCount > 0);
        }

        if (pawn.RaceProps.Animal && pawns.Count > 0)
            HandleAnimal(num, buttonRect, pawn, pawns, trad);
        else
            return num;

        return num - (buttonWidth - 25f);
    }

    private static void CollectPawns(TransferableOneWayWidget widget)
    {
        CachedRiders.Clear();
        CachedAllPawns.Clear();
        foreach (var section in widget.sections)
        {
            var title = section.title;
            //This is mainly for mods that add new sections such as Colony Groups
            if (title == "Capture" || title == "Prisoners")
                continue;
            var animals = title == "Animals" || title == "Mechanoids";
            foreach (var tow in section.cachedTransferables)
            {
                if (tow.AnyThing is not Pawn towPawn)
                    continue;
                if (!animals)
                    CachedRiders.Add(towPawn);
                CachedAllPawns.Add(towPawn);
            }
        }
    }

    private static void SetSelectedForCaravan(Pawn pawn, TransferableOneWay trad, List<Pawn> pawns, bool wasSelected)
    {
        var pawnData = pawn.GetExtendedPawnData();
        var reservedMount = pawnData.ReservedMount;

        if (trad.CountToTransfer == 0)
        {
            pawnData.selectedForCaravan = false;
            //Only drop the links when the player deselected the pawn. Running this for every unselected row used to
            //wipe live Ride and Roll reservations just by opening the dialog.
            if (wasSelected)
            {
                if (reservedMount != null)
                    UnsetDataForRider(pawnData);
                if (pawnData.ReservedBy != null)
                    UnsetDataForMount(pawnData);
            }
        }

        if (reservedMount != null && (reservedMount.Dead || reservedMount.Downed))
            UnsetDataForRider(pawnData);

        if (trad.CountToTransfer > 0 && !pawnData.selectedForCaravan)
        {
            if (pawn.IsEverMountable())
            {
                var selectedPawn = GetFirstValidRider(pawn, pawns);
                if (selectedPawn != null)
                    SelectMountRider(pawnData, selectedPawn.GetExtendedPawnData(), pawn, selectedPawn);
            }
            else if (pawn.IsCapableOfRiding(out _))
            {
                var selectedMount = GetFirstValidMount(pawn, pawns);
                if (selectedMount != null)
                    SelectMountRider(selectedMount.GetExtendedPawnData(), pawnData, selectedMount, pawn);
            }
        }
        
        if (trad.CountToTransfer > 0)
            pawnData.selectedForCaravan = true;
    }

    private static Pawn? GetFirstValidRider(Pawn animal, List<Pawn> pawns) =>
        pawns.FirstOrDefault(x =>
        {
            if (!x.RaceProps.Humanlike)
                return false;
            
            var extendedData = x.GetExtendedPawnData();
            return x.IsCapableOfRiding(out _) && !x.IsTooHeavy(animal) &&
                   extendedData is { selectedForCaravan: true, ReservedMount: null };
        });

    private static Pawn? GetFirstValidMount(Pawn rider, List<Pawn> pawns) =>
        pawns.FirstOrDefault(x =>
        {
            if (x.RaceProps.Humanlike)
                return false;
            
            var mountData = x.GetExtendedPawnData();
            //Caravan pawns in the split dialog aren't spawned, so skip the on-map state checks for them
            return x.IsMountable(out _, rider, x.Spawned) && mountData is { selectedForCaravan: true, ReservedBy: null };
        });

    private static void UnsetDataForRider(ExtendedPawnData pawnData)
    {
        pawnData.ReservedMount.GetExtendedPawnData().ReservedBy = null;
        pawnData.ReservedMount = null;
    }

    private static void UnsetDataForMount(ExtendedPawnData pawnData)
    {
        pawnData.ReservedBy.GetExtendedPawnData().ReservedMount = null;
        pawnData.ReservedBy = null;
    }

    private static void HandleAnimal(float num, Rect buttonRect, Pawn animal, List<Pawn> pawns, TransferableOneWay trad)
    {
        var animalData = animal.GetExtendedPawnData();
        Text.Anchor = TextAnchor.MiddleLeft;

        var list = new List<FloatMenuOption>();

        string buttonText;
        bool canMount;

        if (!animalData.selectedForCaravan ||
            (!animal.IsMountable(out var reason, null) &&
             (reason == Reason.NotInModOptions || reason == Reason.NotFullyGrown)))
        {
            buttonText = "";
            canMount = false;
        }
        else
        {
            buttonText = animalData.ReservedBy != null && animalData.ReservedBy.GetExtendedPawnData().selectedForCaravan
                ? animalData.ReservedBy.Name.ToStringShort
                : "GU_Car_Set_Rider".Translate();
            canMount = true;
        }

        if (!canMount)
        {
            Widgets.ButtonText(buttonRect, buttonText, false, false, false);
        }
        else if (Widgets.ButtonText(buttonRect, buttonText, true, false, true))
        {
            var length = pawns.Count;
            for (var i = 0; i < length; i++)
            {
                var rider = pawns[i];
                if (rider.IsColonist)
                {
                    var pawnData = rider.GetExtendedPawnData();
                    if (!pawnData.selectedForCaravan)
                    {
                        list.Add(new FloatMenuOption(
                            rider.Name.ToStringShort + " (" + "GU_Car_PawnNotSelected".Translate() + ")", null,
                            MenuOptionPriority.Default, null, null, 0f, null, null));
                        continue;
                    }

                    if (pawnData.ReservedMount != null)
                        continue;
                    if (!rider.IsCapableOfRiding(out var riderReason))
                    {
                        string cannotRideReason;
                        if (riderReason == Reason.TooYoung)
                            cannotRideReason = rider.Name.ToStringShort + " (" + "GU_TooYoung".Translate() + ")";
                        else
                            cannotRideReason = rider.Name.ToStringShort + " (" + "GU_CannotRide".Translate() + ")";

                        list.Add(new FloatMenuOption(cannotRideReason, null));
                        continue;
                    }

                    if (rider.IsTooHeavy(animal))
                    {
                        list.Add(new FloatMenuOption(rider.Name.ToStringShort + " (" + "GUC_TooHeavy".Translate() + ")", null));
                        continue;
                    }

                    list.Add(new FloatMenuOption(rider.Name.ToStringShort, delegate
                    {
                        {
                            SelectMountRider(animalData, pawnData, animal, rider);
                            trad.CountToTransfer = 1;
                        }
                    }, MenuOptionPriority.High));
                }
            }

            //Selected created without rider
            list.Add(new FloatMenuOption("GU_Car_No_Rider".Translate(), delegate
            {
                {
                    ClearMountRider(animalData);
                    trad.CountToTransfer = 1;
                }
            }, MenuOptionPriority.Low));

            //Print list
            Find.WindowStack.Add(new FloatMenu(list));
        }
    }

    //[SyncMethod]
    private static void SelectMountRider(ExtendedPawnData animalData, ExtendedPawnData pawnData, Pawn animal, Pawn pawn)
    {
        if (animalData.ReservedBy != null)
            animalData.ReservedBy.GetExtendedPawnData().ReservedMount = null;

        pawnData.ReservedMount = animal;
        animalData.ReservedBy = pawn;

        animalData.selectedForCaravan = true;
    }

    //[SyncMethod]
    private static void ClearMountRider(ExtendedPawnData animalData)
    {
        if (animalData.ReservedBy != null)
        {
            var riderData = animalData.ReservedBy.GetExtendedPawnData();
            riderData.ReservedMount = null;
        }

        animalData.ReservedBy = null;
        animalData.selectedForCaravan = true;
    }
}

[HarmonyPatch(typeof(TransferableUtility), nameof(TransferableUtility.TransferAsOne))]
internal static class Patch_TransferableUtility
{
    private static bool Prepare()
    {
        return Settings.caravansEnabled;
    }

    private static bool Postfix(bool __result, Thing a, Thing b, TransferAsOneMode mode)
    {
        //Keep mounts as separate rows for caravans and pods only; trade windows used to be affected too.
        //Dialog_SplitCaravan builds its rows in Normal mode, so allow that while it is open.
        if (mode != TransferAsOneMode.PodsOrCaravanPacking &&
            !(mode == TransferAsOneMode.Normal && Find.WindowStack.IsOpen<Dialog_SplitCaravan>()))
            return __result;
        if (__result && a.def.category == ThingCategory.Pawn && b.def.category == ThingCategory.Pawn &&
            (IsMountableUtility.IsEverMountable((Pawn)a) || IsMountableUtility.IsEverMountable((Pawn)b)))
            return false;
        return __result;
    }
}
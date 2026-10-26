using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GiddyUp.Jobs;
using GiddyUpCaravan;
using GiddyUpCore.Compatibility;
using GiddyUpCore.Compatibility.WhatTheHack;
using GiddyUpMechanoids;
using GiddyUpRideAndRoll;
using RimWorld;
using Verse;

namespace GiddyUp;

[StaticConstructorOnStartup]
public static class Setup
{
    public static readonly List<ThingDef?> AllAnimals = []; //Only used during setup and for the mod options UI

    public static readonly List<ThingDef?> AllMechs = [];

    private static readonly HashSet<ushort> DefEditLedger = [];
    private static readonly HashSet<int> PatchLedger = [];

    private static readonly SimpleCurve SizeFactor =
    [
        new CurvePoint(1.2f, 0.9f),
        new CurvePoint(1.5f, 1f),
        new CurvePoint(2.4f, 1.15f),
        new CurvePoint(4f, 1.25f)
    ];

    private static readonly SimpleCurve SpeedFactor =
    [
        new CurvePoint(4.3f, 1f),
        new CurvePoint(5.8f, 1.2f),
        new CurvePoint(8f, 1.4f)
    ];

    private static readonly SimpleCurve ValueFactor =
    [
        new CurvePoint(300f, 1f),
        new CurvePoint(550f, 1.15f),
        new CurvePoint(5000f, 1.3f)
    ];

    private static readonly SimpleCurve WildnessFactor =
    [
        new CurvePoint(0.2f, 1f),
        new CurvePoint(0.6f, 0.9f),
        new CurvePoint(1f, 0.85f)
    ];

    static Setup()
    {
        var harmony = new HarmonyLib.Harmony("GiddyUp");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        JobDriver_Mounted.BuildAllowedJobsCache(ModSettings_GiddyUp.noMountedHunting);
        BuildMountCache();
        MountUtility.BuildAnimalBiomeCache();
        if (!ModSettings_GiddyUp.rideAndRollEnabled)
            RemoveRideAndRoll();
        if (!ModSettings_GiddyUp.caravansEnabled)
            RemoveCaravans();
        if(!ModSettings_GiddyUp.mechanoidsEnabled)
            RemoveMechanoids();

        ProcessPawnKinds(harmony);
        if (ModSettings_GiddyUp.disableSlavePawnColumn)
            DefDatabase<PawnTableDef>.GetNamed("Animals").columns.RemoveAll(x => x.defName == "MountableBySlaves");

        //VE Classical mod support
        var type = HarmonyLib.AccessTools.TypeByName("AnimalBehaviours.AnimalCollectionClass");
        if (type != null)
            ExtendedDataStorage.noFleeingAnimals = HarmonyLib.Traverse.Create(type).Field("nofleeing_animals")
                ?.GetValue<HashSet<Thing>>();
        CompatibilityLoader.Setup();
    }

    //Responsible for caching which animals are mounted, draw layering behavior, and calling caravan speed bonuses
    public static void BuildMountCache()
    {
        //Setup collections
        ModSettings_GiddyUp.invertMountingRules ??= [];
        ModSettings_GiddyUp.invertDrawRules ??= [];

        var animalDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(def => def.race is { Animal: true } && !def.IsCorpse).ToList();
        foreach (var def in animalDefs)
        {
            var setting = def.race.baseBodySize > ResourceBank.DefaultSizeThreshold;
            if (def.HasModExtension<NotMountable>())
                setting = false;
            else if (def.HasModExtension<Mountable>())
                setting = true;
            if (ModSettings_GiddyUp.invertMountingRules.Contains(def.defName))
                setting = !setting; //Player customization says to invert rule.

            if (setting)
            {
                ModSettings_GiddyUp.MountableCache.Add(def.shortHash);
                CalculateCaravanSpeed(def);
            }
            else
            {
                ModSettings_GiddyUp.MountableCache.Remove(def.shortHash);
            }

            //Handle the draw front/behind draw instruction cache
            setting = def.HasModExtension<DrawInFront>();
            if (ModSettings_GiddyUp.invertDrawRules.Contains(def.defName))
                setting = !setting;

            if (setting)
                ModSettings_GiddyUp.DrawRulesCache.Add(def.shortHash);
            else
                ModSettings_GiddyUp.DrawRulesCache.Remove(def.shortHash);
        }

        animalDefs.SortBy(x => x.label);
        AllAnimals.AddRange(animalDefs);

        AllMechs.AddRange(DefDatabase<ThingDef>.AllDefs.Where(x => x.race is { IsMechanoid: true } && !x.IsCorpse));

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        ModSettings_GiddyUp.mechSelector ??= [];
        foreach (var mech in AllMechs)
        {
            ModSettings_GiddyUp.mechSelector.TryAdd(mech.defName, !mech.race.IsWorkMech && mech.race.baseBodySize >= 1);
        }

        ModSettings_GiddyUp.MechSelectedCache.Clear();
        ModSettings_GiddyUp.MechSelectedCache.AddRange(Setup.AllMechs
            .Where(x => ModSettings_GiddyUp.mechSelector.TryGetValue(x.defName, out var val) && val)
            .Select(x => x.shortHash));
    }

    //Responsible for setting up the draw offsets and custom stat overrides
    public static void ProcessPawnKinds(HarmonyLib.Harmony? harmony = null)
    {
        var newEntries = false;
        var usingCustomStats = false;
        if (ModSettings_GiddyUp.offsetCache == null)
            ModSettings_GiddyUp.offsetCache = new Dictionary<string, float>();
        var list = DefDatabase<PawnKindDef>.AllDefsListForReading;
        var length = list.Count;
        for (var i = 0; i < length; i++)
        {
            var pawnKindDef = list[i];
            if (pawnKindDef.race == null)
                continue;
            if (!usingCustomStats && pawnKindDef.HasModExtension<CustomStats>())
                usingCustomStats = true;

            //Determine which life stages are considered mature enough to ride
            var lifeStages = pawnKindDef.lifeStages;
            var lifeIndexes = lifeStages?.Count;
            //Go through each life stage for this animal
            for (var lifeIndex = 0; lifeIndex < lifeIndexes; lifeIndex++)
            {
                //Convert the def and age into a key string used for storage between sessions
                var key = TextureUtility.FormatKey(pawnKindDef, lifeIndex);

                //Skip if already set
                if (ModSettings_GiddyUp.offsetCache.ContainsKey(key))
                    continue;

                //Build out...
                var age = lifeStages[lifeIndex];
                if (age == null)
                {
                    Log.Error($"Age[{lifeIndex}] for '{pawnKindDef.defName}:{pawnKindDef.modContentPack.Name}' is null");
                    continue;
                }
                var offset = TextureUtility.SetDrawOffset(age);
                if (offset == null)
                {
                    Log.Error($"Texture for '{pawnKindDef.defName}:{pawnKindDef.modContentPack.Name}' is null");
                    continue;
                }
                ModSettings_GiddyUp.offsetCache.SetOrAdd(key, offset.Value);
                newEntries = true;
            }
        }

        //Write to settings file
        if (newEntries)
            LoadedModManager.GetMod<Mod_GiddyUp>().modSettings.Write();
    }

    //TODO: It may be possible to fold this into th BuildCache method
    public static void RebuildInversions()
    {
        //Reset
        ModSettings_GiddyUp.invertMountingRules = new HashSet<string>();
        ModSettings_GiddyUp.invertDrawRules = new HashSet<string>();

        foreach (var animalDef in AllAnimals)
        {
            var hash = animalDef.shortHash;
            //Search for abnormalities, meaning the player wants to invert the rules
            if (animalDef.HasModExtension<NotMountable>())
            {
                if (ModSettings_GiddyUp.MountableCache.Contains(hash))
                    ModSettings_GiddyUp.invertMountingRules.Add(animalDef.defName);
            }
            else if (animalDef.HasModExtension<Mountable>())
            {
                if (!ModSettings_GiddyUp.MountableCache.Contains(hash))
                    ModSettings_GiddyUp.invertMountingRules.Add(animalDef.defName);
            }
            else if (animalDef.race.baseBodySize <= ResourceBank.DefaultSizeThreshold)
            {
                if (ModSettings_GiddyUp.MountableCache.Contains(hash))
                    ModSettings_GiddyUp.invertMountingRules.Add(animalDef.defName);
            }
            else
            {
                if (!ModSettings_GiddyUp.MountableCache.Contains(hash))
                    ModSettings_GiddyUp.invertMountingRules.Add(animalDef.defName);
            }

            //And now draw rules
            var drawFront = false;
            var modExt = animalDef.GetModExtension<DrawInFront>();
            if (modExt != null)
                drawFront = true;

            if (drawFront && !ModSettings_GiddyUp.DrawRulesCache.Contains(hash) || !drawFront && ModSettings_GiddyUp.DrawRulesCache.Contains(hash))
                ModSettings_GiddyUp.invertDrawRules.Add(animalDef.defName);
        }

        foreach (var mech in AllMechs)
        {
            ModSettings_GiddyUp.mechSelector[mech.defName] =
                ModSettings_GiddyUp.MechSelectedCache.Contains(mech.shortHash);
        }
    }

    private static void RemoveRideAndRoll()
    {
        //Remove jobs
        DefDatabase<JobDef>.Remove(ResourceBank.JobDefOf.WaitForRider);

        //Remove pawn columns (UI icons in the pawn table)
        DefDatabase<PawnTableDef>.GetNamed("Animals").columns.RemoveAll(x =>
            x.defName == "MountableByColonists" || x.defName == "MountableBySlaves");

        //Remove area designators
        var designationCategoryDef = DefDatabase<DesignationCategoryDef>.GetNamed("Zone");
        designationCategoryDef.specialDesignatorClasses.RemoveAll(x =>
            x == typeof(Designator_GU_DropAnimal_Expand) ||
            x == typeof(Designator_GU_DropAnimal_Clear) ||
            x == typeof(Designator_GU_NoMount_Expand) ||
            x == typeof(Designator_GU_NoMount_Clear)
        );
        var workingList = new List<Designator>(designationCategoryDef.resolvedDesignators);
        foreach (var designator in workingList)
            if (designator is Designator_GU_DropAnimal_Expand ||
                designator is Designator_GU_DropAnimal_Clear ||
                designator is Designator_GU_NoMount_Expand ||
                designator is Designator_GU_NoMount_Clear)
                designationCategoryDef.resolvedDesignators.Remove(designator);
    }

    private static void RemoveCaravans()
    {
        //Remove area designators
        var designationCategoryDef = DefDatabase<DesignationCategoryDef>.GetNamed("Zone");
        designationCategoryDef.specialDesignatorClasses.RemoveAll(x =>
            x == typeof(Designator_GU_DropAnimal_NPC_Clear) ||
            x == typeof(Designator_GU_DropAnimal_NPC_Expand)
        );
        var workingList = new List<Designator>(designationCategoryDef.resolvedDesignators);
        foreach (var designator in workingList)
            if (designator is Designator_GU_DropAnimal_NPC_Clear ||
                designator is Designator_GU_DropAnimal_NPC_Expand)
                designationCategoryDef.resolvedDesignators.Remove(designator);
    }

    private static void RemoveMechanoids()
    {
        if (Extensions.WhatTheHackEnabled)
        {

            DefDatabase<RecipeDef>.Remove(GU_Mech_DefOf.GU_Mech_InstallGiddyUpModule);

            DefDatabase<HediffDef>.Remove(GU_Mech_DefOf.GU_Mech_GiddyUpModule);
            DefDatabase<ResearchProjectDef>.Remove(
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(nameof(GU_Mech_DefOf.GU_Mech_GiddyUpModule)
                ));
            DefDatabase<ThingDef>.Remove(
                DefDatabase<ThingDef>.GetNamedSilentFail(nameof(GU_Mech_DefOf.GU_Mech_GiddyUpModule)
                ));
        }
    }

    public static void CalculateCaravanSpeed(ThingDef def, bool check = false)
    {
        //Horse		2.4 size	5.8 speed	packAnimal	550 value	0.35 wildeness	= 1.6
        //Thrumbo	4.0 size	5.5 speed	!packAnimal	4000 value	0.985 wildness	= 1.5
        //Dromedary	2.1 size	4.3 speed	packAnimal	300 value	0.25 wildeness	= 1.3

        //Muffalo	2.4 size	4.5 speed	packAnimal	300 value	0.6 wildness	= ???

        float speed;

        //This would pass if the animal has an XML-defined bonus that we didn't apply, leave it alone
        if (def.StatBaseDefined(StatDefOf.CaravanRidingSpeedFactor) && !DefEditLedger.Contains(def.shortHash))
        {
            return;
        }
        //This would pass if mod options are changed, the mount is no longer rideable, and it was once given a bonus
        else if (check && !ModSettings_GiddyUp.MountableCache.Contains(def.shortHash) && DefEditLedger.Contains(def.shortHash))
        {
            DefEditLedger.Remove(def.shortHash);
            speed = 1f;
        }
        //Give the bonus
        else if (ModSettings_GiddyUp.giveCaravanSpeed)
        {
            DefEditLedger.Add(def.shortHash);
            speed = SizeFactor.Evaluate(def.race.baseBodySize) *
                    SpeedFactor.Evaluate(def.GetStatValueAbstract(StatDefOf.MoveSpeed)) *
                    ValueFactor.Evaluate(def.BaseMarketValue) *
                    WildnessFactor.Evaluate(def.GetStatValueAbstract(StatDefOf.Wildness)) *
                    (def.race.packAnimal ? 1.1f : 0.95f);
            if (speed < 1.00001f)
                speed = 1.00001f;
        }
        //Don't give a bonus and instead just set the value to be above 1f so the game thinks it's a rideable mount on the caravan UI, but low enough to render as 100%
        else
        {
            DefEditLedger.Remove(def.shortHash);
            speed = speed = 1.00001f;
        }

        StatUtility.SetStatValueInList(ref def.statBases, StatDefOf.CaravanRidingSpeedFactor, speed);
    }
}
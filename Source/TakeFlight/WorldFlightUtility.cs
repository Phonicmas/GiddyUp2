using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using GiddyUp;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Settings = GiddyUp.ModSettings_GiddyUp;

namespace GiddyUpTakeFlight;

/// <summary>
/// Load, capacity and resulting world range for one rider on one mount, computed once and passed around.
/// </summary>
public readonly struct LoadInfo
{
    public readonly float load;
    public readonly float capacity;
    public readonly float multiplier;
    public readonly int range;

    public bool Overloaded => load > capacity;

    public LoadInfo(Pawn mount, float load, float capacity)
    {
        this.load = load;
        this.capacity = capacity;
        multiplier = WorldFlightUtility.RangeMultiplier(mount, load, capacity);
        range = load > capacity ? 0 : Mathf.FloorToInt(WorldFlightUtility.BaseWorldRange(mount) * multiplier);
    }

    public static LoadInfo For(Pawn? rider, Pawn mount) =>
        new LoadInfo(mount, WorldFlightUtility.LoadOf(rider, mount), WorldFlightUtility.CapacityOf(mount));
}

/// <summary>
/// A flying mount and its rider. Riderless flying mounts are pairs with no rider: they fly along with a group but
/// can't fly on their own.
/// </summary>
public class FlyingPair
{
    public Pawn? rider;
    public Pawn mount;
    public LoadInfo load;

    [MemberNotNullWhen(true, nameof(rider))]
    public bool HasRider => rider != null;

    public FlyingPair(Pawn? rider, Pawn mount, LoadInfo load)
    {
        this.rider = rider;
        this.mount = mount;
        this.load = load;
    }

    public FlyingPair(Pawn? rider, Pawn mount) : this(rider, mount, LoadInfo.For(rider, mount))
    {
    }

    public void RefreshLoad() => load = LoadInfo.For(rider, mount);
}

public static class WorldFlightUtility
{
    public const int CommandGroupKey = 81563201;

    private const int CaravanGizmoRefreshTicks = 30;
    private const int CaravanGizmoRefreshFrames = 60;

    private static Caravan? cachedCaravan;
    private static int cachedCaravanTick = -1;
    private static int cachedCaravanFrame = -1;
    private static int cachedCaravanPawnCount = -1;
    private static Command? cachedCaravanCommand;

    private static Map? selectedPairsMap;
    private static int selectedPairsFrame = -1;
    private static readonly List<FlyingPair> SelectedPairs = new List<FlyingPair>();

    private static readonly HashSet<Pawn> TmpTakenMounts = new HashSet<Pawn>();
    private static readonly HashSet<Pawn> TmpTakenRiders = new HashSet<Pawn>();
    private static readonly List<Pawn> TmpFreeMounts = new List<Pawn>();
    private static readonly List<float> TmpFreeCapacity = new List<float>();
    private static readonly List<float> TmpFreeInventoryMass = new List<float>();
    private static readonly HashSet<Pawn> TmpFlying = new HashSet<Pawn>();

    public static float LoadOf(Pawn? rider, Pawn mount)
    {
        var load = rider != null ? rider.GetStatValue(StatDefOf.Mass) : 0f;
        if (mount.inventory != null)
            load += MassUtility.InventoryMass(mount);
        return load;
    }

    public static float CapacityOf(Pawn mount) => mount.GetStatValue(StatDefOf.CarryingCapacity);

    public static bool IsOverloaded(Pawn rider, Pawn mount) => LoadOf(rider, mount) > CapacityOf(mount);

    public static int BaseWorldRange(Pawn mount)
    {
        var extension = FlyerUtility.ExtensionFor(mount.def);
        return extension != null && extension.worldFlightRange >= 0
            ? extension.worldFlightRange
            : Settings.worldFlightDefaultRange;
    }

    public static float RangeMultiplier(Pawn mount, float load, float capacity)
    {
        var ratio = load / Mathf.Max(capacity, 0.01f);
        var extension = FlyerUtility.ExtensionFor(mount.def);
        if (extension?.rangeByLoad != null)
            return Mathf.Max(0f, extension.rangeByLoad.Evaluate(ratio));

        var cap = extension != null && extension.maxRangeMultiplier > 0f
            ? extension.maxRangeMultiplier
            : Settings.worldFlightMaxRangeMultiplier;
        return Mathf.Clamp(1f / Mathf.Max(ratio, 0.0001f), 1f, Mathf.Max(1f, cap));
    }

    public static int WorldRangeFor(Pawn rider, Pawn mount) => LoadInfo.For(rider, mount).range;

    public static float SpeedFor(Pawn mount)
    {
        var extension = FlyerUtility.ExtensionFor(mount.def);
        return extension != null && extension.worldFlightSpeed > 0f
            ? extension.worldFlightSpeed
            : Settings.worldFlightSpeed;
    }

    /// <summary>
    /// Slowest flight speed (tiles per day) among the flying mounts in a group of pawns.
    /// </summary>
    public static float GroupSpeed(IEnumerable<Pawn> pawns)
    {
        var speed = float.MaxValue;
        foreach (var pawn in pawns)
            if (pawn != null && !pawn.RaceProps.Humanlike && FlyerUtility.IsFlyer(pawn.def))
                speed = Mathf.Min(speed, SpeedFor(pawn));
        return speed == float.MaxValue ? Settings.worldFlightSpeed : Mathf.Max(1f, speed);
    }

    public static float GroupSpeed(List<FlyingPair> pairs)
    {
        var speed = float.MaxValue;
        for (var i = 0; i < pairs.Count; i++)
            speed = Mathf.Min(speed, SpeedFor(pairs[i].mount));
        return speed == float.MaxValue ? Settings.worldFlightSpeed : Mathf.Max(1f, speed);
    }

    public static int TravelTicks(int tiles, float tilesPerDay)
    {
        return Mathf.RoundToInt(tiles / Mathf.Max(1f, tilesPerDay) * GenDate.TicksPerDay);
    }

    public static int WorldFatigueTicks(Pawn mount, int tiles, float tilesPerDay)
    {
        return FlyerUtility.CooldownTicksFor(mount) +
               Mathf.RoundToInt(Settings.worldFlightRestHoursPerTile * GenDate.TicksPerHour * tiles) +
               TravelTicks(tiles, tilesPerDay);
    }

    public static string OverloadedReason(Pawn mount, LoadInfo load)
    {
        return "GU_TF_Overloaded".Translate(mount.Named("MOUNT"), load.load.ToStringMass().Named("LOAD"),
            load.capacity.ToStringMass().Named("CAPACITY"));
    }

    public static string LoadReport(LoadInfo load)
    {
        return "GU_TF_LoadReport".Translate(load.load.ToStringMass().Named("LOAD"),
            load.capacity.ToStringMass().Named("CAPACITY"),
            load.multiplier.ToString("0.##").Named("MULT"),
            load.range.Named("RANGE")).Resolve();
    }

    public static bool CanFlyWorldFromMap(Pawn rider, Pawn mount, LoadInfo load, out string? reason)
    {
        if (!MountedFlightUtility.CanTakeOff(rider, mount, load, out reason))
            return false;
        if (rider.Position.Roofed(rider.Map))
        {
            reason = "GU_TF_RoofedTakeOff".Translate();
            return false;
        }
        if (load.range < 1)
        {
            reason = "GU_TF_NoWorldRange".Translate(mount.Named("MOUNT"));
            return false;
        }
        return true;
    }

    /// <summary>
    /// Whether a flying mount without a rider can fly along with a group leaving its map.
    /// </summary>
    public static bool CanFlyWorldFromMapRiderless(Pawn mount, LoadInfo load)
    {
        var map = mount.Map;
        if (map == null || !mount.Spawned || mount.Downed || mount.Dead || mount.InMentalState)
            return false;
        if (MountedFlightUtility.IsFatigued(mount, out _) || load.Overloaded || load.range < 1)
            return false;
        if (map.Biome != null && map.Biome.inVacuum && !mount.RaceProps.canFlyInVacuum)
            return false;
        return !mount.Position.Roofed(map);
    }

    private static bool IsBeingRidden(Pawn mount)
    {
        var rider = mount.GetExtendedPawnData()?.ReservedBy;
        return rider != null && FlyerUtility.TryGetFlyingMount(rider, out var ridden) && ridden == mount;
    }

    public static bool CanFlyWorldFromCaravan(Pawn? rider, Pawn mount, LoadInfo load, out string? reason)
    {
        reason = null;
        if (rider != null && (rider.Downed || rider.Dead || mount.Downed || mount.Dead))
        {
            reason = "GU_TF_PairDowned".Translate(rider.Named("RIDER"), mount.Named("MOUNT"));
            return false;
        }
        if (mount.Downed || mount.Dead)
        {
            reason = "GU_TF_MountDowned".Translate(mount.Named("MOUNT"));
            return false;
        }
        if (MountedFlightUtility.IsFatigued(mount, out reason))
            return false;
        if (load.Overloaded)
        {
            reason = OverloadedReason(mount, load);
            return false;
        }
        if (load.range < 1)
        {
            reason = "GU_TF_NoWorldRange".Translate(mount.Named("MOUNT"));
            return false;
        }
        return true;
    }

    public static Command MakeMapWorldFlightCommand(Pawn rider, Pawn mount, LoadInfo load)
    {
        var command = new Command_Action
        {
            defaultLabel = "GU_TF_FlyWorld".Translate(),
            defaultDesc = "GU_TF_FlyWorldDesc".Translate(mount.Named("MOUNT")) + "\n\n" + LoadReport(load),
            icon = ResourceBank.iconFlyWorld,
            groupKey = CommandGroupKey,
            alsoClickIfOtherInGroupClicked = false,
            action = () => StartMapFlight(rider.Map)
        };
        if (!CanFlyWorldFromMap(rider, mount, load, out var reason) &&
            !EligibleSelectedPairs(rider.Map).Exists(p => p.HasRider))
            command.Disable(reason);
        return command;
    }

    /// <summary>
    /// Selected riders on a flying mount that can fly right now, plus selected riderless flying mounts that can
    /// fly along. Rebuilt once per UI frame; callers that keep the result must copy it.
    /// </summary>
    private static List<FlyingPair> EligibleSelectedPairs(Map map)
    {
        if (selectedPairsFrame == RealTime.frameCount && selectedPairsMap == map)
            return SelectedPairs;
        selectedPairsFrame = RealTime.frameCount;
        selectedPairsMap = map;
        SelectedPairs.Clear();

        var selected = Find.Selector.SelectedPawns;
        for (var i = 0; i < selected.Count; i++)
        {
            var pawn = selected[i];
            if (pawn.Map != map || !pawn.IsColonistPlayerControlled)
                continue;
            if (!FlyerUtility.TryGetFlyingMount(pawn, out var mount))
                continue;
            var load = LoadInfo.For(pawn, mount);
            if (!CanFlyWorldFromMap(pawn, mount, load, out _))
                continue;
            SelectedPairs.Add(new FlyingPair(pawn, mount, load));
        }

        for (var i = 0; i < selected.Count; i++)
        {
            var mount = selected[i];
            if (mount.Map != map || mount.RaceProps.Humanlike || mount.Faction != Faction.OfPlayer ||
                !FlyerUtility.IsFlyer(mount.def) || IsBeingRidden(mount) || SelectedPairs.Exists(p => p.mount == mount))
                continue;
            var load = LoadInfo.For(null, mount);
            if (CanFlyWorldFromMapRiderless(mount, load))
                SelectedPairs.Add(new FlyingPair(null, mount, load));
        }
        return SelectedPairs;
    }

    private static void StartMapFlight(Map? map)
    {
        if (map == null)
            return;
        var pairs = new List<FlyingPair>(EligibleSelectedPairs(map));
        if (!pairs.Exists(p => p.HasRider))
            return;
        var range = MinRange(pairs);
        var pods = new List<IThingHolder> { new PawnGroupHolder(pairs) };
        StartChoosingDestination(map.Tile, pods, range, true,
            (tile, arrivalAction) => LaunchFromMap(map, pairs, tile, arrivalAction));
    }

    private static int MinRange(List<FlyingPair> pairs)
    {
        var range = int.MaxValue;
        for (var i = 0; i < pairs.Count; i++)
            range = Mathf.Min(range, pairs[i].load.range);
        return range == int.MaxValue ? 0 : range;
    }

    public static void StartChoosingDestination(PlanetTile origin, List<IThingHolder> pods, int range,
        bool closeWorldTabWhenFinished, Action<PlanetTile, TransportersArrivalAction> launchAction)
    {
        CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(origin)));
        Find.WorldSelector.ClearSelection();
        Find.WorldTargeter.BeginTargeting(target => ChoseWorldTarget(target, origin, pods, range, launchAction),
            true,
            ResourceBank.iconFlyWorld,
            closeWorldTabWhenFinished,
            () => GenDraw.DrawWorldRadiusRing(origin, range),
            target => TargetingLabel(target, origin, range),
            null,
            origin,
            true);
    }

    private static string? TargetError(GlobalTargetInfo target, PlanetTile origin, int range)
    {
        if (!target.IsValid || Find.World.Impassable(target.Tile))
            return "MessageTransportPodsDestinationIsInvalid".Translate();
        if (target.Tile.Layer != origin.Layer)
            return "GU_TF_OtherLayer".Translate();
        if (target.HasWorldObject && !target.WorldObject.def.validLaunchTarget)
            return "MessageWorldObjectIsInvalid".Translate(target.WorldObject.Named("OBJECT"));
        if (ModsConfig.OdysseyActive && target.HasWorldObject && target.WorldObject.RequiresSignalJammerToReach)
            return "TransportPodDestinationRequiresSignalJammer".Translate();
        if (Find.WorldGrid.TraversalDistanceBetween(origin, target.Tile, true, range, true) > range)
            return "TransportPodDestinationBeyondMaximumRange".Translate();
        return null;
    }

    private static TaggedString TargetingLabel(GlobalTargetInfo target, PlanetTile origin, int range)
    {
        if (!target.IsValid)
            return null;
        var error = TargetError(target, origin, range);
        if (error == null)
            return "";
        GUI.color = ColorLibrary.RedReadable;
        return error;
    }

    /// <summary>
    /// Destination options for a tile, the same ones vanilla offers transport pods (not shuttles).
    /// </summary>
    private static List<FloatMenuOption> OptionsForTile(PlanetTile tile, List<IThingHolder> pods,
        Action<PlanetTile, TransportersArrivalAction> launchAction)
    {
        var options = new List<FloatMenuOption>();
        if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, tile) &&
            !Find.WorldObjects.AnySettlementBaseAt(tile) && !Find.WorldObjects.AnySiteAt(tile))
            options.Add(new FloatMenuOption("FormCaravanHere".Translate(),
                () => launchAction(tile, new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived"))));

        var worldObjects = Find.WorldObjects.AllWorldObjects;
        for (var i = 0; i < worldObjects.Count; i++)
            if (worldObjects[i].Tile == tile)
                options.AddRange(worldObjects[i].GetTransportersFloatMenuOptions(pods, launchAction));
        return options;
    }

    private static bool ChoseWorldTarget(GlobalTargetInfo target, PlanetTile origin, List<IThingHolder> pods,
        int range, Action<PlanetTile, TransportersArrivalAction> launchAction)
    {
        var error = TargetError(target, origin, range);
        if (error != null)
        {
            Messages.Message(error, MessageTypeDefOf.RejectInput, false);
            return false;
        }

        var options = OptionsForTile(target.Tile, pods, launchAction);
        if (options.Count == 0)
        {
            Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }
        if (options.Count == 1)
        {
            if (options[0].Disabled)
                return false;
            options[0].action();
            return true;
        }
        Find.WindowStack.Add(new FloatMenu(options));
        return false;
    }

    private static void LaunchFromMap(Map map, List<FlyingPair> chosen, PlanetTile destination,
        TransportersArrivalAction arrivalAction)
    {
        var pairs = new List<FlyingPair>();
        foreach (var pair in chosen)
        {
            if (!pair.HasRider)
            {
                if (pair.mount.Map != map || IsBeingRidden(pair.mount))
                    continue;
                pair.RefreshLoad();
                if (CanFlyWorldFromMapRiderless(pair.mount, pair.load))
                    pairs.Add(pair);
                continue;
            }
            if (!pair.rider.Spawned || pair.rider.Map != map)
                continue;
            if (!FlyerUtility.TryGetFlyingMount(pair.rider, out var mount) || mount != pair.mount)
                continue;
            pair.RefreshLoad();
            if (CanFlyWorldFromMap(pair.rider, pair.mount, pair.load, out _))
                pairs.Add(pair);
        }
        var tiles = Find.WorldGrid.TraversalDistanceBetween(map.Tile, destination, true, int.MaxValue, true);
        pairs.RemoveAll(p => p.load.range < tiles);
        if (!pairs.Exists(p => p.HasRider))
            return;

        var speed = GroupSpeed(pairs);
        var groupID = Find.UniqueIDsManager.GetNextTransporterGroupID();
        MountedFlightUtility.Launching = true;
        try
        {
            foreach (var pair in pairs)
                LaunchPairFromMap(map, pair, groupID, destination, arrivalAction, tiles, speed);
        }
        finally
        {
            MountedFlightUtility.Launching = false;
        }
        CameraJumper.TryHideWorld();
    }

    private static void LaunchPairFromMap(Map map, FlyingPair pair, int groupID, PlanetTile destination,
        TransportersArrivalAction arrivalAction, int tiles, float speed)
    {
        var rider = pair.rider;
        var mount = pair.mount;
        var cell = rider?.Position ?? mount.Position;

        LifeStageUtility.PlayNearestLifestageSound(mount, ls => ls.soundCall, g => g.soundCall, m => m.soundCall);
        FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), map, Mathf.Clamp(mount.BodySize, 1.5f, 4f));

        if (rider != null)
            rider.Dismount(mount, rider.GetExtendedPawnData(), false, default, false, false);
        mount.jobs?.StopAll();
        rider?.jobs?.StopAll();

        var info = CreateTransporterInfo();
        if (mount.Spawned)
            mount.DeSpawn();
        info.innerContainer.TryAdd(mount);
        if (rider != null)
        {
            rider.Rotation = mount.Rotation;
            if (rider.Spawned)
                rider.DeSpawn();
            info.innerContainer.TryAdd(rider);
        }

        var active = (ActiveTransporter)ThingMaker.MakeThing(GU_TF_DefOf.GU_TF_MountedFlyerActive);
        active.Contents = info;
        var leaving = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(GU_TF_DefOf.GU_TF_MountedFlyerLeaving, active);
        leaving.groupID = groupID;
        leaving.destinationTile = destination;
        leaving.arrivalAction = arrivalAction;
        leaving.worldObjectDef = GU_TF_DefOf.GU_TF_TravellingMountedFlyers;
        GenSpawn.Spawn(leaving, cell, map);

        MountedFlightUtility.ApplyFatigue(mount, WorldFatigueTicks(mount, tiles, speed));
    }

    private static ActiveTransporterInfo CreateTransporterInfo()
    {
        return new ActiveTransporterInfo
        {
            openDelay = 0,
            sentTransporterDef = GU_TF_DefOf.GU_TF_MountedFlyerTransporter
        };
    }

    public static bool IsOurTransporter(ActiveTransporterInfo? info) =>
        info != null && info.sentTransporterDef == GU_TF_DefOf.GU_TF_MountedFlyerTransporter;

    /// <summary>
    /// The caravan's world flight gizmo, or null when the caravan has no flying mount. Rebuilt only when the
    /// caravan, its pawn count, or enough time has changed, since building it evaluates stats for every pair.
    /// </summary>
    public static Command? CaravanWorldFlightGizmo(Caravan caravan)
    {
        var tick = Find.TickManager.TicksGame;
        var frame = RealTime.frameCount;
        var pawnCount = caravan.PawnsListForReading.Count;
        if (caravan != cachedCaravan || pawnCount != cachedCaravanPawnCount ||
            tick < cachedCaravanTick || tick - cachedCaravanTick >= CaravanGizmoRefreshTicks ||
            frame - cachedCaravanFrame >= CaravanGizmoRefreshFrames)
        {
            cachedCaravan = caravan;
            cachedCaravanPawnCount = pawnCount;
            cachedCaravanTick = tick;
            cachedCaravanFrame = frame;
            cachedCaravanCommand = BuildCaravanGizmo(caravan);
        }
        return cachedCaravanCommand;
    }

    private static Command? BuildCaravanGizmo(Caravan caravan)
    {
        var pairs = CaravanPairs(caravan);
        if (pairs.Exists(p => p.HasRider))
            return MakeCaravanWorldFlightCommand(caravan, pairs);
        return CaravanHasFlyingMount(caravan) ? MakeCaravanNoRiderCommand() : null;
    }

    /// <summary>
    /// Riders in the caravan paired with a flying mount, eligible or not. Uses Giddy-Up's reserved mount first, then
    /// pairs remaining riders with unreserved flying mounts in the caravan. Flying mounts left over are added
    /// without a rider.
    /// </summary>
    public static List<FlyingPair> CaravanPairs(Caravan caravan)
    {
        var pairs = new List<FlyingPair>();
        var pawns = caravan.PawnsListForReading;
        TmpTakenMounts.Clear();
        TmpTakenRiders.Clear();
        TmpFreeMounts.Clear();
        TmpFreeCapacity.Clear();
        TmpFreeInventoryMass.Clear();

        for (var i = 0; i < pawns.Count; i++)
        {
            var rider = pawns[i];
            if (!rider.RaceProps.Humanlike || !rider.IsColonist)
                continue;
            var mount = rider.GetExtendedPawnData()?.ReservedMount;
            if (mount == null || !caravan.ContainsPawn(mount) || !FlyerUtility.IsFlyer(mount.def))
                continue;
            if (!TmpTakenMounts.Add(mount))
                continue;
            TmpTakenRiders.Add(rider);
            pairs.Add(new FlyingPair(rider, mount));
        }

        for (var i = 0; i < pawns.Count; i++)
        {
            var mount = pawns[i];
            if (mount.RaceProps.Humanlike || !FlyerUtility.IsFlyer(mount.def) || !mount.IsEverMountable() ||
                TmpTakenMounts.Contains(mount) || IsReservedWithinCaravan(caravan, mount))
                continue;
            TmpFreeMounts.Add(mount);
            TmpFreeCapacity.Add(CapacityOf(mount));
            TmpFreeInventoryMass.Add(mount.inventory != null ? MassUtility.InventoryMass(mount) : 0f);
        }

        for (var i = 0; i < pawns.Count && TmpFreeMounts.Count > 0; i++)
        {
            var rider = pawns[i];
            if (!rider.RaceProps.Humanlike || !rider.IsColonist || TmpTakenRiders.Contains(rider) ||
                !rider.IsCapableOfRiding(out _))
                continue;

            var riderMass = rider.GetStatValue(StatDefOf.Mass);
            var best = -1;
            var bestRange = -1;
            var bestLoad = default(LoadInfo);
            for (var j = 0; j < TmpFreeMounts.Count; j++)
            {
                var load = new LoadInfo(TmpFreeMounts[j], riderMass + TmpFreeInventoryMass[j], TmpFreeCapacity[j]);
                if (load.Overloaded || load.range <= bestRange)
                    continue;
                best = j;
                bestRange = load.range;
                bestLoad = load;
            }
            if (best < 0)
            {
                best = 0;
                bestLoad = new LoadInfo(TmpFreeMounts[0], riderMass + TmpFreeInventoryMass[0], TmpFreeCapacity[0]);
            }

            pairs.Add(new FlyingPair(rider, TmpFreeMounts[best], bestLoad));
            TmpTakenMounts.Add(TmpFreeMounts[best]);
            TmpFreeMounts.RemoveAt(best);
            TmpFreeCapacity.RemoveAt(best);
            TmpFreeInventoryMass.RemoveAt(best);
        }

        for (var i = 0; i < pawns.Count; i++)
        {
            var mount = pawns[i];
            if (mount.RaceProps.Humanlike || !FlyerUtility.IsFlyer(mount.def) || TmpTakenMounts.Contains(mount))
                continue;
            pairs.Add(new FlyingPair(null, mount));
        }

        TmpTakenMounts.Clear();
        TmpTakenRiders.Clear();
        TmpFreeMounts.Clear();
        TmpFreeCapacity.Clear();
        TmpFreeInventoryMass.Clear();
        return pairs;
    }

    private static bool IsReservedWithinCaravan(Caravan caravan, Pawn mount)
    {
        var reservedBy = mount.GetExtendedPawnData()?.ReservedBy;
        return reservedBy != null && caravan.ContainsPawn(reservedBy) &&
               reservedBy.GetExtendedPawnData()?.ReservedMount == mount;
    }

    public static bool CaravanHasFlyingMount(Caravan caravan)
    {
        var pawns = caravan.PawnsListForReading;
        for (var i = 0; i < pawns.Count; i++)
            if (!pawns[i].RaceProps.Humanlike && FlyerUtility.IsFlyer(pawns[i].def))
                return true;
        return false;
    }

    public static Command MakeCaravanNoRiderCommand()
    {
        var command = new Command_Action
        {
            defaultLabel = "GU_TF_FlyCaravan".Translate(),
            defaultDesc = "GU_TF_FlyCaravanDesc".Translate(),
            icon = ResourceBank.iconFlyWorld,
            action = () => { }
        };
        command.Disable("GU_TF_NoRiderForFlyer".Translate());
        return command;
    }

    public static Command MakeCaravanWorldFlightCommand(Caravan caravan, List<FlyingPair> allPairs)
    {
        string? firstReason = null;
        var eligible = new List<FlyingPair>();
        var anyRider = false;
        foreach (var pair in allPairs)
        {
            if (CanFlyWorldFromCaravan(pair.rider, pair.mount, pair.load, out var reason))
            {
                eligible.Add(pair);
                anyRider |= pair.HasRider;
            }
            else if (pair.HasRider)
                firstReason ??= reason;
        }

        var description = "GU_TF_FlyCaravanDesc".Translate().Resolve();
        foreach (var pair in eligible)
            description += "\n- " + (pair.HasRider ? pair.rider.LabelShortCap + " / " : "") +
                           pair.mount.LabelShortCap + ": " + LoadReport(pair.load);

        var command = new Command_Action
        {
            defaultLabel = "GU_TF_FlyCaravan".Translate(),
            defaultDesc = description,
            icon = ResourceBank.iconFlyWorld,
            action = () => StartCaravanFlight(caravan)
        };

        if (!anyRider)
        {
            command.Disable(firstReason);
            return command;
        }

        TmpFlying.Clear();
        foreach (var pair in eligible)
        {
            if (pair.HasRider)
                TmpFlying.Add(pair.rider);
            TmpFlying.Add(pair.mount);
        }
        var anyLeftBehind = false;
        var ownerLeftBehind = false;
        var pawns = caravan.PawnsListForReading;
        for (var i = 0; i < pawns.Count && !ownerLeftBehind; i++)
        {
            if (TmpFlying.Contains(pawns[i]))
                continue;
            anyLeftBehind = true;
            ownerLeftBehind = CaravanUtility.IsOwner(pawns[i], Faction.OfPlayer);
        }
        TmpFlying.Clear();

        if (anyLeftBehind && !ownerLeftBehind)
            command.Disable("GU_TF_WouldStrand".Translate());
        return command;
    }

    private static List<FlyingPair> EligibleCaravanPairs(Caravan caravan)
    {
        var pairs = CaravanPairs(caravan);
        pairs.RemoveAll(p => !CanFlyWorldFromCaravan(p.rider, p.mount, p.load, out _));
        if (!pairs.Exists(p => p.HasRider))
            pairs.Clear();
        return pairs;
    }

    private static void StartCaravanFlight(Caravan caravan)
    {
        var pairs = EligibleCaravanPairs(caravan);
        if (pairs.Count == 0)
            return;
        var range = MinRange(pairs);
        var pods = new List<IThingHolder> { new PawnGroupHolder(pairs) };
        StartChoosingDestination(caravan.Tile, pods, range, false,
            (tile, arrivalAction) => LaunchFromCaravan(caravan, pairs, tile, arrivalAction));
    }

    private static void LaunchFromCaravan(Caravan caravan, List<FlyingPair> chosen, PlanetTile destination,
        TransportersArrivalAction arrivalAction)
    {
        Find.WorldTargeter.StopTargeting();
        if (caravan == null || caravan.Destroyed)
            return;
        var pairs = new List<FlyingPair>();
        foreach (var pair in chosen)
        {
            if ((pair.HasRider && !caravan.ContainsPawn(pair.rider)) || !caravan.ContainsPawn(pair.mount))
                continue;
            pair.RefreshLoad();
            if (CanFlyWorldFromCaravan(pair.rider, pair.mount, pair.load, out _))
                pairs.Add(pair);
        }
        var tiles = Find.WorldGrid.TraversalDistanceBetween(caravan.Tile, destination, true, int.MaxValue, true);
        pairs.RemoveAll(p => p.load.range < tiles);
        if (!pairs.Exists(p => p.HasRider))
            return;

        var speed = GroupSpeed(pairs);
        var travelling = (TravellingMountedFlyers)WorldObjectMaker.MakeWorldObject(GU_TF_DefOf.GU_TF_TravellingMountedFlyers);
        travelling.SetFaction(Faction.OfPlayer);
        travelling.destinationTile = destination;
        travelling.arrivalAction = arrivalAction;
        travelling.Tile = caravan.Tile;
        travelling.SetTilesPerDay(speed);
        Find.WorldObjects.Add(travelling);

        foreach (var pair in pairs)
        {
            var info = CreateTransporterInfo();
            caravan.RemovePawn(pair.mount);
            info.innerContainer.TryAdd(pair.mount);
            if (pair.HasRider)
            {
                var riderData = pair.rider.GetExtendedPawnData();
                var mountData = pair.mount.GetExtendedPawnData();
                if (riderData != null && mountData != null)
                {
                    riderData.ReservedMount = pair.mount;
                    mountData.ReservedBy = pair.rider;
                }
                pair.rider.Rotation = pair.mount.Rotation;
                caravan.RemovePawn(pair.rider);
                info.innerContainer.TryAdd(pair.rider);
            }
            travelling.AddTransporter(info, false);
            MountedFlightUtility.ApplyFatigue(pair.mount, WorldFatigueTicks(pair.mount, tiles, speed));
        }

        if (caravan.PawnsListForReading.Count == 0)
            caravan.Destroy();
        CameraJumper.TryJump(new GlobalTargetInfo(travelling));
    }
}

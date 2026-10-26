using System.Collections;
using System.Reflection;
using DevTools;
using DevTools.Testing;
using GiddyUp;
using GiddyUpTakeFlight;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using static GiddyUp.MountUtility;
using Priority = DevTools.Testing.Priority;
using Settings = GiddyUp.ModSettings_GiddyUp;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class ModuleTakeFlight : RiderAndMountTest
{
    private static readonly FieldInfo SelectedPairsFrame =
        AccessTools.Field(typeof(WorldFlightUtility), "selectedPairsFrame");
    private static readonly MethodInfo EligibleSelectedPairs =
        AccessTools.Method(typeof(WorldFlightUtility), "EligibleSelectedPairs");
    private static readonly MethodInfo LaunchFromMap =
        AccessTools.Method(typeof(WorldFlightUtility), "LaunchFromMap");
    private static readonly MethodInfo LaunchFromCaravan =
        AccessTools.Method(typeof(WorldFlightUtility), "LaunchFromCaravan");

    private readonly List<Pawn> _extraPawns = [];
    private bool _horseWasFlyer;
    private bool _requireOpenSky;
    private bool _ignoreTerrain;

    protected override Faction FactionOfPawns => Faction.OfPlayer;

    [SetUp]
    [ExecutionPriority(Priority.BelowNormal)]
    public void FlightSetup()
    {
        _horseWasFlyer = Settings.FlyingCache.Contains(_mount.def.shortHash);
        Settings.FlyingCache.Add(_mount.def.shortHash);
        _requireOpenSky = Settings.flightRequireOpenSky;
        _ignoreTerrain = Settings.flyersIgnoreTerrain;
        Settings.flightRequireOpenSky = true;
        Settings.flyersIgnoreTerrain = true;
        _mount.health.Reset();
    }

    [TearDown]
    [ExecutionPriority(Priority.AboveNormal)]
    public void FlightTearDown()
    {
        Find.Selector.ClearSelection();
        CameraJumper.TryHideWorld();

        var ours = new HashSet<Pawn>(_extraPawns) { _rider, _mount };
        foreach (var flyers in Find.WorldObjects.AllWorldObjects.OfType<TravellingMountedFlyers>().ToList())
            flyers.Destroy();
        foreach (var pawn in _extraPawns.Where(pawn => !pawn.Destroyed))
            pawn.Destroy();
        _extraPawns.Clear();
        foreach (var caravan in Find.WorldObjects.Caravans.Where(caravan => ours.Any(caravan.ContainsPawn)).ToList())
            caravan.Destroy();

        if (!_horseWasFlyer)
            Settings.FlyingCache.Remove(_mount.def.shortHash);
        Settings.flightRequireOpenSky = _requireOpenSky;
        Settings.flyersIgnoreTerrain = _ignoreTerrain;
    }

    [Test]
    public void RangeScalesWithLoad()
    {
        using var rangeRollback = new ScopedRollback<int>(ref Settings.worldFlightDefaultRange);
        using var multiplierRollback = new ScopedRollback<float>(ref Settings.worldFlightMaxRangeMultiplier);
        Settings.worldFlightDefaultRange = 5;
        Settings.worldFlightMaxRangeMultiplier = 3f;

        var full = new LoadInfo(_mount, 100f, 100f);
        Expect.AreApproximatelyEqual(1f, full.multiplier);
        Expect.AreEqual(5, full.range, "Full load should fly the base range");

        var half = new LoadInfo(_mount, 50f, 100f);
        Expect.AreApproximatelyEqual(2f, half.multiplier);
        Expect.AreEqual(10, half.range, "Half load should double the range");

        var light = new LoadInfo(_mount, 10f, 100f);
        Expect.AreApproximatelyEqual(3f, light.multiplier, "Multiplier should be capped");
        Expect.AreEqual(15, light.range);

        var overloaded = new LoadInfo(_mount, 110f, 100f);
        Expect.IsTrue(overloaded.Overloaded);
        Expect.AreEqual(0, overloaded.range, "An overloaded mount can't fly at all");
    }

    [Test]
    public void RiderlessLoadIsOnlyInventory()
    {
        var inventory = MassUtility.InventoryMass(_mount);
        Expect.AreApproximatelyEqual(inventory, WorldFlightUtility.LoadOf(null, _mount));
        Expect.AreApproximatelyEqual(_rider.GetStatValue(StatDefOf.Mass) + inventory,
            WorldFlightUtility.LoadOf(_rider, _mount));
        Expect.IsFalse(LoadInfo.For(null, _mount).Overloaded, "An empty mount shouldn't be overloaded");
    }

    [Test]
    public IEnumerator TakeOffRequirements()
    {
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("setup");
        Expect.IsTrue(FlyerUtility.TryGetFlyingMount(_rider, out var flyingMount), "Rider isn't on a flying mount");
        Expect.AreEqual(_mount, flyingMount);

        var map = _rider.Map;
        var cell = _rider.Position;
        var roof = map.roofGrid.RoofAt(cell);
        try
        {
            map.roofGrid.SetRoof(cell, null);
            Expect.IsTrue(MountedFlightUtility.CanTakeOff(_rider, _mount, out var reason), reason);

            // Roofed
            map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            Expect.IsFalse(MountedFlightUtility.CanTakeOff(_rider, _mount, out reason), "Took off under a roof");
            Expect.AreEqual("GU_TF_RoofedTakeOff".Translate().ToString(), reason);

            Settings.flightRequireOpenSky = false;
            Expect.IsTrue(MountedFlightUtility.CanTakeOff(_rider, _mount, out reason),
                "Couldn't take off under a roof with open sky not required");
            Settings.flightRequireOpenSky = true;
            map.roofGrid.SetRoof(cell, null);

            // Fatigued
            MountedFlightUtility.ApplyFatigue(_mount, GenDate.TicksPerHour);
            Expect.IsTrue(MountedFlightUtility.IsFatigued(_mount, out _), "Mount isn't fatigued after flight fatigue");
            Expect.IsFalse(MountedFlightUtility.CanTakeOff(_rider, _mount, out _), "Took off while fatigued");
            _mount.health.Reset();
            Expect.IsTrue(MountedFlightUtility.CanTakeOff(_rider, _mount, out reason), reason);
        }
        finally
        {
            map.roofGrid.SetRoof(cell, roof);
            _rider.Dismount(_mount, _riderData, true, ropeIfNeeded: false, waitForRider: false);
        }
        yield return new WaitTicks(1);
        ValidateIsNotMounted("cleanup");
    }

    [Test]
    public IEnumerator FlyingRiderIgnoresTerrain()
    {
        var pathing = _rider.Map.pathing;
        Expect.ReferencesAreEqual(pathing.Normal, _rider.GetPathContext(pathing), "Unmounted rider flies");

        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("setup");
        try
        {
            Expect.ReferencesAreEqual(pathing.Flying, _rider.GetPathContext(pathing),
                "Rider on a flying mount doesn't use the flying path grid");
            Expect.AreEqual(1, _rider.WaterCellCost, "Rider on a flying mount pays for water");

            Settings.flyersIgnoreTerrain = false;
            Expect.ReferencesAreEqual(pathing.Normal, _rider.GetPathContext(pathing),
                "Rider flies with ignore terrain off");
            Settings.flyersIgnoreTerrain = true;

            Settings.FlyingCache.Remove(_mount.def.shortHash);
            Expect.ReferencesAreEqual(pathing.Normal, _rider.GetPathContext(pathing),
                "Rider on a non-flying mount flies");
            Settings.FlyingCache.Add(_mount.def.shortHash);
        }
        finally
        {
            _rider.Dismount(_mount, _riderData, true, ropeIfNeeded: false, waitForRider: false);
        }
        yield return new WaitTicks(1);
        ValidateIsNotMounted("cleanup");
    }

    [Test]
    public IEnumerator TakeFlightLandsMounted()
    {
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("setup");

        var start = _rider.Position;
        var destination = GenRadial.RadialCellsAround(start, 12f, false)
            .Where(cell => cell.DistanceTo(start) >= 6f &&
                           MountedFlightUtility.CanLandAt(_rider, _mount, cell, out _))
            .DefaultIfEmpty(IntVec3.Invalid)
            .First();
        Expect.IsTrue(destination.IsValid, "No landing spot found near the rider");

        MountedFlightUtility.Launch(_rider, _mount, destination);
        Expect.IsFalse(_rider.Spawned, "Rider is still on the map after taking off");
        Expect.IsFalse(_mount.Spawned, "Mount is still on the map after taking off");

        yield return new WaitUntilTimeout(() => _rider.Spawned && _mount.Spawned, GenDate.TicksPerHour);
        yield return new WaitTicks(1);
        ValidateIsMounted("landed");
        Expect.LessThanOrEqualTo(_mount.Position.DistanceTo(destination), 2f, "Mount landed away from its target");
        Expect.IsTrue(MountedFlightUtility.IsFatigued(_mount, out _), "Mount isn't resting after a flight");

        _rider.Dismount(_mount, _riderData, true, ropeIfNeeded: false, waitForRider: false);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("cleanup");
    }

    [Test]
    public void CaravanRiderlessMountsFlyAlong()
    {
        var caravan = MakeCaravan(_rider, _mount, SpawnFlyer(), SpawnFlyer());
        var pairs = WorldFlightUtility.CaravanPairs(caravan);

        Expect.AreEqual(3, pairs.Count, "Every flying mount should be paired");
        Expect.AreEqual(1, pairs.Count(pair => pair.HasRider), "Only one flying mount has a rider");
        Expect.IsTrue(pairs.Any(pair => pair.rider == _rider && pair.mount == _mount),
            "Rider isn't paired with its reserved mount");

        var command = WorldFlightUtility.MakeCaravanWorldFlightCommand(caravan, pairs);
        Expect.IsFalse(command.Disabled, $"Caravan can't fly: {command.disabledReason}");
    }

    [Test]
    public void CaravanStillNeedsLeaderForWalkers()
    {
        var walker = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"), Faction.OfPlayer);
        _extraPawns.Add(walker);
        Expect.IsFalse(FlyerUtility.IsFlyer(walker.def), "Muffalo shouldn't fly");

        var caravan = MakeCaravan(_rider, _mount, SpawnFlyer(), walker);
        var command = WorldFlightUtility.MakeCaravanWorldFlightCommand(caravan, WorldFlightUtility.CaravanPairs(caravan));
        Expect.IsTrue(command.Disabled, "Caravan flew off and stranded an animal that can't fly");
        Expect.AreEqual("GU_TF_WouldStrand".Translate().ToString(), command.disabledReason);
    }

    [Test]
    public void CaravanWithoutRiderCannotFly()
    {
        var caravan = MakeCaravan(_mount, SpawnFlyer());
        Expect.IsFalse(WorldFlightUtility.CaravanPairs(caravan).Any(pair => pair.HasRider));

        var command = WorldFlightUtility.CaravanWorldFlightGizmo(caravan);
        Expect.IsNotNull(command, "Caravan with flying mounts has no flight button");
        Expect.IsTrue(command!.Disabled, "Flying mounts flew off without a rider");
        Expect.AreEqual("GU_TF_NoRiderForFlyer".Translate().ToString(), command.disabledReason);
    }

    [Test]
    public IEnumerator CaravanFlightCarriesRiderlessMounts()
    {
        var riderless = new[] { SpawnFlyer(), SpawnFlyer() };
        var caravan = MakeCaravan([_rider, _mount, ..riderless]);
        var destination = NeighbouringTile(caravan.Tile);

        LaunchFromCaravan.Invoke(null, [caravan, WorldFlightUtility.CaravanPairs(caravan), destination,
            new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived")]);
        Expect.IsTrue(caravan.Destroyed, "Empty caravan was left behind");

        yield return new WaitUntilTimeout(() => CaravanWith(_rider) != null, GenDate.TicksPerDay);
        var arrived = CaravanWith(_rider)!;
        Expect.AreEqual(destination, arrived.Tile, "Caravan formed on the wrong tile");
        Expect.IsTrue(arrived.ContainsPawn(_mount), "Ridden mount didn't arrive");
        foreach (var mount in riderless)
        {
            Expect.IsTrue(arrived.ContainsPawn(mount), $"{mount.LabelShortCap} didn't fly along");
            Expect.IsTrue(MountedFlightUtility.IsFatigued(mount, out _), $"{mount.LabelShortCap} isn't resting");
        }
    }

    [Test]
    public IEnumerator MapFlightTakesSelectedRiderlessMounts()
    {
        var map = _rider.Map;
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("setup");

        var selected = SpawnFlyer(map);
        var unselected = SpawnFlyer(map);
        var roofs = new Dictionary<IntVec3, RoofDef?>();
        foreach (var pawn in new[] { _rider, selected })
        {
            roofs[pawn.Position] = map.roofGrid.RoofAt(pawn.Position);
            map.roofGrid.SetRoof(pawn.Position, null);
        }

        List<FlyingPair> pairs;
        try
        {
            Find.Selector.ClearSelection();
            Find.Selector.Select(_rider, false, false);
            Find.Selector.Select(selected, false, false);

            pairs = SelectedPairs(map);
            Expect.AreEqual(2, pairs.Count, "Expected the rider and one riderless mount");
            Expect.IsTrue(pairs.Any(pair => pair.rider == _rider && pair.mount == _mount), "Rider isn't flying");
            Expect.IsTrue(pairs.Any(pair => !pair.HasRider && pair.mount == selected),
                "Selected riderless mount isn't flying along");
            Expect.IsFalse(pairs.Any(pair => pair.mount == unselected), "Unselected mount is flying along");

            // A riderless mount under a roof stays behind
            map.roofGrid.SetRoof(selected.Position, RoofDefOf.RoofConstructed);
            Expect.IsFalse(SelectedPairs(map).Any(pair => pair.mount == selected), "Roofed mount is flying along");
            map.roofGrid.SetRoof(selected.Position, null);

            // Riderless mounts alone can't fly
            Find.Selector.Deselect(_rider);
            Expect.IsFalse(SelectedPairs(map).Any(pair => pair.HasRider), "No rider was selected");
            Find.Selector.Select(_rider, false, false);
            pairs = SelectedPairs(map);
        }
        finally
        {
            foreach (var (cell, roof) in roofs)
                map.roofGrid.SetRoof(cell, roof);
        }

        var destination = NeighbouringTile(map.Tile);
        LaunchFromMap.Invoke(null, [map, pairs, destination,
            new TransportersArrivalAction_FormCaravan("MessageTransportPodsArrived")]);
        Expect.IsFalse(selected.Spawned, "Selected riderless mount stayed on the map");
        Expect.IsTrue(unselected.Spawned, "Unselected mount left the map");

        yield return new WaitUntilTimeout(() => CaravanWith(_rider) != null, GenDate.TicksPerDay);
        var arrived = CaravanWith(_rider)!;
        Expect.IsTrue(arrived.ContainsPawn(_mount), "Ridden mount didn't arrive");
        Expect.IsTrue(arrived.ContainsPawn(selected), "Riderless mount didn't fly along");
        Expect.IsTrue(MountedFlightUtility.IsFatigued(selected, out _), "Riderless mount isn't resting");
    }

    private Pawn SpawnFlyer(Map? map = null)
    {
        var flyer = PawnGenerator.GeneratePawn(AnimalKind, Faction.OfPlayer);
        _extraPawns.Add(flyer);
        map ??= Find.CurrentMap;
        GenSpawn.Spawn(flyer, CellFinder.StandableCellNear(_rider.Position, map, 5f), map);
        return flyer;
    }

    private static Caravan MakeCaravan(params Pawn[] pawns)
    {
        var rider = pawns.FirstOrDefault(pawn => pawn.RaceProps.Humanlike);
        var mount = pawns.FirstOrDefault(pawn => !pawn.RaceProps.Humanlike);
        if (rider != null && mount != null)
        {
            rider.GetExtendedPawnData().ReservedMount = mount;
            mount.GetExtendedPawnData().ReservedBy = rider;
        }
        return CaravanMaker.MakeCaravan(pawns, Faction.OfPlayer, Find.CurrentMap.Tile, true);
    }

    private static List<FlyingPair> SelectedPairs(Map map)
    {
        SelectedPairsFrame.SetValue(null, -1);
        return new List<FlyingPair>((List<FlyingPair>)EligibleSelectedPairs.Invoke(null, [map])!);
    }

    private static Caravan? CaravanWith(Pawn pawn) =>
        Find.WorldObjects.Caravans.FirstOrDefault(caravan => caravan.ContainsPawn(pawn));

    private static PlanetTile NeighbouringTile(PlanetTile tile)
    {
        var neighbours = new List<PlanetTile>();
        Find.WorldGrid.GetTileNeighbors(tile, neighbours);
        var destination = neighbours
            .Where(neighbour => !Find.World.Impassable(neighbour) && !Find.WorldObjects.AnyWorldObjectAt(neighbour))
            .DefaultIfEmpty(PlanetTile.Invalid)
            .First();
        Expect.IsTrue(destination.Valid, "No free tile next to the colony");
        return destination;
    }
}

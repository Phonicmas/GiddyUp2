using System.Collections;
using DevTools;
using DevTools.Testing;
using GiddyUp;
using GiddyUpRideAndRoll;
using RimWorld;
using Verse;
using static GiddyUp.ExtendedPawnData;
using static GiddyUp.MountUtility;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class ModuleRideAndRoll : RiderAndMountTest
{
    protected override Faction FactionOfPawns => Faction.OfPlayer;

    [Test]
    public void RidePermissionByColumn()
    {
        var colonists = new PawnColumnWorker_Mountable_Colonists();
        var slaves = new PawnColumnWorker_Mountable_Slaves();
        var allowedToRide = new PawnColumnWorker_AllowedToRide();

        // All checkboxes
        Expect.IsTrue(colonists.HasCheckbox(_mount));
        Expect.IsTrue(slaves.HasCheckbox(_mount));
        Expect.IsFalse(colonists.HasCheckbox(_rider));
        Expect.IsFalse(slaves.HasCheckbox(_rider));

        // Anyone -> Slaves
        _mountData.automount = Automount.Anyone;
        colonists.SetValue(_mount, false, null!);
        Expect.AreEqual(Automount.Slaves, _mountData.automount);

        // Slaves -> False
        slaves.SetValue(_mount, false, null!);
        Expect.AreEqual(Automount.False, _mountData.automount);

        // False -> Colonists
        colonists.SetValue(_mount, true, null!);
        Expect.AreEqual(Automount.Colonists, _mountData.automount);

        // Colonists -> Anyone
        slaves.SetValue(_mount, true, null!);
        Expect.AreEqual(Automount.Anyone, _mountData.automount);

        // Pawn
        Expect.IsTrue(allowedToRide.HasCheckbox(_rider));
        allowedToRide.SetValue(_rider, false, null!);
        Expect.IsFalse(_rider.CanRide());
        allowedToRide.SetValue(_rider, true, null!);
        Expect.IsTrue(_rider.CanRide());
        allowedToRide.SetValue(_rider, true, null!);
        Expect.IsTrue(_rider.CanRide(), "Setting an enabled checkbox twice must leave it enabled");
    }

    [Test]
    public void Areas()
    {
        var map = Find.CurrentMap;
        var cell = map.Center;
        var dropExpand = new TestableDropExpand();
        var dropClear = new TestableDropClear();
        var noMountExpand = new TestableNoMountExpand();
        var noMountClear = new TestableNoMountClear();

        try
        {
            // Init
            dropExpand.SelectArea();
            dropClear.SelectArea();
            noMountExpand.SelectArea();
            noMountClear.SelectArea();

            // Drop - Designate Cell
            Expect.IsTrue(dropExpand.CanDesignateCell(cell));
            dropExpand.DesignateSingleCell(cell);
            Expect.IsFalse(dropExpand.CanDesignateCell(cell));
            Expect.IsTrue(dropClear.CanDesignateCell(cell));

            map.GetGUAreas(out var noMountArea, out var dropArea);
            Expect.IsNotNull(dropArea);
            Expect.IsTrue(dropArea![cell]);

            // Drop - Clear cell
            dropClear.DesignateSingleCell(cell);
            Expect.IsFalse(dropArea[cell]);

            // No Mount - Designate Cell
            Expect.IsTrue(noMountExpand.CanDesignateCell(cell));
            noMountExpand.DesignateSingleCell(cell);
            Expect.IsTrue(noMountClear.CanDesignateCell(cell));

            map.GetGUAreas(out noMountArea, out _);
            Expect.IsNotNull(noMountArea);
            Expect.IsTrue(noMountArea![cell]);
            Expect.IsFalse(cell.CanRideAt(noMountArea));

            // No Mount - Clear Cell
            noMountClear.DesignateSingleCell(cell);
            Expect.IsTrue(cell.CanRideAt(noMountArea));
            Expect.AreEqual(DrawStyleCategoryDefOf.FilledRectangle, dropClear.DrawStyleCategory);
            Expect.AreEqual(DrawStyleCategoryDefOf.FilledRectangle, noMountClear.DrawStyleCategory);
            Expect.IsTrue(dropClear.DragDrawMeasurements);
            Expect.IsTrue(noMountClear.DragDrawMeasurements);
        }
        finally
        {
            ClearArea(map, ResourceBank.AreaDropMount);
            ClearArea(map, ResourceBank.AreaNoMount);
        }
    }

    [Test]
    public void FindClosestDismountSpot()
    {
        using var hitchDistance = new ScopedRollback<int>(ref ModSettings_GiddyUp.autoHitchDistance);
        var map = Find.CurrentMap;
        var destination = map.Center + new IntVec3(8, 0, 0);
        var closestSpot = map.Center + new IntVec3(6, 0, 0);
        var fartherSpot = map.Center + new IntVec3(-6, 0, 0);
        var area = GetOrCreateArea(map, ResourceBank.AreaDropMount);
        area[closestSpot] = true;
        area[fartherSpot] = true;
        ModSettings_GiddyUp.autoHitchDistance = 100;

        try
        {
            var found = _rider.FindPlaceToDismount(area, null, destination, out var location, _mount, out var type);

            Expect.IsTrue(found);
            Expect.AreEqual(DismountLocationType.Spot, type);
            Expect.AreEqual(closestSpot, location);
        }
        finally
        {
            ClearArea(map, ResourceBank.AreaDropMount);
        }
    }

    [Test]
    public IEnumerator AnimalWaitsWhenDismounted()
    {
        // Setup
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("setup", checkReservations: true);

        // Wait for Rider
        _rider.Dismount(_mount, _riderData, ropeIfNeeded: false);
        yield return _mount.WaitJob(ResourceBank.JobDefOf.WaitForRider, waitJobType: WaitJobType.StartsJob);

        ValidateIsNotMounted("waiting");
        Expect.AreEqual(_rider, _mountData.ReservedBy);
        Expect.AreEqual(_mount, _riderData.ReservedMount);
        Expect.AreEqual(_rider, _mount.CurJob.targetA.Thing);

        // Has Gizmo
        var leaveRiderLabel = "GU_RR_Gizmo_LeaveRider_Label".Translate();
        var command = _mount.GetGizmos().OfType<Command_Action>()
            .FirstOrDefault(gizmo => gizmo.defaultLabel == leaveRiderLabel);
        Expect.IsNotNull(command, "Waiting mounts should expose a Leave rider gizmo");

        // End Wait for Rider
        command!.action();
        yield return new WaitTicks(1);
        
        Expect.AreNotEqual(ResourceBank.JobDefOf.WaitForRider, _mount.CurJobDef);
        Expect.IsNull(_mountData.ReservedBy);
        Expect.IsNull(_riderData.ReservedMount);
        Expect.All(_mount.GetGizmos().OfType<Command_Action>(), gizmo => gizmo.defaultLabel != leaveRiderLabel);
    }

    [Test]
    public IEnumerator AnimalWaitsUntilHungry()
    {
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        _rider.Dismount(_mount, _riderData, ropeIfNeeded: false);
        yield return _mount.WaitJob(ResourceBank.JobDefOf.WaitForRider, waitJobType: WaitJobType.StartsJob);
        
        _mount.needs.food.CurLevel = 0f;
        yield return new WaitTicks(31);
        Expect.AreNotEqual(ResourceBank.JobDefOf.WaitForRider, _mount.CurJobDef);
        Expect.IsNull(_mountData.ReservedBy);
        Expect.IsNull(_riderData.ReservedMount);
    }

    [Test]
    public void Areas_Alert()
    {
        var map = Find.CurrentMap;
        var noMount = GetOrCreateArea(map, ResourceBank.AreaNoMount);
        var dropMount = GetOrCreateArea(map, ResourceBank.AreaDropMount);
        var alert = new Alert_NoDropAnimal();
        var cell = map.Center;

        try
        {
            noMount[cell] = false;
            dropMount[cell] = false;
            using (new MockGameTicks(20))
                Expect.IsFalse(alert.GetReport().active);

            noMount[cell] = true;
            using (new MockGameTicks(40))
                Expect.IsFalse(alert.GetReport().active,
                    "A rope-managed animal alone must not trigger the missing mount spot warning");
        }
        finally
        {
            ClearArea(map, ResourceBank.AreaNoMount);
            ClearArea(map, ResourceBank.AreaDropMount);
        }
    }

    private static Area GetOrCreateArea(Map map, string label)
    {
        var area = map.areaManager.GetLabeled(label);
        if (area != null)
            return area;

        switch (label)
        {
            case ResourceBank.AreaDropMount:
                new TestableDropExpand().SelectArea();
                break;
            case ResourceBank.AreaNoMount:
                new TestableNoMountExpand().SelectArea();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown Ride-and-Roll area label");
        }

        return map.areaManager.GetLabeled(label)!;
    }

    private static void ClearArea(Map map, string label)
    {
        var area = map.areaManager.GetLabeled(label);
        if (area == null)
            return;

        foreach (var cell in map.AllCells)
            area[cell] = false;
        map.UpdateAreaCache();
    }

    private sealed class TestableDropExpand : Designator_GU_DropAnimal_Expand
    {
        public void SelectArea() => SetSelectedArea();
    }

    private sealed class TestableDropClear : Designator_GU_DropAnimal_Clear
    {
        public void SelectArea() => SetSelectedArea();
    }

    private sealed class TestableNoMountExpand : Designator_GU_NoMount_Expand
    {
        public void SelectArea() => SetSelectedArea();
    }

    private sealed class TestableNoMountClear : Designator_GU_NoMount_Clear
    {
        public void SelectArea() => SetSelectedArea();
    }
}

using System.Collections;
using System.Reflection;
using DevTools.Testing;
using GiddyUp;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Priority = DevTools.Testing.Priority;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class FeatureVisitorMounting : RiderAndMountTest
{
    private static readonly MethodInfo DetermineNextJobPostfix =
        AccessTools.Method(AccessTools.TypeByName("GiddyUp.Harmony.Patch_DetermineNextJob"), "Postfix");

    private Lord? _lord;
    private Trait? _noThinking;

    protected override Faction FactionOfPawns => Faction.OfAncients;

    [SetUp]
    [ExecutionPriority(Priority.BelowNormal)]
    public void VisitorSetup()
    {
        // Keep the visitor from picking its own jobs, so only the test drives it
        _noThinking = new Trait(TestingDefs.Dummy);
        _rider.story.traits.GainTrait(_noThinking);
        _mount.Position = CellFinder.StandableCellNear(_rider.Position, _rider.Map, 3f);
        _mount.Notify_Teleported();
        // Visitors arrive on tamed mounts; a freshly generated one isn't
        _mount.training.Train(TrainableDefOf.Tameness, null, true);
        _riderData.ReservedMount = _mount;
        _mountData.ReservedBy = _rider;
        _lord = LordMaker.MakeNewLord(FactionOfPawns, new LordJob_ExitMapBest(), _rider.Map, [_rider]);
    }

    [TearDown]
    [ExecutionPriority(Priority.AboveNormal)]
    public void VisitorTearDown()
    {
        if (_lord != null && _rider.Map?.lordManager.lords.Contains(_lord) is true)
            _rider.Map.lordManager.RemoveLord(_lord);
        _lord = null;
    }

    [Test]
    public void LeavingVisitorRequeuesPlannedJob()
    {
        Expect.IsTrue(_lord!.CurLordToil is LordToil_ExitMap, "Visitor's lord isn't leaving");
        Expect.IsFalse(_rider.Faction.HostileTo(Faction.OfPlayer), "Visitor is hostile");
        Expect.IsTrue(ModSettings_GiddyUp.caravansEnabled, "Caravans module is off");
        Expect.IsTrue(_mount.IsMountable(out var reason, _rider, true, true), $"Visitor can't mount its animal: {reason}");

        // A job another mod gave the visitor (e.g. Allies are Helpful's TendPatient)
        var planned = PlannedJob();
        var result = DetermineNextJob(new ThinkResult(planned, null));

        Expect.AreEqual(ResourceBank.JobDefOf.Mount, result.Job?.def, "Leaving visitor didn't go mount up");
        Expect.AreEqual(_mount, result.Job!.targetA.Thing, "Visitor is mounting the wrong animal");
        Expect.ReferencesAreEqual(planned, _rider.jobs.jobQueue.FirstOrFallback(null)?.job,
            "Visitor's planned job was dropped instead of queued behind mounting");
    }

    [Test]
    public void QueuedMountIsLeftAlone()
    {
        // Once mounting is queued, the visitor's next job must pass through untouched, or it would be queued again
        var mount = JobMaker.MakeJob(ResourceBank.JobDefOf.Mount, _mount);
        _rider.jobs.jobQueue.EnqueueFirst(mount);
        var planned = PlannedJob();
        var result = DetermineNextJob(new ThinkResult(planned, null));

        Expect.ReferencesAreEqual(planned, result.Job, "Visitor's job was replaced while mounting was queued");
        Expect.AreEqual(1, _rider.jobs.jobQueue.Count, "A job was queued twice");
        _rider.jobs.ClearQueuedJobs();
    }

    [Test]
    public IEnumerator LeavingVisitorMountsThenDoesPlannedJob()
    {
        var planned = PlannedJob();
        var result = DetermineNextJob(new ThinkResult(planned, null));
        Expect.AreEqual(ResourceBank.JobDefOf.Mount, result.Job?.def, "Leaving visitor didn't go mount up");

        // Let the visitor pick up queued jobs on its own once mounting finishes
        _rider.story.traits.RemoveTrait(_noThinking!);
        _rider.jobs.StartJob(result.Job, JobCondition.InterruptForced);
        yield return new WaitUntilTimeout(_rider.IsMounted, GenDate.TicksPerHour);
        ValidateIsMounted("mounted to leave", checkReservations: true);

        yield return new WaitUntilTimeout(() => _rider.CurJob == planned, GenDate.TicksPerHour);
        Expect.ReferencesAreEqual(planned, _rider.CurJob, "Visitor didn't get back to its planned job after mounting");
        Expect.IsTrue(_rider.IsMounted(), "Visitor dismounted to do its planned job");

        // Stop the visitor before it heads off the map
        _rider.story.traits.GainTrait(_noThinking!);
        _rider.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
        _rider.Dismount(_mount, _riderData, true, ropeIfNeeded: false, waitForRider: false);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("cleanup");
    }

    private Job PlannedJob()
    {
        var map = _rider.Map;
        // Far enough that the visitor is still on its way when the test checks
        var target = (_rider.Position + new IntVec3(40, 0, 0)).ClampInsideMap(map);
        var cell = CellFinder.StandableCellNear(target, map, 8f);
        Expect.IsTrue(cell.IsValid, "No standable cell for the planned job");
        return JobMaker.MakeJob(JobDefOf.Goto, cell);
    }

    private ThinkResult DetermineNextJob(ThinkResult result)
    {
        object[] args = [_rider.jobs, result];
        DetermineNextJobPostfix.Invoke(null, args);
        return (ThinkResult)args[1];
    }
}

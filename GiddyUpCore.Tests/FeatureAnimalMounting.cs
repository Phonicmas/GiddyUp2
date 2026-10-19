using System.Collections;
using DevTools;
using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static GiddyUp.IsMountableUtility;
using static GiddyUp.MountUtility;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class FeatureAnimalMounting : RiderAndMountTest
{
    protected override Faction FactionOfPawns => Faction.OfPlayer;
    
    [Test]
    public IEnumerator AnimalMountableWithConditions()
    {
        // Can be mounted
        Expect.IsTrue(_mount.IsMountable(out var reason, _rider, true, true));
        Expect.AreEqual(Reason.CanMount, reason);
        Expect.IsFalse(_mount.IsInPoorCondition(), "Animal is in poor condition after spawning");
        
        // Does mount with Job
        _rider.GoMount(_mount, GiveJobMethod.Try);
        yield return _rider.WaitJob(ResourceBank.JobDefOf.Mount);
        ValidateIsMounted("job");
        
        _rider.Dismount(_mount, _riderData, true, ropeIfNeeded: false, waitForRider: false);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("job");
        
        // Does mount instantly
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("instant");
        
        _rider.Dismount(_mount, _riderData, true, ropeIfNeeded: false, waitForRider: false);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("instant");
        
        // Can reverse lookup for dismount
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("reverse");
        
        _rider.Dismount(null, _riderData, true, ropeIfNeeded: false, waitForRider: false);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("reverse");
        
        // Shouldn't be mountable while hurt
        while (_mount.health.summaryHealth.SummaryHealthPercent >= 0.75) 
            _mount.TakeDamage(new DamageInfo(DamageDefOf.Crush, 10f));
        
        Expect.IsTrue(_mount.IsInPoorCondition(), "Animal is not in poor condition after crushing");
        
        // Should be mountable after health reset
        _mount.health.Reset();
        Expect.IsFalse(_mount.IsInPoorCondition(), "Animal is still in poor condition after resetting health");
        
        // Shouldn't be mountable after infection
        _mount.health.AddHediff(HediffDefOf.WoundInfection, _mount.RaceProps.body.corePart);
        Expect.IsTrue(_mount.health.HasHediffsNeedingTend(), "Animal does not have tendable hediff after infection added");
        Expect.IsTrue(_mount.IsInPoorCondition(), "Animal is not in poor condition after getting an infection");
        
        // Should be mountable after infection tended
        var infection = _mount.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.WoundInfection);
        Expect.IsNotNull(infection, "Animal does not have infection");
        infection.Tended(1f, 1f);
        Expect.IsFalse(_mount.IsInPoorCondition(), "Animal is still in poor condition after infection tended");
        
        // Shouldn't be mountable if enemy
        _mount.health.Reset();
        _mount.SetFaction(Faction.OfAncientsHostile);
        Expect.IsFalse(_mount.IsMountable(out var factionReason, _rider, true, true));
        Expect.AreEqual(Reason.WrongFaction, factionReason);
    }

    [Test]
    public void AutoMount()
    {
        using var distanceRollback = new ScopedRollback<int>(ref ModSettings_GiddyUp.minAutoMountDistance);
        var destination = Find.CurrentMap.AllCells.MaxBy(cell => cell.DistanceTo(_rider.Position));


        // Should auto mount
        ModSettings_GiddyUp.minAutoMountDistance = 2;
        var result = new ThinkResult(JobMaker.MakeJob(JobDefOf.Goto, destination), null);
        _rider.TryAutoMount(_rider.jobs, ref result);
        
        Expect.AreEqual(ResourceBank.JobDefOf.Mount, result.Job.def);
        Expect.AreEqual(_mount, result.Job.targetA.Thing);
        
        // Shouldn't auto mount
        ModSettings_GiddyUp.minAutoMountDistance = 500;
        var job = JobMaker.MakeJob(JobDefOf.Goto, destination);
        result = new ThinkResult(job, null);
        _rider.TryAutoMount(_rider.jobs, ref result);
        
        Expect.AreEqual(job, result.Job);
        
        // Shouldn't auto mount when the pawn is not allowed to ride
        ModSettings_GiddyUp.minAutoMountDistance = 2;
        _riderData.canRide = false;
        job = JobMaker.MakeJob(JobDefOf.Goto, destination);
        result = new ThinkResult(job, null);
        _rider.TryAutoMount(_rider.jobs, ref result);
        Expect.AreEqual(job, result.Job); 
        
        // Shouldn't auto mount animals disabled for automatic riding
        _riderData.canRide = true;
        _mountData.automount = ExtendedPawnData.Automount.False;
        job = JobMaker.MakeJob(JobDefOf.Goto, destination);
        result = new ThinkResult(job, null);
        _rider.TryAutoMount(_rider.jobs, ref result);
        Expect.AreEqual(job, result.Job);
        
        // Should auto mount when both pawn and animal permissions allow it
        _mountData.automount = ExtendedPawnData.Automount.Anyone;
        result =  new ThinkResult(JobMaker.MakeJob(JobDefOf.Goto, destination), null);
        _rider.TryAutoMount(_rider.jobs, ref result);
        Expect.AreEqual(ResourceBank.JobDefOf.Mount, result.Job.def);
        Expect.AreEqual(_mount, result.Job.targetA.Thing);
    }
}
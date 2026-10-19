using System.Collections;
using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using GiddyUpCore.SaddleUp;
using GiddyUpCore.Tests.Utils;
using RimWorld;
using SaddleUp;
using Verse;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class ModuleSaddleUp : RiderAndMountTest
{
    protected override Faction FactionOfPawns => Faction.OfPlayer;

    [Test]
    public IEnumerator HasGizmo()
    {
        var mountString = "GUC_Mount".Translate();
        var dismountString = "GUC_Dismount".Translate();
        _rider.drafter.Drafted = true;
        var gizmos = _rider.GetGizmos().ToList();
        
        // Go mount
        Expect.Any(gizmos, x => x is Command_Action action 
                                && action.defaultLabel == mountString
                                && !action.Disabled);

        _rider.GoMount(_mount, MountUtility.GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        
        // Go dismount
        gizmos = _rider.GetGizmos().ToList();
        Expect.Any(gizmos, x => x is Command_Action action
                                && action.defaultLabel == dismountString
                                && !action.Disabled);
        
        // Downed / Undrafted
        HealthUtility.DamageUntilDowned(_rider);
        yield return new WaitTicks(1);
        gizmos = _rider.GetGizmos().ToList();
        Expect.All(gizmos, x => x is not Command_Action action || action.defaultLabel != dismountString);
    }

    [Test]
    public IEnumerator MultiMount()
    {
        var worker = TestPawns
            .Worker
            .WithLocation(_rider.Map.Center + new IntVec3(10, 0, 0))
            .WithMount(AnimalKind);
        var otherRider = worker.Spawn();

        var otherRiderData = otherRider.GetExtendedPawnData();
        var otherMount = otherRiderData.Mount;
        otherRider.InvoluntaryDismount(otherMount, otherRiderData);
        yield return new WaitTicks(1);
        _rider.drafter.Drafted = true;
        otherRider.drafter.Drafted = true;
        
        // Mount
        var comp = _rider.Map.GetComponent<Coordinator>();
        comp.MountPawn(_rider);
        comp.MountPawn(otherRider);

        yield return new WaitUntilTimeout(() => _rider.IsMounted() && otherRider.IsMounted());
        
        ValidateIsMounted("Mount", checkBindings: false);
        
        // Dismount
        Pawn_GetGizmos_SU2.DismountPawn(_rider);
        Pawn_GetGizmos_SU2.DismountPawn(otherRider);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("Dismount");
        
        worker.Destroy();
    }
}
using System.Collections;
using DevTools.Testing;
using GiddyUp;
using RimWorld;
using UnityEngine;
using Verse;
using static GiddyUp.MountUtility;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class FeatureEnemyAnimalMounting : RiderAndMountTest
{
    protected override Faction FactionOfPawns => Faction.OfAncientsHostile;
    
    [Test]
    public IEnumerator EnemyAnimalMountable()
    {
        ValidateIsNotMounted("init");

        // Go mount
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("default");

        // Animal downed
        HealthUtility.DamageUntilDowned(_mount);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("downed");
        
        // Heal and Remount
        _mount.health.Reset();
        _mount.SetFaction(FactionOfPawns);
        _rider.GoMount(_mount, GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        
        // Animal dead
        HealthUtility.DamageUntilDead(_mount);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("dead");
        
        // Resurrected
        ResurrectionUtility.TryResurrect(_mount);
        if(_mount.Faction != FactionOfPawns)
            _mount.SetFaction(FactionOfPawns);
        yield return new WaitTicks(1);
        ValidateIsNotMounted("resurrected");
    }
}
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using DevTools;
using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using GiddyUpCore.Core;
using GiddyUpCore.Tests.Utils;
using RimWorld;
using UnityEngine;
using Verse;
using static GiddyUp.IsMountableUtility;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class FeatureModExtension : RiderAndMountTest
{
    protected override Faction FactionOfPawns => Faction.OfPlayer;

    [Test]
    public IEnumerator AllowedLifeStages()
    {
        var mountModExtensions = _mount.def.modExtensions;
        _mount.def.modExtensions = [];
        
        var originalDisregardAnimalAge = ModSettings_GiddyUp.disregardAnimalAge;
        var originalDisregardAnimalCarryingCapacity =  ModSettings_GiddyUp.disregardAnimalCarryingCapacity;
        ModSettings_GiddyUp.disregardAnimalAge = false;
        ModSettings_GiddyUp.disregardAnimalCarryingCapacity = true;
        _mount.health.Reset();

        try
        {
            _rider.GoMount(_mount, MountUtility.GiveJobMethod.Instant);
            yield return new WaitTicks(1);
            ValidateIsMounted("setup");

            _rider.Dismount(_mount, _riderData, true, ropeIfNeeded: false, waitForRider: false);
            yield return new WaitTicks(1);
            ValidateIsNotMounted("setup");

            // Too young
            _mount.ageTracker.AgeBiologicalTicks = (long)(_mount.RaceProps.lifeStageAges.FirstOrDefault()!.minAge * 3600000f);
            Expect.IsFalse(_mount.ageTracker.Adult, "Animal is still an adult at earliest stage");
            Expect.IsFalse(_mount.IsMountable(out var notFullyGrownReason, _rider));
            Expect.AreEqual(Reason.NotFullyGrown, notFullyGrownReason, "Animal is ridable while not fully grown");

            _mount.def.modExtensions.Add(new AllowedLifeStages()
            {
                allowedLifeStagesCSV = _mount.ageTracker.CurLifeStageIndex.ToString(),
            });
            Expect.IsTrue(_mount.IsMountable(out var reason, _rider));
            Expect.AreEqual(Reason.CanMount, reason, reason.ToString());
        }
        finally
        {
            _mount.def.modExtensions = mountModExtensions;
            ModSettings_GiddyUp.disregardAnimalAge = originalDisregardAnimalAge;
            ModSettings_GiddyUp.disregardAnimalCarryingCapacity = originalDisregardAnimalCarryingCapacity;
        }
    }

    [Test]
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public IEnumerator CustomStats()
    {
        _rider.GoMount(_mount, MountUtility.GiveJobMethod.Instant);
        yield return new WaitTicks(1);
        ValidateIsMounted("setup");
        
        var stats = new CustomStats();
        var modExtensions = _mount.kindDef.modExtensions;
        _mount.kindDef.modExtensions = [ stats ];
        var crush = DamageDefOf.Crush;
        _mount.health.Reset();
        var mountHealth = _mount.health.hediffSet.hediffs.Where(x => x.def.isBad);
        var part = _mount.RaceProps.body.AllParts.FirstOrDefault(x => x.def == BodyPartDefOf.Leg);

        var originalHandlingMovementImpact = ModSettings_GiddyUp.handlingMovementImpact;
        try   
        {
            var des = Find.CurrentMap.AllCells.Last();
            
            // Baseline
            var normalMovement = _rider.GetStatValue(StatDefOf.MoveSpeed);
            var normalCost = _rider.PathCost(des);
            _mount.TakeDamage(new DamageInfo(crush, 10, hitPart: part));
            var normalDamage = mountHealth.Sum(x => x.Severity);
            _mount.health.Reset();
            
            // High
            stats.speedModifier = 5f;
            stats.armorModifier = 5f;
            var fastMovement = _rider.GetStatValue(StatDefOf.MoveSpeed);
            var fastCost = _rider.PathCost(des);
            _mount.TakeDamage(new DamageInfo(crush, 10, hitPart: part));
            var fastDamage = mountHealth.Sum(x => x.Severity);
            _mount.health.Reset();

            // Low
            stats.speedModifier = 0.5f;
            stats.armorModifier = 0.5f;
            var slowMovement = _rider.GetStatValue(StatDefOf.MoveSpeed);
            var slowCost = _rider.PathCost(des);
            _mount.TakeDamage(new DamageInfo(crush, 10, hitPart: part));
            var slowDamage = mountHealth.Sum(x => x.Severity);
            _mount.health.Reset();

            // Impact
            ModSettings_GiddyUp.handlingMovementImpact = 2.5f;
            var baseSpeed = 4f;
            var skill = _rider.skills.GetSkill(SkillDefOf.Animals).Level;
            var required = (int)Math.Round(_mount.GetStatValue(StatDefOf.MinimumHandlingSkill, true));
            var expected = baseSpeed * (1f + (skill - required) * 0.025f) * stats.speedModifier;
            var actual = StatPart_Riding.GetRidingSpeed(baseSpeed, _mount, _rider.skills);

            Expect.IsTrue(Math.Abs(expected - actual) < 0.0001f,
                $"Expected riding speed {expected}, got {actual}");
            
            
            Expect.GreaterThan(fastMovement, normalMovement);
            Expect.GreaterThan(normalCost, fastCost);
            Expect.GreaterThan(normalMovement, slowMovement);
            Expect.GreaterThan(slowCost, normalCost);
            
            Expect.GreaterThan(normalDamage, fastDamage);
            Expect.GreaterThan(slowDamage, normalDamage);
        }
        finally
        {
            _mount.kindDef.modExtensions = modExtensions;
            ModSettings_GiddyUp.handlingMovementImpact = originalHandlingMovementImpact;
        }
    }

    [Test]
    public void DrawingOffset()
    {
        var offset = new DrawingOffset()
        {
            northOffset =  new Vector3(1f, 2f, 3f),
            southOffset = new Vector3(4f, 5f, 6f),
            eastOffset = new Vector3(7f, 8f, 9f),
            westOffset = new Vector3(10f, 11f, 12f)
        };

        var originalExtensions = _mount.def.modExtensions;
        var originalDrawOffset = _riderData.drawOffset;
        _mount.def.modExtensions = [offset];
        _riderData.drawOffset = -1f;

        try
        {
            // No Offset
            var north = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.North);
            var south = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.South);
            var east = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.East);
            var west = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.West);

            // TODO Screenshot util would be helpful here, visually compare that things move
            Expect.AreEqual(new Vector3(1f, 2f, 3f), north);
            Expect.AreEqual(new Vector3(4f, 5f, 6f), south);
            Expect.AreEqual(new Vector3(7f, 8f, 9f), east);
            Expect.AreEqual(new Vector3(10f, 11f, 12f), west);
            
            // Offset
            _riderData.drawOffset = 4f;
            north = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.North);
            south = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.South);
            east = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.East);
            west = MountedRiderRenderNodeUtility.GetMountedRiderOffset(_rider, _mount, Rot4.West);
            
            Expect.AreEqual(new Vector3(1f, 2f, 7f), north);
            Expect.AreEqual(new Vector3(4f, 5f, 10f), south);
            Expect.AreEqual(new Vector3(7f, 8f, 13f), east);
            Expect.AreEqual(new Vector3(10f, 11f, 16f), west);
        }
        finally
        {
            _mount.def.modExtensions = originalExtensions;
            _riderData.drawOffset = originalDrawOffset;
        }
    }

    [Test]
    public void MountableOrNot()
    {
        var originalExtensions = _mount.def.modExtensions;
        try
        {
            var mountable = new Mountable();
            var notMountable = new NotMountable();

            // Can mount
            _mount.def.modExtensions = [mountable];
            GiddyUp.Setup.BuildMountCache();
            Expect.IsTrue(_mount.IsMountable(out var mountableReason, _rider, false, false, false), mountableReason.ToString());

            // Can't mount
            _mount.def.modExtensions = [notMountable];
            GiddyUp.Setup.BuildMountCache();
            Expect.IsFalse(_mount.IsMountable(out var unmountableReason, _rider, false, false, false), "Animal is still mountable");
            Expect.AreEqual(Reason.NotInModOptions, unmountableReason, "Expected reason to be NotInModOptions");
        }
        finally
        {
            _mount.def.modExtensions = originalExtensions;
            GiddyUp.Setup.BuildMountCache();
        }
    }

    
    public void DrawInFront()
    {
        // Yeah, I actually have no idea how to test this...
    }

    [Test]
    public void ResearchRestrictions()
    {
        var research = DefDatabase<ResearchProjectDef>.AllDefsListForReading.FirstOrDefault(x => !x.IsFinished);
        Expect.IsNotNull(research, "No unfinished research to use for test");
        Expect.IsTrue(_mount.IsMountable(out var initReason, _rider), initReason.ToString());
        
        var extension = new ResearchRestrictions { researchProjectDefs = [research] };
        var originalExtensions = _mount.def.modExtensions;
        _mount.def.modExtensions = [extension];
        
        try
        {
            Expect.IsFalse(_mount.IsMountable(out var reason, _rider));
            Expect.AreEqual(Reason.MissingResearch, reason);
        }
        finally
        {
            _mount.def.modExtensions = originalExtensions;
        }
    }

    [Test]
    public IEnumerator CustomMounts()
    {
        var customMounts = new CustomMounts()
        {
            possibleMounts = new() { [_mount.kindDef] = 1 },
            mountChance = 100
        };
        
        var originalExtensions = PawnKindDefOf.AncientSoldier.modExtensions;
        PawnKindDefOf.AncientSoldier.modExtensions = [customMounts];
        var builder = TestPawns.Solder
            .MakeHostile()
            .WithKind(PawnKindDefOf.AncientSoldier)
            .WithWorkMaxed(WorkTypeDefOf.Hunting)
            .WithCustomMounts();
        try
        {
            var pawns = builder.SpawnMany(3);
            yield return new WaitTicks(1);
            
            Expect.Any(pawns, pawn => pawn.IsMounted(), "No pawn was mounted");
            Expect.All(pawns.Where(x => x.IsMounted()), pawn => pawn.GetExtendedPawnData().Mount!.kindDef == _mount.kindDef);
        }
        finally
        {
            PawnKindDefOf.AncientSoldier.modExtensions = originalExtensions;
            Tweaker.SuperFast = true;
            builder.Destroy();
        }
    }

    [Test]
    public void FactionRestrictions()
    {
        // TODO
    }
}
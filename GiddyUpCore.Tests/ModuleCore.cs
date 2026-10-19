using System;
using System.Collections;
using System.Collections.Generic;
using DevTools;
using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using GiddyUpCore.Core;
using RimWorld;
using UnityEngine;
using Verse;
using static GiddyUp.IsMountableUtility;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class ModuleCore : RiderAndMountTest
{
    protected override Faction FactionOfPawns => Faction.OfPlayer;

    [Test]
    public void ExtendedPawnDataReverseLookup()
    {
        var repeatedLookup = ExtendedDataStorage.Singleton.GetExtendedPawnData(_rider);
        Expect.AreEqual(_riderData, repeatedLookup);

        _mountData.ReservedBy = _rider;
        Expect.AreEqual(_mountData,
            ExtendedDataStorage.Singleton.GetExtendedPawnDataReverseLookup(_rider.thingIDNumber));
    }
    
    [Test]
    public void AutomountDefaults()
    {
        using var settingRollback = new ScopedRollback<bool>(ref ModSettings_GiddyUp.automountDisabledByDefault);
        ModSettings_GiddyUp.automountDisabledByDefault = true;
        var disabledByDefault = new ExtendedPawnData(_rider);
        Expect.AreEqual(ExtendedPawnData.Automount.False, disabledByDefault.automount);

        ModSettings_GiddyUp.automountDisabledByDefault = false;
        var enabledByDefault = new ExtendedPawnData(_rider);
        Expect.AreEqual(ExtendedPawnData.Automount.Anyone, enabledByDefault.automount);
    }
    
    [Test]
    public void MountabilityRejectsInvalidSubjects()
    {
        Pawn? noAnimal = null;
        Expect.IsFalse(noAnimal.IsMountable(out var nullReason, _rider));
        Expect.AreEqual(Reason.NotInModOptions, nullReason);

        Expect.IsFalse(_rider.IsMountable(out var selfReason, _rider));
        Expect.AreEqual(Reason.NotAnimal, selfReason);

        Expect.IsFalse(_rider.IsMountable(out var humanReason, null, checkState: false, checkTraining: false));
        Expect.AreEqual(Reason.NotAnimal, humanReason);
    }

    [Test]
    public void AutomountAudienceHonorsAnimalPreference()
    {
        _mountData.automount = ExtendedPawnData.Automount.Anyone;
        Expect.IsTrue(_rider.IsAllowed(_mount));

        _mountData.automount = ExtendedPawnData.Automount.Colonists;
        Expect.IsTrue(_rider.IsAllowed(_mount));

        _mountData.automount = ExtendedPawnData.Automount.Slaves;
        Expect.IsFalse(_rider.IsAllowed(_mount));

        _mountData.automount = ExtendedPawnData.Automount.False;
        Expect.IsFalse(_rider.IsAllowed(_mount));
    }

    [Test]
    public void FailedMountabilityCheckClearsStaleReservation()
    {
        var originalExtensions = _mount.def.modExtensions;
        _riderData.ReservedMount = _mount;
        _mountData.ReservedBy = _rider;
        _mount.def.modExtensions = [new NotMountable()];
        GiddyUp.Setup.BuildMountCache();

        try
        {
            Expect.IsFalse(_mount.IsStillMountable(_rider, out var reason));
            Expect.AreEqual(Reason.NotInModOptions, reason);
            Expect.IsNull(_riderData.ReservedMount);
            Expect.IsNull(_mountData.ReservedBy);
        }
        finally
        {
            _mount.def.modExtensions = originalExtensions;
            GiddyUp.Setup.BuildMountCache();
        }
    }

    [Test]
    public void MountAndDrawCachesHonorExtensionsAndInversions()
    {
        var originalExtensions = _mount.def.modExtensions;
        var originalMountInversions = ModSettings_GiddyUp.invertMountingRules;
        var originalDrawInversions = ModSettings_GiddyUp.invertDrawRules;
        _mount.def.modExtensions = [new Mountable(), new DrawInFront()];
        ModSettings_GiddyUp.invertMountingRules = [];
        ModSettings_GiddyUp.invertDrawRules = [];

        try
        {
            GiddyUp.Setup.BuildMountCache();
            Expect.IsTrue(ModSettings_GiddyUp.MountableCache.Contains(_mount.def.shortHash));
            Expect.IsTrue(ModSettings_GiddyUp.DrawRulesCache.Contains(_mount.def.shortHash));

            ModSettings_GiddyUp.invertMountingRules.Add(_mount.def.defName);
            ModSettings_GiddyUp.invertDrawRules.Add(_mount.def.defName);
            GiddyUp.Setup.BuildMountCache();
            Expect.IsFalse(ModSettings_GiddyUp.MountableCache.Contains(_mount.def.shortHash));
            Expect.IsFalse(ModSettings_GiddyUp.DrawRulesCache.Contains(_mount.def.shortHash));
        }
        finally
        {
            _mount.def.modExtensions = originalExtensions;
            ModSettings_GiddyUp.invertMountingRules = originalMountInversions;
            ModSettings_GiddyUp.invertDrawRules = originalDrawInversions;
            GiddyUp.Setup.BuildMountCache();
        }
    }

    [Test]
    public void CaravanSpeedSettingAddsAndRemovesGeneratedBonus()
    {
        using var speedRollback = new ScopedRollback<bool>(ref ModSettings_GiddyUp.giveCaravanSpeed);
        var originalStats = _mount.def.statBases == null ? null : new List<StatModifier>(_mount.def.statBases);
        try
        {
            _mount.def.statBases = originalStats?
                .Where(stat => stat.stat != StatDefOf.CaravanRidingSpeedFactor)
                .ToList();
            ModSettings_GiddyUp.giveCaravanSpeed = true;
            GiddyUp.Setup.CalculateCaravanSpeed(_mount.def);
            var bonus = _mount.def.GetStatValueAbstract(StatDefOf.CaravanRidingSpeedFactor);

            ModSettings_GiddyUp.giveCaravanSpeed = false;
            GiddyUp.Setup.CalculateCaravanSpeed(_mount.def);
            var baseline = _mount.def.GetStatValueAbstract(StatDefOf.CaravanRidingSpeedFactor);

            Expect.GreaterThan(bonus, 1f);
            Expect.IsTrue(Math.Abs(baseline - 1.00001f) < 0.0001f,
                $"Expected generated caravan speed baseline, got {baseline}");
        }
        finally
        {
            _mount.def.statBases = originalStats;
        }
    }
}
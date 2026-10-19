using System.Collections;
using DevTools.Testing;
using GiddyUp;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace GiddyUpCore.Tests;

// [TestFixture(TestType.Playing)]
public class ModuleCaravan : RiderAndMountTest
{
    protected override Faction FactionOfPawns => Faction.OfPlayer;
    
    [Test]
    public IEnumerator TraderCaravanMounts()
    {
        // TODO MountUtility rolls 1..100 and skips when chance <= roll; 101 guarantees this integration path.
        var originalEnemyMountChancePreInd = ModSettings_GiddyUp.enemyMountChancePreInd;
        ModSettings_GiddyUp.enemyMountChancePreInd = 101;
        try
        {
            var map = Find.CurrentMap;
            var incident = IncidentDefOf.TraderCaravanArrival;
            var parms = StorytellerUtility.DefaultParmsNow(incident.category, map);

            parms.target = map;
            parms.forced = true;
            parms.points = 1000;
            var traderFaction = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(x => 
                    !x.IsPlayer && !x.HostileTo(Faction.OfPlayer) &&
                    x is { Hidden: false, defeated: false } &&
                    x.def.caravanTraderKinds.Any() && x.def.techLevel < TechLevel.Industrial);
            Expect.IsNotNull(traderFaction, "Unable to find a pre-industrial trader faction");
            parms.faction = traderFaction;

            var pawnsBeforeIncident = map.mapPawns.AllPawns.ToHashSet();
            Rand.PushState(161803);
            
                Expect.IsTrue(incident.Worker.CanFireNow(parms), "Unable to fire trade caravan spawn");
                Expect.IsTrue(incident.Worker.TryExecute(parms), "Failed to spawn trade caravan");
            

            yield return new WaitUntilTimeout(() => map.mapPawns.AllPawns.Any(trader =>
                !pawnsBeforeIncident.Contains(trader) && trader.Faction == traderFaction &&
                trader.RaceProps.Humanlike && trader.Spawned));
            yield return new WaitTicks(10);

            var traders = map.mapPawns.AllPawns
                .Where(trader => !pawnsBeforeIncident.Contains(trader) && trader.Faction == traderFaction &&
                                 trader.RaceProps.Humanlike && trader.Spawned).ToList();
            Expect.IsNotEmpty(traders, "The trader incident spawned no traders");
            Expect.IsTrue(traders.Any(trader => trader.IsMounted()), "No spawned traders were mounted");

            foreach (var trader in traders.Where(trader => trader.IsMounted()))
            {
                var traderData = trader.GetExtendedPawnData();
                trader.Dismount(traderData.Mount, traderData);
            }
            yield return new WaitTicks(1);

            var lord = traders[0].GetLord();
            traders[0].mindState.traderDismissed = true;
            yield return new WaitUntilTimeout(() => lord.CurLordToil is LordToil_ExitMapAndEscortCarriers);
            yield return new WaitUntilTimeout(() => traders.All(trader => !trader.Spawned), GenDate.TicksPerHour * 5);
        }
        finally
        {
            Rand.PopState();
            ModSettings_GiddyUp.enemyMountChancePreInd = originalEnemyMountChancePreInd;
        }
    }

    [Test]
    public IEnumerator PlayerCaravanMounts()
    {
        _riderData.ReservedMount = _mount;
        _mountData.ReservedBy = _rider;
        
        var map = Find.CurrentMap;
        try
        {
            CaravanFormingUtility.StartFormingCaravan([_rider, _mount], [], Faction.OfPlayer, [],
                map.Center, map.AllCells.MaxBy(x => (x - map.Center).LengthManhattan), map.Tile,
                map.Tile);
            yield return _rider.WaitJob(ResourceBank.JobDefOf.Mount);
            yield return new WaitUntilTimeout(_rider.IsMounted);
        }
        finally
        {
            if(map.lordManager.TryGetLordByJob(Faction.OfPlayer, out LordJob_FormAndSendCaravan caravan))
                caravan.Dispose();
        }
    }
}
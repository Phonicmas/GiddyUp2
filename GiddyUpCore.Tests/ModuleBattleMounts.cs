using System.Collections;
using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using RimWorld;
using Verse;

namespace GiddyUpCore.Tests;

[TestFixture(TestType.Playing)]
public class ModuleBattleMounts
{
    private HashSet<Pawn> pawnsBeforeTest = [];

    [OneTimeSetUp]
    public void OneTimeSetup() => Tweaker.SuperFast = true;

    [OneTimeTearDown]
    public void OneTimeTearDown() => Tweaker.SuperFast = false;

    [SetUp]
    public void Setup() => pawnsBeforeTest = Find.CurrentMap.mapPawns.AllPawns.ToHashSet();

    [TearDown]
    public void Teardown() => Find.CurrentMap.mapPawns.AllPawns
        .Where(x => !pawnsBeforeTest.Contains(x)).ToList()
        .ForEach(x => x.Destroy());
    
    [Test]
    public IEnumerator RaidersSpawnMounted()
    {
        var faction = Current.Game.World.factionManager.AllFactions
            .FirstOrDefault(x =>
                x.HostileTo(Faction.OfPlayer) && x.def.techLevel < TechLevel.Industrial);
        Expect.IsNotNull(faction, "Unable to find PreInd faction that is hostile to player to test with");
        
        var originalAnimalFlee = faction!.def.animalsFleeDanger;
        faction.def.animalsFleeDanger = false;
        // TODO MountUtility rolls 1..100 and skips when chance <= roll; 101 guarantees this integration path.
        var originalEnemyMountChancePreInd = ModSettings_GiddyUp.enemyMountChancePreInd;
        ModSettings_GiddyUp.enemyMountChancePreInd = 101;
        var incidentParms = new IncidentParms()
        {
            faction = faction,
            target = Find.CurrentMap,
            points = 5000,
            forced = true,
            raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
        };
        var raidWorker = (IncidentWorker_Raid)IncidentDefOf.RaidEnemy.Worker;
        List<Pawn> raiders;
        Rand.PushState(314159);
        try
        {
            Expect.IsTrue(raidWorker.TryGenerateRaidInfo(incidentParms, out raiders),
                "Unable to generate raid pawns");
        }
        finally
        {
            Rand.PopState();
            ModSettings_GiddyUp.enemyMountChancePreInd = originalEnemyMountChancePreInd;
            faction.def.animalsFleeDanger = originalAnimalFlee;
        }
        yield return new WaitUntilTimeout(() => raiders.Any(raider => raider.Spawned));
        Expect.Any(raiders, raider => raider.IsMounted(), "No raiders are mounted");
    }
    
    [Test]
    public IEnumerator EnemyFactionAmbushSpawnsMounted()
    {
        // TODO MountUtility rolls 1..100 and skips when chance <= roll; 101 guarantees this integration path.
        var originalEnemyMountChancePreInd = ModSettings_GiddyUp.enemyMountChancePreInd;
        ModSettings_GiddyUp.enemyMountChancePreInd = 101;
        var faction = Current.Game.World.factionManager.AllFactions
            .FirstOrDefault(x =>
                x.HostileTo(Faction.OfPlayer) && x.def.techLevel < TechLevel.Industrial);
        Expect.IsNotNull(faction, "Unable to find PreInd faction that is hostile to player to test with");

        var ambush = DefDatabase<IncidentDef>.GetNamed("Ambush");
        var parms = new IncidentParms
        {
            target = Find.CurrentMap,
            points = 5000,
            forced = true,
            sendLetter = false,
            faction = faction,
            raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
        };

        var pawnsBeforeIncident = Find.CurrentMap.mapPawns.AllPawns.ToHashSet();
        Rand.PushState(271828);
        try
        {
            Expect.IsTrue(
                ambush.Worker.CanFireNow(parms),
                "Ambush cannot fire on the current map");

            Expect.IsTrue(
                ambush.Worker.TryExecute(parms),
                "Failed to execute ambush");
        }
        finally
        {
            Rand.PopState();
            ModSettings_GiddyUp.enemyMountChancePreInd = originalEnemyMountChancePreInd;
        }

        yield return new WaitUntilTimeout(() =>
            Find.CurrentMap.mapPawns.AllPawns.Any(pawn =>
                !pawnsBeforeIncident.Contains(pawn) && pawn.HostileTo(Faction.OfPlayer) && pawn.Spawned));

        Expect.Any(
            Find.CurrentMap.mapPawns.AllPawns,
            pawn => !pawnsBeforeIncident.Contains(pawn) && pawn.HostileTo(Faction.OfPlayer) && pawn.IsMounted(),
            "No ambush pawns were mounted");

    }
}
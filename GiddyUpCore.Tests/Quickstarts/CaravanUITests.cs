using System.Collections;
using System.Reflection;
using DevTools;
using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using GiddyUpCaravan.Harmony;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace GiddyUpCore.Tests;

[Quickstart("Caravan")]
// [TestFixture(TestType.Playing)]
public class CaravanUITests
{
    private Dialog_FormCaravan _dialog = null!;
    private List<Pawn> _horses = null!;
    private List<Pawn> _workers = null!;

    private static string ArtefactDir => Path.Combine(Mod_GiddyUp.Instance.Content.RootDir,
        "GiddyUpCore.Tests", "artefacts");

    private static MethodInfo Patch => AccessTools.Method(typeof(Patch_TransferableOneWayWidget),
        nameof(Patch_TransferableOneWayWidget.AddMountSelector));
    
    [SetUp]
    public void Setup()
    {
        _workers = TestPawns.Worker.SpawnMany(5);
        _horses = PawnKindDef.Named("Horse").AsBuilder().WithFaction(Faction.OfPlayer).SpawnMany(5);

        _dialog = new Dialog_FormCaravan(Find.CurrentMap);
        Find.WindowStack.Add(_dialog);
        
        var dest = Find.World.worldObjects.Settlements.Where(x => x.Tile.LayerDef == PlanetLayerDefOf.Surface && x.Faction != Faction.OfPlayer)
            .MinBy(x => x.Tile.Layer.ApproxDistanceInTiles(Find.CurrentMap.Tile));
        Find.WorldRoutePlanner.TryAddWaypoint(dest.Tile);
        _dialog.Notify_ChoseRoute(Find.WorldRoutePlanner.waypoints[1].Tile);
        Find.WindowStack.Add(_dialog);
        Find.WorldRoutePlanner.Stop();

        _dialog.transferables.FirstOrDefault(x => x.AnyThing == _workers[0]).ForceToDestination(1);
        _dialog.transferables.FirstOrDefault(x => x.AnyThing == _horses[0]).ForceToDestination(1);
        _dialog.transferables.FirstOrDefault(x => x.AnyThing == _horses[1]).ForceToDestination(1);
    }

    // [Test]
    // public IEnumerator SetRiderButtonRenders()
    // {
    //     yield return ScreenshotCamera.TakeUIScreenshot(Path.Combine(ArtefactDir, nameof(SetRiderButtonRenders) + ".png"), _dialog, Patch);
    // }
}
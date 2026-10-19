using System.Collections;
using DevTools;
using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using GiddyUpCore.Tests.Utils;
using RimWorld;
using UnityEngine;
using Verse;

namespace GiddyUpCore.Tests;

[Quickstart("Mountable")]
public class MountableQuickstart
{
    [SetUp]
    public void SpawnColonists()
    {
        var wallDef = ThingDefOf.Wall;
        var wallStuff = GenStuff.AllowedStuffsFor(wallDef).ToList();
        var pawns = PawnKindDefOf.Colonist
            .AsBuilder()
            .WithName("Rider")
            .WithWorkMaxed(WorkTypeDefOf.Hunting)
            .WithMount(PawnKindDef.Named("Horse"))
            .WithPostSpawn((pawn, index) =>
            {
                var offset = (int)(index % 2 == 0 ? -(index / 2) : Math.Ceiling(index / 2f));
                pawn.Position += new IntVec3(offset, 0, 0);
                pawn.drafter.Drafted = true;

                var block = pawn.Position + new IntVec3(0, 0, 1);
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(wallDef,
                    wallStuff[index % wallStuff.Count]), block, pawn.Map, ThingPlaceMode.Direct);
            })
            .SpawnMany(5);
        // var minX = pawns.Min(x => x.Position.x);
        // var maxX = pawns.Max(x => x.Position.x);
        // var minZ = pawns.Min(x => x.Position.z);
        // var maxZ = pawns.Max(x => x.Position.z);
        //
        // var rect = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        //
        // var cells = rect
        //     .ClipInsideMap(Find.CurrentMap);
        
        // yield return ScreenshotCamera.TakeScreenshot(Path.Combine(Mod_GiddyUp.Instance.Content.RootDir, "GiddyUpCore.Tests", "artefacts", nameof(SpawnColonists) + ".png"), pawns, cells);
    }

    [TearDown]
    public void Cleanup()
    {
        
    }
}
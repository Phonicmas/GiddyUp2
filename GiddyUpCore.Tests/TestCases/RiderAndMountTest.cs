using DevTools.Testing;
using DevTools.Testing.Utils;
using GiddyUp;
using RimWorld;
using Verse;

namespace GiddyUpCore.Tests;

public abstract class RiderAndMountTest
{
    protected Pawn _rider = null!, _mount = null!;
    protected ExtendedPawnData _riderData = null!, _mountData = null!;

    protected abstract Faction FactionOfPawns { get; }
    protected virtual PawnKindDef AnimalKind => PawnKindDef.Named("Horse");
    private bool animalsFleeDanger;
    
    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        Tweaker.SuperFast = true;
        if (FactionOfPawns == Faction.OfPlayer) return;
        animalsFleeDanger = FactionOfPawns.def.animalsFleeDanger;
        FactionOfPawns.def.animalsFleeDanger = false;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Tweaker.SuperFast = false;
        FactionOfPawns.def.animalsFleeDanger = animalsFleeDanger;
    }

    [SetUp]
    public void Setup()
    {
        var map = Find.CurrentMap;
        var cell = map.AllCells.MaxBy(x => x.y);
        _rider = PawnKindDefOf.Colonist
            .AsBuilder()
            .WithModifer(pawn => pawn.health.Reset())
            .WithFaction(FactionOfPawns)
            .WithValidator(pawn =>
                pawn.story.AllBackstories
                    .All(backstory => backstory.DisabledWorkTypes.Count == 0))
            .Spawn();
        
        _mount = PawnGenerator.GeneratePawn(AnimalKind, FactionOfPawns);
        GenSpawn.Spawn(_mount, map.Center, map);
        
        if(_mount.RaceProps.IsMechanoid)
        {
            _rider.health.AddHediff(HediffDefOf.MechlinkImplant);
            _rider.relations.AddDirectRelation(PawnRelationDefOf.Overseer, _mount);
        }

        _riderData = _rider.GetExtendedPawnData();
        _mountData = _mount.GetExtendedPawnData();
    }

    [TearDown]
    public void TearDown()
    {
        _rider.Destroy();
        _mount.Destroy();
        
        var map = Find.CurrentMap; 
        map.AllCells.ToList().ForEach(x => FilthMaker.RemoveAllFilth(x, map));

        var danglingPawns = map.mapPawns.PawnsInFaction(Faction.OfPlayer);
        Expect.IsEmpty(danglingPawns, $"Map wasn't cleared: {string.Join(", ", danglingPawns.Select(x => x.LabelShortCap))}");
        map.mapPawns.PawnsInFaction(Faction.OfPlayer).ForEach(x => x.Destroy());
    }

    protected void ValidateIsMounted(string? context = null, bool checkReservations = false, bool checkBindings = true)
    {
        var suffix = context is null ? string.Empty : $" ({context})";
        Expect.IsTrue(_rider.IsMounted(), "Rider is not mounted" + suffix);
        Expect.IsTrue(_mount.IsMountedAnimal(out var actualRider), "Animal was not actually mounted" + suffix);
        if(checkBindings)
        {
            Expect.AreEqual(_riderData.Mount, _mount,
                "Rider's data showed a different animal" + suffix);
            Expect.AreEqual(_mountData.Rider, _rider,
                "Animal's data showed a different rider" + suffix);
            Expect.AreEqual(_rider, actualRider, "Animal was mounted by another rider" + suffix);
        }
        Expect.AreEqual(ResourceBank.JobDefOf.Mounted, _mount.CurJobDef, "Animal does not have mounted job");

        if (!checkReservations)
            return;
        
        Expect.AreEqual(_rider, _mountData.ReservedBy, "Animal is reserved by another rider" + suffix);
        Expect.AreEqual(_mount, _riderData.ReservedMount,  "Animal is reserved by another rider" + suffix);
    }

    protected void ValidateIsNotMounted(string? context = null, bool checkReservations = false)
    {
        var suffix = context is null ? string.Empty : $" ({context})";
        Expect.IsFalse(_rider.IsMounted(), "Rider is still mounted" + suffix);
        Expect.IsFalse(_mount.IsMountedAnimal(out _), "Animal is still mounted" + suffix);
        Expect.IsNull(_riderData.Mount, "Rider's data showed a mount after dismounting" + suffix);
        Expect.IsNull(_mountData.Rider, "Animal's data showed a rider after dismounting" + suffix);
        Expect.AreNotEqual(ResourceBank.JobDefOf.Mounted, _mount.CurJobDef, "Animal still has mounted job");

        if (!checkReservations)
            return;
        
        Expect.IsNull(_riderData.ReservedMount);
        Expect.IsNull(_mountData.ReservedBy);
    }
}
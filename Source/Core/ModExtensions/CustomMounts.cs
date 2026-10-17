using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace GiddyUp;

//For PawnKindDef
public class CustomMounts : DefModExtension
{
    public int mountChance = 0;
    public Dictionary<PawnKindDef, int> possibleMounts = new();
    [MayRequire("Ingendum.AnimalApparelFramework")]
    public List<CustomMountApparel> apparel = new();

    public override void ResolveReferences(Def parentDef)
    {
        base.ResolveReferences(parentDef);
        possibleMounts.RemoveAll(x =>
        {
            //Was "!mechanoidsEnabled || IsMechanoid", which kept everything when mechs were disabled
            if (x.Key.RaceProps.Animal ||
                (ModSettings_GiddyUp.mechanoidsEnabled && x.Key.RaceProps.IsMechanoid))
                return false;

            Log.Warning(
                $"Attempted to add non-animal/non-mech '{x.Key.LabelCap}' to possible mounts for '{parentDef.LabelCap}', only animals/mechs are allowed.");
            return true;
        });
    }
}

public class CustomMountApparel
{
    public ThingDef ThingDef;
    public ThingDef? Stuff;
}
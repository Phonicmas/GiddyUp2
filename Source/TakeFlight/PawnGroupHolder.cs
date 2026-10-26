using System.Collections.Generic;
using Verse;

namespace GiddyUpTakeFlight;

/// <summary>
/// Read-only stand-in for transport pod contents, so vanilla's destination menu can inspect pawns that are still
/// on the map or in a caravan. Never takes ownership of the pawns.
/// </summary>
public class PawnGroupHolder : IThingHolder
{
    private readonly PawnListView view;

    public PawnGroupHolder(IEnumerable<FlyingPair> pairs)
    {
        var pawns = new List<Thing>();
        foreach (var pair in pairs)
        {
            pawns.Add(pair.mount);
            if (pair.HasRider)
                pawns.Add(pair.rider);
        }
        view = new PawnListView(this, pawns);
    }

    public IThingHolder? ParentHolder => null;

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
    }

    public ThingOwner GetDirectlyHeldThings() => view;

    private class PawnListView(IThingHolder owner, List<Thing> things) : ThingOwner(owner)
    {
        public override int Count => things.Count;

        public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks = true) => 0;

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true) => false;

        public override int IndexOf(Thing item) => things.IndexOf(item);

        public override bool Remove(Thing item) => false;

        public override Thing GetAt(int index) => things[index];
    }
}

using GiddyUp;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace GiddyUpCore.SaddleUp
{
    public record MountPairing(Pawn Rider, Pawn Mount, float Distance);

    //Collects the riders from one frame's worth of gizmo clicks and pairs them with the closest free mounts. Runs from
    //MapComponentUpdate so it also works while paused; it used to toggle the game's pause state to get a tick in.
    public class Coordinator(Map map) : MapComponent(map)
    {
        private bool WakeUp = false;
        private readonly List<Pawn> MonitoredPawns = new();

        public override void MapComponentUpdate()
        {
            if (!WakeUp)
                return;

            WakeUp = false;

            var pawns = MonitoredPawns.Where(x => x.Spawned && x.Map == map).ToList();
            MonitoredPawns.Clear();
            if (pawns.Count == 0)
                return;

            //Only this map's spawned colony animals (and mechs) that could ever be ridden and aren't ridden right now
            var mounts = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
                .Where(x => (x.RaceProps.Animal || (ModSettings_GiddyUp.mechanoidsEnabled && x.RaceProps.IsMechanoid)) &&
                            x.IsEverMountable() && !x.IsMountedAnimal())
                .ToList();

            // Rider, Mount
            var mappedMounts = new Dictionary<Pawn, Pawn>();

            // Master
            foreach (var pawn in pawns)
            {
                var masterOf = mounts.Where(mount => mount.playerSettings?.Master == pawn && CanRide(pawn, mount)).ToArray();
                if (!masterOf.Any())
                    continue;

                var closest = masterOf.MinBy(mount => Vector3.Distance(pawn.DrawPos, mount.DrawPos));
                if (closest == null)
                    continue;

                mappedMounts[pawn] = closest;
                mounts.Remove(closest);
            }

            pawns.RemoveAll(mappedMounts.ContainsKey);
            //Next closest
            var mountDistances = pawns
                .SelectMany(rider =>
                    mounts.Where(mount => CanRide(rider, mount))
                        .Select(mount => new MountPairing(rider, mount, Vector3.Distance(rider.DrawPos, mount.DrawPos))))
                .OrderBy(x => x.Distance)
                .ToList();

            var usedRiders = new HashSet<Pawn>();
            var usedMounts = new HashSet<Pawn>();

            foreach (var pairing in mountDistances)
            {
                if (usedRiders.Contains(pairing.Rider) || usedMounts.Contains(pairing.Mount))
                    continue;

                mappedMounts[pairing.Rider] = pairing.Mount;
                usedRiders.Add(pairing.Rider);
                usedMounts.Add(pairing.Mount);
            }

            foreach (var (rider, mount) in mappedMounts)
            {
                var job = new Job(ResourceBank.JobDefOf.Mount, mount) { count = 1 };
                rider.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }

            if (mappedMounts.Count > 0)
                SoundDef.Named("horsesnore").PlayOneShotOnCamera();
        }

        private static bool CanRide(Pawn rider, Pawn mount) =>
            mount.IsMountable(out _, rider, checkTraining: mount.RaceProps.Animal);

        public void MountPawn(Pawn pawn)
        {
            MonitoredPawns.Add(pawn);
            WakeUp = true;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

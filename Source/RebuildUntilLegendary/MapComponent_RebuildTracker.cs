using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RebuildUntilLegendary
{
    /// <summary>
    /// Per-map storage of rebuild jobs plus the tick driver that keeps each loop
    /// going: a finished building below the target quality is deconstructed, the
    /// empty spot gets a fresh blueprint with the same def/stuff/rotation/style,
    /// and a finished building at or above the target quality ends the loop.
    /// </summary>
    public class MapComponent_RebuildTracker : MapComponent, IDisposable
    {
        private const int CheckIntervalTicks = 15;

        private const int RetryIntervalTicks = 250;

        public List<RebuildJob> Jobs = new List<RebuildJob>();

        /// <summary>Components of all live maps. Kept tiny by design: the workgiver
        /// patches consult it on every construction job scan.</summary>
        private static readonly List<MapComponent_RebuildTracker> ActiveTrackers = new List<MapComponent_RebuildTracker>();

        public MapComponent_RebuildTracker(Map map) : base(map)
        {
            ActiveTrackers.Add(this);
        }

        void IDisposable.Dispose()
        {
            ActiveTrackers.Remove(this);
        }

        public static MapComponent_RebuildTracker GetFor(Map map)
        {
            return map?.GetComponent<MapComponent_RebuildTracker>();
        }

        /// <summary>Finds the restricted job matching a blueprint/frame/building,
        /// across all live maps. Used by the workgiver patches, so it must stay
        /// cheap: with no restricted jobs anywhere it returns almost immediately.</summary>
        public static bool IsRestrictedThing(Thing t, out RebuildJob job)
        {
            job = null;
            if (t == null || ActiveTrackers.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < ActiveTrackers.Count; i++)
            {
                MapComponent_RebuildTracker tracker = ActiveTrackers[i];
                if (tracker.map != t.MapHeld || tracker.Jobs.Count == 0)
                {
                    continue;
                }
                for (int j = 0; j < tracker.Jobs.Count; j++)
                {
                    RebuildJob candidate = tracker.Jobs[j];
                    if (candidate.builder != null && candidate.Matches(t))
                    {
                        job = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        public RebuildJob FindJob(Building building)
        {
            for (int i = 0; i < Jobs.Count; i++)
            {
                if (Jobs[i].cell == building.Position && Jobs[i].buildDef == building.def)
                {
                    return Jobs[i];
                }
            }
            return null;
        }

        public RebuildJob FindJobForThing(Thing t)
        {
            for (int i = 0; i < Jobs.Count; i++)
            {
                if (Jobs[i].Matches(t))
                {
                    return Jobs[i];
                }
            }
            return null;
        }

        public void Register(Building building, QualityCategory target, Pawn builder)
        {
            RebuildJob existing = FindJob(building);
            if (existing != null)
            {
                DebugLog.Log("not activating " + building.LabelCap + " at " + existing.DescribeCell()
                    + ": already rebuilding until " + existing.DescribeTarget() + ".");
                return;
            }
            if (building.TryGetQuality(out QualityCategory current) && current >= target)
            {
                Messages.Message("RebuildUntilLegendary.AlreadyGoodEnough".Translate(
                    building.LabelCap, target.GetLabel().CapitalizeFirst()),
                    building, MessageTypeDefOf.NeutralEvent);
                DebugLog.Log("not activating " + building.LabelCap + " at " + building.Position
                    + ": already " + current + ", target was " + target + ".");
                return;
            }
            RebuildJob job = new RebuildJob
            {
                cell = building.Position,
                rotation = building.Rotation,
                buildDef = building.def,
                stuff = building.Stuff,
                styleSourcePrecept = building.StyleSourcePrecept,
                styleDef = building.StyleDef,
                targetQuality = target,
                builder = builder,
                occupantIdNumber = building.thingIDNumber
            };
            if (building is Building_Storage storage)
            {
                job.CaptureStorageSettings(storage.GetStoreSettings());
            }
            Jobs.Add(job);
            Messages.Message("RebuildUntilLegendary.Activated".Translate(
                building.LabelCap, job.DescribeTarget(), job.DescribeBuilder()),
                building, MessageTypeDefOf.NeutralEvent);
            DebugLog.Log("activated " + job.DescribeBuilding() + " at " + job.DescribeCell()
                + ": target " + target + ", builder " + job.DescribeBuilder()
                + ", rotation " + job.rotation + ", stuff " + (job.stuff != null ? job.stuff.label : "none") + ".");
        }

        public void Unregister(RebuildJob job, string reason)
        {
            if (!Jobs.Remove(job))
            {
                return;
            }
            DebugLog.Log("stopped rebuilding " + job.DescribeBuilding() + " at " + job.DescribeCell()
                + " after " + job.attempts + " attempt(s): " + reason + ".");
        }

        /// <summary>Called from the Thing.Destroy prefix, while the thing still has a
        /// map. Covers every way a tracked blueprint, frame or building can vanish.</summary>
        public void NotifyDestroying(Thing t, DestroyMode mode)
        {
            RebuildJob job = FindJobForThing(t);
            if (job == null)
            {
                return;
            }
            switch (mode)
            {
                case DestroyMode.Cancel:
                    Unregister(job, "the blueprint was canceled");
                    Messages.Message("RebuildUntilLegendary.StoppedCanceled".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                    return;
                case DestroyMode.Vanish when t is Blueprint || t is Frame:
                    // Normal lifecycle handover: blueprint becomes frame, frame becomes
                    // the finished building. The new occupant is picked up by the tick.
                    DebugLog.VerboseLog(job.DescribeBuilding() + " at " + job.DescribeCell()
                        + " advanced to the next construction stage.");
                    return;
                case DestroyMode.FailConstruction when t is Frame:
                    // Vanilla places a replacement blueprint itself; nothing to do.
                    DebugLog.Log("construction of " + job.DescribeBuilding() + " at " + job.DescribeCell() + " failed; vanilla re-placed the blueprint.");
                    return;
                case DestroyMode.WillReplace:
                    // The player is replacing this building with another blueprint.
                    DebugLog.Log(job.DescribeBuilding() + " at " + job.DescribeCell() + " is being replaced by a new blueprint.");
                    return;
                default:
                    job.pendingPlacement = true;
                    job.retryAtTick = 0;
                    if (t is Building_Storage storage)
                    {
                        job.CaptureStorageSettings(storage.GetStoreSettings());
                    }
                    DebugLog.Log(job.DescribeBuilding() + " at " + job.DescribeCell() + " destroyed (" + mode
                        + ") - will place a new blueprint.");
                    return;
            }
        }

        public override void MapComponentTick()
        {
            if (Jobs.Count == 0 || Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                RebuildJob job = Jobs[i];
                ValidateBuilder(job);
                Thing occupant = FindOccupant(job);
                if (occupant != null)
                {
                    job.NotifyOccupantSeen();
                    job.pendingPlacement = false;
                    job.occupantIdNumber = occupant.thingIDNumber;
                    if (occupant is Building building)
                    {
                        EvaluateFinishedBuilding(job, building);
                    }
                    continue;
                }
                if (job.pendingPlacement)
                {
                    if (now >= job.retryAtTick)
                    {
                        TryPlaceBlueprint(job, now);
                    }
                    continue;
                }
                // No occupant and no pending placement: the building left its cell
                // without a destroy event (minified, caravaned, moved elsewhere).
                if (ForeignEdificeAt(job))
                {
                    Unregister(job, "the spot is now occupied by something else");
                    Messages.Message("RebuildUntilLegendary.StoppedOccupied".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                    continue;
                }
                if (BuildingWasMovedAway(job))
                {
                    Unregister(job, "the building was moved away");
                    Messages.Message("RebuildUntilLegendary.StoppedMoved".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                    continue;
                }
                job.NotifyOccupantMissing(now, out bool expired);
                if (expired)
                {
                    Unregister(job, "the building disappeared");
                    Messages.Message("RebuildUntilLegendary.StoppedGone".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                }
            }
        }

        private void ValidateBuilder(RebuildJob job)
        {
            if (job.builder != null && (job.builder.Discarded || job.builder.Dead
                || (job.builder.Spawned && job.builder.Map != map) || job.builder.MapHeld == null))
            {
                DebugLog.Log("chosen builder " + job.builder.LabelShortCap + " for " + job.DescribeBuilding()
                    + " at " + job.DescribeCell() + " is unavailable; lifting the restriction.");
                job.builder = null;
                Messages.Message("RebuildUntilLegendary.BuilderGone".Translate(job.DescribeBuilding()),
                    MessageTypeDefOf.NeutralEvent);
            }
        }

        private void EvaluateFinishedBuilding(RebuildJob job, Building building)
        {
            if (!building.TryGetQuality(out QualityCategory quality))
            {
                DebugLog.Warn("rebuilding " + job.DescribeBuilding() + " at " + job.DescribeCell()
                    + " stopped: the finished building has no quality (changed mods?).");
                Unregister(job, "the building no longer reports a quality");
                return;
            }
            DebugLog.VerboseLog("quality check: " + job.DescribeBuilding() + " at " + job.DescribeCell()
                + " rolled " + quality + " (target " + job.targetQuality + ", attempt " + (job.attempts + 1) + ").");
            if (quality >= job.targetQuality)
            {
                Unregister(job, "target quality reached: " + quality);
                Messages.Message("RebuildUntilLegendary.Success".Translate(
                    building.LabelCap, job.DescribeTarget()),
                    building, MessageTypeDefOf.PositiveEvent);
                return;
            }
            if (!building.DeconstructibleBy(Faction.OfPlayer).Accepted)
            {
                Unregister(job, "the building cannot be deconstructed");
                Messages.Message("RebuildUntilLegendary.StoppedNotDeconstructable".Translate(job.DescribeBuilding()),
                    MessageTypeDefOf.NeutralEvent);
                return;
            }
            job.attempts++;
            DebugLog.Log("quality " + quality + " below " + job.targetQuality + " - deconstructing "
                + job.DescribeBuilding() + " at " + job.DescribeCell() + " (attempt " + job.attempts + ").");
            building.Destroy(DestroyMode.Deconstruct);
            if (!building.Destroyed)
            {
                // Destroy was refused (e.g. a non-destroyable def, or another mod
                // blocked it). Stop instead of hammering the same call every check.
                Unregister(job, "the building could not be destroyed");
            }
        }

        private Thing FindOccupant(RebuildJob job)
        {
            if (!job.cell.InBounds(map))
            {
                return null;
            }
            List<Thing> things = job.cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if ((thing is Blueprint || thing is Frame || thing is Building) && job.Matches(thing))
                {
                    return thing;
                }
            }
            return null;
        }

        private void TryPlaceBlueprint(RebuildJob job, int now)
        {
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(
                job.buildDef, job.cell, job.rotation, map, godMode: false, null, null, job.stuff);
            if (!report.Accepted)
            {
                if (ForeignEdificeAt(job))
                {
                    Unregister(job, "the spot is occupied by something else");
                    Messages.Message("RebuildUntilLegendary.StoppedOccupied".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                    return;
                }
                job.retryAtTick = now + RetryIntervalTicks;
                DebugLog.VerboseLog("cannot place the blueprint for " + job.DescribeBuilding() + " at "
                    + job.DescribeCell() + " yet (" + report.Reason + "), retrying later.");
                return;
            }
            Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(
                job.buildDef, job.cell, map, job.rotation, Faction.OfPlayer,
                job.stuff, job.styleSourcePrecept, job.styleDef);
            job.pendingPlacement = false;
            if (blueprint != null)
            {
                job.occupantIdNumber = blueprint.thingIDNumber;
                if (blueprint is Blueprint_Storage storageBlueprint && job.storageSettings != null)
                {
                    storageBlueprint.settings = new StorageSettings(storageBlueprint);
                    storageBlueprint.settings.CopyFrom(job.storageSettings);
                    DebugLog.Log("copied the storage settings onto the new blueprint.");
                }
            }
            DebugLog.Log("placed blueprint " + (blueprint != null ? blueprint.ThingID : "???")
                + " for " + job.DescribeBuilding() + " at " + job.DescribeCell()
                + ", rotation " + job.rotation + ", target " + job.targetQuality
                + ", builder " + job.DescribeBuilder() + ".");
        }

        /// <summary>True when a finished building of a different def occupies the
        /// tracked footprint - a sure sign the player wants the spot for something else.</summary>
        private bool ForeignEdificeAt(RebuildJob job)
        {
            CellRect rect = GenAdj.OccupiedRect(job.cell, job.rotation, job.buildDef.Size);
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Building building && !(things[i] is Frame)
                        && building.def != job.buildDef && building.def.IsEdifice())
                    {
                        DebugLog.VerboseLog("spot of " + job.DescribeBuilding() + " at " + job.DescribeCell()
                            + " is taken by " + building.LabelCap + ".");
                        return true;
                    }
                }
            }
            return false;
        }

        private bool BuildingWasMovedAway(RebuildJob job)
        {
            if (job.occupantIdNumber < 0)
            {
                return false;
            }
            // Minified (uninstalled, packed for a caravan): the building survives as
            // the inner thing of a MinifiedThing, so the def lister will not find it.
            List<Thing> minified = map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing);
            for (int i = 0; i < minified.Count; i++)
            {
                if (minified[i] is MinifiedThing wrapper && wrapper.InnerThing != null
                    && wrapper.InnerThing.thingIDNumber == job.occupantIdNumber)
                {
                    DebugLog.VerboseLog(job.DescribeBuilding() + " at " + job.DescribeCell()
                        + " was minified away.");
                    return true;
                }
            }
            List<Thing> things = map.listerThings.ThingsOfDef(job.buildDef);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].thingIDNumber == job.occupantIdNumber
                    && things[i] is Building building && !building.Destroyed)
                {
                    DebugLog.VerboseLog(job.DescribeBuilding() + " at " + job.DescribeCell()
                        + " still exists on this map, just not at its cell anymore.");
                    return true;
                }
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref Jobs, "jobs", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Jobs == null)
                {
                    Jobs = new List<RebuildJob>();
                }
                Jobs.RemoveAll(job => job == null);
            }
        }
    }
}

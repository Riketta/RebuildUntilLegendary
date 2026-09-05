using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
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

        /// <summary>Training mode cancels a frame once at most this fraction of its
        /// work remains left to do (i.e. at 99% completion).</summary>
        private const float TrainingInterruptAtWorkLeftFraction = 0.01f;

        /// <summary>How long a same-def occupant at the cell counts as the successor
        /// of a tracked blueprint/frame. Every vanilla handover (frame spawn, frame
        /// completion, vanilla re-placed blueprint) happens within the same tick as
        /// the preceding destroy, so a small window is enough.</summary>
        private const int SuccessorGraceTicks = 60;

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

        /// <summary>Starts a rebuild loop from a finished building, its in-progress
        /// frame or an already placed blueprint - all three resolve to the same
        /// buildable def, material and style the replacement blueprints reproduce.</summary>
        public void Register(Thing constructible, QualityCategory target, Pawn builder)
        {
            RebuildJob existing = FindJobForThing(constructible);
            if (existing != null)
            {
                DebugLog.Log("not activating " + constructible.LabelCap + " at " + existing.DescribeCell()
                    + ": already rebuilding until " + existing.DescribeTarget() + ".");
                return;
            }
            if (constructible is Building finished && !(constructible is Frame)
                && finished.TryGetQuality(out QualityCategory current) && current >= target)
            {
                Messages.Message("RebuildUntilLegendary.AlreadyGoodEnough".Translate(
                    finished.LabelCap, target.GetLabel().CapitalizeFirst()),
                    finished, MessageTypeDefOf.NeutralEvent);
                DebugLog.Log("not activating " + finished.LabelCap + " at " + finished.Position
                    + ": already " + current + ", target was " + target + ".");
                return;
            }
            RebuildJob job = new RebuildJob
            {
                cell = constructible.Position,
                rotation = constructible.Rotation,
                buildDef = RebuildJob.BuildDefOf(constructible),
                stuff = StuffOf(constructible),
                styleSourcePrecept = constructible.StyleSourcePrecept,
                styleDef = constructible.StyleDef,
                targetQuality = target,
                builder = builder,
                occupantIdNumber = constructible.thingIDNumber
            };
            CaptureStorageSettings(constructible, job);
            Jobs.Add(job);
            Messages.Message("RebuildUntilLegendary.Activated".Translate(
                constructible.LabelCap, job.DescribeTarget(), job.DescribeBuilder()),
                constructible, MessageTypeDefOf.NeutralEvent);
            DebugLog.Log("activated " + job.DescribeBuilding() + " at " + job.DescribeCell()
                + " from a " + constructible.GetType().Name + ": target " + target
                + ", builder " + job.DescribeBuilder()
                + ", rotation " + job.rotation + ", stuff " + (job.stuff != null ? job.stuff.label : "none") + ".");
        }

        /// <summary>Blueprints keep their material in stuffToUse, frames and finished
        /// buildings in the regular Thing.Stuff slot.</summary>
        private static ThingDef StuffOf(Thing constructible)
        {
            if (constructible is Blueprint_Build blueprint)
            {
                return blueprint.stuffToUse;
            }
            return constructible.Stuff;
        }

        private void CaptureStorageSettings(Thing constructible, RebuildJob job)
        {
            switch (constructible)
            {
                case Building_Storage storage:
                    job.CaptureStorageSettings(storage.GetStoreSettings());
                    break;
                case Blueprint_Storage storageBlueprint:
                    job.CaptureStorageSettings(storageBlueprint.settings);
                    break;
                case Frame frame:
                    job.CaptureStorageSettings(frame.GetStoreSettings());
                    break;
            }
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
        /// map. Covers every way a tracked blueprint, frame or building can vanish.
        /// Deconstruction and replacement by the player end the loop; the mod's own
        /// below-target re-roll and ordinary destructions keep it going.</summary>
        public void NotifyDestroying(Thing t, DestroyMode mode)
        {
            RebuildJob job = FindJobForThing(t);
            if (job == null)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            switch (mode)
            {
                case DestroyMode.Cancel when t is Frame && job.modInitiatedCancel:
                    // Our own training-mode interrupt: vanilla refunds the frame's
                    // full materials, and the loop continues with a fresh blueprint.
                    job.modInitiatedCancel = false;
                    job.pendingPlacement = true;
                    job.retryAtTick = 0;
                    DebugLog.Log(job.DescribeBuilding() + " at " + job.DescribeCell()
                        + " canceled at " + Mathf.RoundToInt(((Frame)t).PercentComplete * 100f)
                        + "% work (training mode) - materials refunded, will place a new blueprint.");
                    return;
                case DestroyMode.Cancel:
                    Unregister(job, "canceled by the player");
                    Messages.Message("RebuildUntilLegendary.StoppedCanceled".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                    return;
                case DestroyMode.WillReplace:
                    // The player is replacing this building with another blueprint
                    // (upgrades, Replace Stuff-style swaps) - the loop must not claim
                    // the replacement.
                    job.modInitiatedDeconstruct = false;
                    Unregister(job, "the building is being replaced by a new blueprint");
                    Messages.Message("RebuildUntilLegendary.StoppedReplaced".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                    return;
                case DestroyMode.Deconstruct when t is Building && job.modInitiatedDeconstruct:
                    // Our own below-target re-roll: continue the loop with a fresh
                    // blueprint on the same spot.
                    job.modInitiatedDeconstruct = false;
                    job.pendingPlacement = true;
                    job.retryAtTick = 0;
                    if (t is Building_Storage storage)
                    {
                        job.CaptureStorageSettings(storage.GetStoreSettings());
                    }
                    DebugLog.Log(job.DescribeBuilding() + " at " + job.DescribeCell()
                        + " deconstructed for another attempt - will place a new blueprint.");
                    return;
                case DestroyMode.Deconstruct:
                    // A player or another mod deconstructed the tracked building. By
                    // default that is just another way to roll again: count the
                    // attempt and place a fresh blueprint. The mod option restores
                    // the strict "stop the loop" behavior instead.
                    if (RebuildUntilLegendaryMod.Settings?.manualDeconstructContinues ?? true)
                    {
                        job.attempts++;
                        job.pendingPlacement = true;
                        job.retryAtTick = 0;
                        if (t is Building_Storage storage3)
                        {
                            job.CaptureStorageSettings(storage3.GetStoreSettings());
                        }
                        DebugLog.Log(job.DescribeBuilding() + " at " + job.DescribeCell()
                            + " was deconstructed manually (attempt " + job.attempts
                            + ") - will place a new blueprint.");
                        return;
                    }
                    Unregister(job, "the building was deconstructed");
                    Messages.Message("RebuildUntilLegendary.StoppedDeconstructed".Translate(job.DescribeBuilding()),
                        MessageTypeDefOf.NeutralEvent);
                    return;
                case DestroyMode.Vanish when t is Blueprint || t is Frame:
                    // Normal lifecycle handover: blueprint becomes frame, frame becomes
                    // the finished building. The successor appears this same tick.
                    job.expectSuccessorUntilTick = now + SuccessorGraceTicks;
                    DebugLog.VerboseLog(job.DescribeBuilding() + " at " + job.DescribeCell()
                        + " advanced to the next construction stage.");
                    return;
                case DestroyMode.FailConstruction when t is Frame:
                    // Vanilla places a replacement blueprint itself; adopt it.
                    job.expectSuccessorUntilTick = now + SuccessorGraceTicks;
                    DebugLog.Log("construction of " + job.DescribeBuilding() + " at " + job.DescribeCell()
                        + " failed; vanilla re-placed the blueprint.");
                    return;
                default:
                    // Destroyed by damage, quest, teleport, ... Keep rebuilding; the
                    // successor window also adopts vanilla's auto-rebuild blueprint
                    // when that option is enabled.
                    job.modInitiatedDeconstruct = false;
                    job.pendingPlacement = true;
                    job.retryAtTick = 0;
                    job.expectSuccessorUntilTick = now + SuccessorGraceTicks;
                    if (t is Building_Storage storage2)
                    {
                        job.CaptureStorageSettings(storage2.GetStoreSettings());
                    }
                    DebugLog.Log(job.DescribeBuilding() + " at " + job.DescribeCell() + " destroyed (" + mode
                        + ") - will place a new blueprint.");
                    return;
            }
        }

        public override void MapComponentTick()
        {
            if (Jobs.Count == 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            // Training mode must catch a frame within its last 1% of work, which
            // the 15-tick poll can miss on cheap buildings - those jobs are checked
            // every tick instead. The list is tiny, and the check itself is a single
            // cell lookup per training job.
            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                if (Jobs[i].trainingMode)
                {
                    TickTrainingJob(Jobs[i]);
                }
            }
            if (now % CheckIntervalTicks != 0)
            {
                return;
            }
            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                RebuildJob job = Jobs[i];
                ValidateBuilder(job);
                Thing occupant = FindOccupant(job);
                if (occupant != null)
                {
                    job.NotifyOccupantSeen();
                    job.pendingPlacement = false;
                    if (occupant.thingIDNumber != job.occupantIdNumber)
                    {
                        if (now > job.expectSuccessorUntilTick)
                        {
                            // A same-def thing appeared at the cell that we neither
                            // placed nor inherited through a handover: the player is
                            // replacing the building, so the loop must not claim it.
                            Unregister(job, "a new same-def blueprint/building was placed by the player");
                            Messages.Message("RebuildUntilLegendary.StoppedReplaced".Translate(job.DescribeBuilding()),
                                MessageTypeDefOf.NeutralEvent);
                            continue;
                        }
                        job.occupantIdNumber = occupant.thingIDNumber;
                        DebugLog.VerboseLog("adopted successor " + occupant.ThingID + " for "
                            + job.DescribeBuilding() + " at " + job.DescribeCell() + ".");
                    }
                    if (occupant is Building building && !(occupant is Frame))
                    {
                        // Frames are Buildings too, but only a finished building has a
                        // final quality roll - evaluating a frame would kill the loop.
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

        /// <summary>Runs every tick for jobs in training mode: cancels the in-progress
        /// frame once its remaining work drops to the interrupt threshold. Vanilla
        /// refunds the frame's full materials on a cancel while the builder keeps the
        /// experience earned so far, which makes each training cycle nearly free.</summary>
        private void TickTrainingJob(RebuildJob job)
        {
            Thing occupant = FindOccupant(job);
            if (!(occupant is Frame frame)
                || frame.WorkLeft > frame.WorkToBuild * TrainingInterruptAtWorkLeftFraction)
            {
                return;
            }
            DebugLog.Log("training mode: canceling " + job.DescribeBuilding() + " at " + job.DescribeCell()
                + " at " + Mathf.RoundToInt(frame.PercentComplete * 100f) + "% work for a full refund.");
            job.modInitiatedCancel = true;
            frame.Destroy(DestroyMode.Cancel);
            if (!frame.Destroyed)
            {
                // Destroy was refused (e.g. another mod blocked it). Stop instead of
                // hammering the same call every tick.
                job.modInitiatedCancel = false;
                Unregister(job, "the frame could not be canceled");
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
            if (RebuildUntilLegendaryMod.Settings?.pawnDeconstruction ?? true)
            {
                // Queue a vanilla deconstruct order and wait for a colonist to do
                // the tear-down like any manual order. An uninstall order (the
                // player moving the building) is left alone - the moved-away flow
                // ends the loop when it completes.
                if (map.designationManager.DesignationOn(building, DesignationDefOf.Uninstall) != null)
                {
                    return;
                }
                if (map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) == null)
                {
                    // A missing order while our flag is set means the player canceled
                    // it - re-queue without counting the same building twice.
                    bool requeuedAfterCancel = job.modInitiatedDeconstruct;
                    if (!requeuedAfterCancel)
                    {
                        job.attempts++;
                    }
                    job.modInitiatedDeconstruct = true;
                    map.designationManager.AddDesignation(new Designation(building, DesignationDefOf.Deconstruct));
                    DebugLog.Log("quality " + quality + " below " + job.targetQuality
                        + (requeuedAfterCancel
                            ? " - deconstruct order was canceled, re-queued for " + job.DescribeBuilding()
                            + " at " + job.DescribeCell() + " (attempt " + job.attempts + ")."
                            : " - queued a deconstruct order for " + job.DescribeBuilding()
                            + " at " + job.DescribeCell() + " (attempt " + job.attempts + ")."));
                }
                return;
            }
            job.attempts++;
            DebugLog.Log("quality " + quality + " below " + job.targetQuality + " - deconstructing "
                + job.DescribeBuilding() + " at " + job.DescribeCell() + " (attempt " + job.attempts + ").");
            job.modInitiatedDeconstruct = true;
            building.Destroy(DestroyMode.Deconstruct);
            if (!building.Destroyed)
            {
                // Destroy was refused (e.g. a non-destroyable def, or another mod
                // blocked it). Stop instead of hammering the same call every check.
                job.modInitiatedDeconstruct = false;
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
                + ", builder " + job.DescribeBuilder()
                + (job.builder != null && blueprint != null && job.Matches(blueprint)
                    ? " (restriction active)."
                    : " (NO builder restriction)."));
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

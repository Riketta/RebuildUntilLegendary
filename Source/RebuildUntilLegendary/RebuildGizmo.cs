using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RebuildUntilLegendary
{
    /// <summary>
    /// The toggle gizmo shown on quality-capable player buildings and equally on
    /// their already placed blueprints and in-progress frames, so a loop (or a
    /// training job) can be started without finishing the building first. Switching
    /// it on opens the two pickers (target quality, then builder), switching it off
    /// simply removes the rebuild job. With several things selected, one click
    /// applies to all of them; the pickers open only once per click.
    /// </summary>
    internal static class RebuildGizmo
    {
        private static int lastBatchFrame = -1;

        private static int lastTrainingBatchFrame = -1;

        /// <summary>Must match the gizmo patch condition: anything that can be
        /// rebuilt through a blueprint and has a quality to chase. Install
        /// blueprints are excluded - reinstalling an existing building is not a
        /// fresh construction, toggle the loop on the installed building instead.</summary>
        public static bool Qualifies(Thing t)
        {
            if (t == null || !t.Spawned || t.Faction != Faction.OfPlayer)
            {
                return false;
            }
            if (!(t is Blueprint_Build || t is Frame || t is Building))
            {
                return false;
            }
            ThingDef buildDef = RebuildJob.BuildDefOf(t);
            return buildDef != null && buildDef.blueprintDef != null
                && buildDef.IsResearchFinished && buildDef.HasComp(typeof(CompQuality));
        }

        public static Command_Toggle For(Thing constructible)
        {
            if (!Qualifies(constructible))
            {
                return null;
            }
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(constructible.Map);
            if (tracker == null)
            {
                return null;
            }
            return new Command_Toggle
            {
                icon = TexButton.AutoRebuild,
                defaultLabel = "RebuildUntilLegendary.GizmoLabel".Translate(),
                defaultDesc = Describe(tracker.FindJobForThing(constructible)),
                isActive = delegate
                {
                    return tracker.FindJobForThing(constructible) != null;
                },
                toggleAction = delegate
                {
                    ProcessClick(constructible, tracker.FindJobForThing(constructible) != null);
                }
            };
        }

        private static string Describe(RebuildJob job)
        {
            string state = job == null
                ? "RebuildUntilLegendary.StateOff".Translate().ToString()
                : "RebuildUntilLegendary.StateOn".Translate(
                    job.DescribeTarget(), job.DescribeBuilder(), job.attempts).ToString();
            return "RebuildUntilLegendary.GizmoDesc".Translate(state).ToString();
        }

        /// <summary>Stop button for the in-progress blueprint or frame of a tracked
        /// spot, so the loop stays controllable while there is no finished building
        /// to select. The blueprint itself is left alone.</summary>
        public static Command_Action ForTracked(Thing thing)
        {
            if (!thing.Spawned || thing.Map == null)
            {
                return null;
            }
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(thing.Map);
            RebuildJob job = tracker?.FindJobForThing(thing);
            if (job == null)
            {
                return null;
            }
            return new Command_Action
            {
                icon = TexButton.AutoRebuild,
                defaultLabel = "RebuildUntilLegendary.StopGizmoLabel".Translate(),
                defaultDesc = "RebuildUntilLegendary.StopGizmoDesc".Translate(
                    job.DescribeTarget(), job.DescribeBuilder(), job.attempts).ToString(),
                action = delegate
                {
                    // Re-fetch: the occupant may have advanced since this gizmo was drawn.
                    if (tracker.FindJobForThing(thing) is RebuildJob live)
                    {
                        tracker.Unregister(live, "stopped from the blueprint");
                        Messages.Message("RebuildUntilLegendary.StoppedCanceled".Translate(live.DescribeBuilding()),
                            MessageTypeDefOf.NeutralEvent);
                    }
                }
            };
        }

        /// <summary>Training-mode toggle for any tracked thing - the finished
        /// building with an active rebuild job, or its in-progress blueprint or
        /// frame: frames of this loop are canceled at 99% work for a full material
        /// refund instead of being finished.</summary>
        public static Command_Toggle TrainingFor(Thing thing)
        {
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(thing.Map);
            if (tracker == null || tracker.FindJobForThing(thing) == null)
            {
                return null;
            }
            return TrainingToggle(
                isActive: delegate
                {
                    return tracker.FindJobForThing(thing)?.trainingMode ?? false;
                },
                toggleAction: delegate
                {
                    ProcessTrainingClick(thing);
                });
        }

        private static Command_Toggle TrainingToggle(Func<bool> isActive, Action toggleAction)
        {
            return new Command_Toggle
            {
                icon = TexButton.AutoRebuild,
                defaultLabel = "RebuildUntilLegendary.TrainingMode".Translate(),
                defaultDesc = "RebuildUntilLegendary.TrainingModeDesc".Translate().ToString(),
                isActive = isActive,
                toggleAction = toggleAction
            };
        }

        private static void ProcessClick(Thing clicked, bool wasActive)
        {
            if (Time.frameCount == lastBatchFrame)
            {
                // The gizmo grid dispatches one click to every matching gizmo of the
                // selection; the batch collected below already covers them all.
                return;
            }
            lastBatchFrame = Time.frameCount;
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(clicked.Map);
            if (tracker == null)
            {
                return;
            }
            List<Thing> batch = CollectBatch(clicked, wasActive);
            if (batch.Count == 0)
            {
                return;
            }
            if (wasActive)
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    if (tracker.FindJobForThing(batch[i]) is RebuildJob job)
                    {
                        tracker.Unregister(job, "manually switched off");
                    }
                }
                return;
            }
            RebuildSelectorMenus.OpenQualityMenu(delegate (QualityCategory target)
            {
                RebuildSelectorMenus.OpenBuilderMenu(clicked.Map, delegate (Pawn builder)
                {
                    for (int i = 0; i < batch.Count; i++)
                    {
                        tracker.Register(batch[i], target, builder);
                    }
                });
            });
        }

        private static List<Thing> CollectBatch(Thing clicked, bool wasActive)
        {
            List<Thing> batch = new List<Thing>();
            foreach (object selected in Find.Selector.SelectedObjects)
            {
                if (selected is Thing t && Qualifies(t) && t.Map == clicked.Map)
                {
                    MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(t.Map);
                    bool active = tracker != null && tracker.FindJobForThing(t) != null;
                    if (active == wasActive)
                    {
                        batch.Add(t);
                    }
                }
            }
            return batch;
        }

        /// <summary>Training toggle with multi-select support: one click flips every
        /// selected thing whose job currently has the same training state.</summary>
        private static void ProcessTrainingClick(Thing clicked)
        {
            if (Time.frameCount == lastTrainingBatchFrame)
            {
                return;
            }
            lastTrainingBatchFrame = Time.frameCount;
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(clicked.Map);
            RebuildJob clickedJob = tracker?.FindJobForThing(clicked);
            if (clickedJob == null)
            {
                return;
            }
            bool stateBefore = clickedJob.trainingMode;
            foreach (object selected in Find.Selector.SelectedObjects)
            {
                if (selected is Thing t && Qualifies(t) && t.Map == clicked.Map)
                {
                    RebuildJob job = tracker.FindJobForThing(t);
                    if (job != null && job.trainingMode == stateBefore)
                    {
                        job.trainingMode = !stateBefore;
                        DebugLog.Log("training mode " + (job.trainingMode ? "on" : "off") + " for "
                            + job.DescribeBuilding() + " at " + job.DescribeCell() + ".");
                    }
                }
            }
        }
    }
}

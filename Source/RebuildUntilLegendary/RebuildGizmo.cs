using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RebuildUntilLegendary
{
    /// <summary>
    /// The toggle gizmo shown on quality-capable player buildings. Switching it on
    /// opens the two pickers (target quality, then builder), switching it off simply
    /// removes the rebuild job. With several buildings selected, one click applies
    /// to all of them; the pickers open only once per click.
    /// </summary>
    internal static class RebuildGizmo
    {
        private static int lastBatchFrame = -1;

        private static int lastTrainingBatchFrame = -1;

        /// <summary>Must match the gizmo patch condition: anything that can be
        /// rebuilt through a blueprint and has a quality to chase.</summary>
        public static bool Qualifies(Building building)
        {
            return building != null && building.Spawned
                && building.Faction == Faction.OfPlayer
                && building.def.blueprintDef != null
                && building.def.IsResearchFinished
                && building.GetComp<CompQuality>() != null;
        }

        public static Command_Toggle For(Building building)
        {
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(building.Map);
            if (tracker == null)
            {
                return null;
            }
            RebuildJob job = tracker.FindJob(building);
            return new Command_Toggle
            {
                icon = TexButton.AutoRebuild,
                defaultLabel = "RebuildUntilLegendary.GizmoLabel".Translate(),
                defaultDesc = Describe(job),
                isActive = delegate
                {
                    return tracker.FindJob(building) != null;
                },
                toggleAction = delegate
                {
                    ProcessClick(building, tracker.FindJob(building) != null);
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

        /// <summary>Training-mode toggle on a finished building with an active
        /// rebuild job: frames of this loop are canceled at 99% work for a full
        /// material refund instead of being finished.</summary>
        public static Command_Toggle TrainingFor(Building building)
        {
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(building.Map);
            if (tracker == null || tracker.FindJob(building) == null)
            {
                return null;
            }
            return TrainingToggle(tracker,
                isActive: delegate
                {
                    return tracker.FindJob(building)?.trainingMode ?? false;
                },
                toggleAction: delegate
                {
                    ProcessTrainingClick(building);
                });
        }

        /// <summary>Training-mode toggle on the in-progress blueprint or frame of a
        /// tracked spot, next to the stop button.</summary>
        public static Command_Toggle TrainingForTracked(Thing thing)
        {
            if (!thing.Spawned || thing.Map == null)
            {
                return null;
            }
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(thing.Map);
            if (tracker?.FindJobForThing(thing) == null)
            {
                return null;
            }
            return TrainingToggle(tracker,
                isActive: delegate
                {
                    return tracker.FindJobForThing(thing)?.trainingMode ?? false;
                },
                toggleAction: delegate
                {
                    // Re-fetch: the occupant may have advanced since this gizmo was drawn.
                    if (tracker.FindJobForThing(thing) is RebuildJob live)
                    {
                        live.trainingMode = !live.trainingMode;
                        DebugLog.Log("training mode " + (live.trainingMode ? "on" : "off") + " for "
                            + live.DescribeBuilding() + " at " + live.DescribeCell() + ".");
                    }
                });
        }

        private static Command_Toggle TrainingToggle(MapComponent_RebuildTracker tracker, Func<bool> isActive, Action toggleAction)
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

        private static void ProcessClick(Building clicked, bool wasActive)
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
            List<Building> batch = CollectBatch(clicked, wasActive);
            if (batch.Count == 0)
            {
                return;
            }
            if (wasActive)
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    RebuildJob job = tracker.FindJob(batch[i]);
                    if (job != null)
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

        private static List<Building> CollectBatch(Building clicked, bool wasActive)
        {
            List<Building> batch = new List<Building>();
            foreach (object selected in Find.Selector.SelectedObjects)
            {
                if (selected is Building building && Qualifies(building) && building.Map == clicked.Map)
                {
                    MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(building.Map);
                    bool active = tracker != null && tracker.FindJob(building) != null;
                    if (active == wasActive)
                    {
                        batch.Add(building);
                    }
                }
            }
            return batch;
        }

        /// <summary>Training toggle with multi-select support: one click flips every
        /// selected building whose job currently has the same training state.</summary>
        private static void ProcessTrainingClick(Building clicked)
        {
            if (Time.frameCount == lastTrainingBatchFrame)
            {
                return;
            }
            lastTrainingBatchFrame = Time.frameCount;
            MapComponent_RebuildTracker tracker = MapComponent_RebuildTracker.GetFor(clicked.Map);
            RebuildJob clickedJob = tracker?.FindJob(clicked);
            if (clickedJob == null)
            {
                return;
            }
            bool stateBefore = clickedJob.trainingMode;
            foreach (object selected in Find.Selector.SelectedObjects)
            {
                if (selected is Building building && Qualifies(building) && building.Map == clicked.Map)
                {
                    RebuildJob job = tracker.FindJob(building);
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

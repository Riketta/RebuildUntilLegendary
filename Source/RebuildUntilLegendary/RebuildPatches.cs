using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RebuildUntilLegendary
{
    /// <summary>
    /// Watches every destruction as a prefix: when a thing is destroyed it has
    /// already lost its map in the postfix, and the map (plus spawn state) is what
    /// tells us whether a tracked spot is affected. Also snapshots the storage
    /// settings of storage buildings before they disappear, so the replacement
    /// blueprint can inherit them.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    public static class Patch_Thing_Destroy
    {
        public static void Prefix(Thing __instance, DestroyMode mode)
        {
            if (!(__instance is Blueprint) && !(__instance is Frame) && !(__instance is Building))
            {
                return;
            }
            if (!__instance.Spawned || __instance.Map == null)
            {
                return;
            }
            MapComponent_RebuildTracker.GetFor(__instance.Map)?.NotifyDestroying(__instance, mode);
        }
    }

    /// <summary>Adds the toggle to quality-capable player buildings, and a stop
    /// button to the in-progress blueprint or frame of a tracked spot (while the
    /// loop runs there is no finished building to select).</summary>
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
    public static class Patch_ThingWithComps_GetGizmos
    {
        public static void Postfix(ThingWithComps __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__result == null)
            {
                return;
            }
            Gizmo gizmo;
            if (__instance is Blueprint || __instance is Frame)
            {
                gizmo = RebuildGizmo.ForTracked(__instance);
            }
            else if (__instance is Building building && RebuildGizmo.Qualifies(building))
            {
                gizmo = RebuildGizmo.For(building);
            }
            else
            {
                return;
            }
            if (gizmo == null)
            {
                return;
            }
            // Materialize once: gizmo collections can be enumerated more than once,
            // and a lazy Concat would recreate the gizmo (new identity, breaking
            // grouping/state) on every pass.
            List<Gizmo> list = __result.ToList();
            list.Add(gizmo);
            __result = list;
        }
    }

    /// <summary>
    /// Keeps everyone but the chosen builder away from construction work on a
    /// restricted blueprint or frame. Both automatic work assignment and right-click
    /// orders go through these workgivers, and GenConstruct.CanConstruct is the last
    /// gate they all share, so covering those covers every way a pawn could start
    /// working on the rebuild. Material delivery can be exempted by mod setting.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.HasJobOnThing))]
    public static class Patch_SupplyBlueprints_HasJobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, ref bool __result)
        {
            if (__result && BuilderRestriction.Blocks(pawn, t, deliveryWork: true))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), nameof(WorkGiver_ConstructDeliverResourcesToBlueprints.JobOnThing))]
    public static class Patch_SupplyBlueprints_JobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, ref Job __result)
        {
            if (__result != null && BuilderRestriction.Blocks(pawn, t, deliveryWork: true))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), nameof(WorkGiver_ConstructDeliverResourcesToFrames.HasJobOnThing))]
    public static class Patch_SupplyFrames_HasJobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, ref bool __result)
        {
            if (__result && BuilderRestriction.Blocks(pawn, t, deliveryWork: true))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), nameof(WorkGiver_ConstructDeliverResourcesToFrames.JobOnThing))]
    public static class Patch_SupplyFrames_JobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, ref Job __result)
        {
            if (__result != null && BuilderRestriction.Blocks(pawn, t, deliveryWork: true))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), nameof(WorkGiver_ConstructFinishFrames.JobOnThing))]
    public static class Patch_FinishFrames_JobOnThing
    {
        public static void Postfix(Pawn pawn, Thing t, ref Job __result)
        {
            if (__result != null && BuilderRestriction.Blocks(pawn, t, deliveryWork: false))
            {
                __result = null;
            }
        }
    }

    /// <summary>
    /// Deepest gate: every vanilla construction workgiver validates a pawn through
    /// GenConstruct.CanConstruct right before issuing a job (the WorkTypeDef overload
    /// delegates to this one), and combined haul jobs resolve nearby needers through
    /// it too. Blocking here covers all of those paths in one place, including the
    /// ones the workgiver postfixes above cannot see.
    /// </summary>
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanConstruct), new[]
    {
        typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef)
    })]
    public static class Patch_GenConstruct_CanConstruct
    {
        public static void Postfix(Pawn p, Thing t, JobDef jobForReservation, ref bool __result)
        {
            // Delivery calls carry the HaulToContainer reservation tag, construction
            // calls do not - that is what tells helper hauling apart from building.
            bool deliveryWork = jobForReservation == JobDefOf.HaulToContainer;
            if (__result && BuilderRestriction.Blocks(p, t, deliveryWork))
            {
                __result = false;
            }
        }
    }

    /// <summary>Static helper behind the workgiver patches: checks whether the thing
    /// belongs to a rebuild job that reserves construction for one specific pawn.
    /// Material delivery can be opened to everyone by mod setting (on by default),
    /// while the actual construction stays exclusive to the chosen builder.</summary>
    internal static class BuilderRestriction
    {
        public static bool Blocks(Pawn pawn, Thing t, bool deliveryWork)
        {
            if (pawn == null || t == null || !t.Spawned)
            {
                return false;
            }
            if (!MapComponent_RebuildTracker.IsRestrictedThing(t, out RebuildJob job))
            {
                return false;
            }
            if (job.builder == pawn)
            {
                return false;
            }
            if (deliveryWork && (RebuildUntilLegendaryMod.Settings?.anyoneHauls ?? true))
            {
                return false;
            }
            DebugLog.VerboseThrottled("deny_" + pawn.thingIDNumber + "_" + t.thingIDNumber,
                "denied " + pawn.LabelShortCap + " work on " + t.ThingID + " (restricted to " + job.DescribeBuilder() + ").");
            return true;
        }
    }
}

using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RebuildUntilLegendary
{
    public class RebuildUntilLegendarySettings : ModSettings
    {
        public bool anyoneHauls = true;

        public bool debugLogging = false;

        public bool verboseLogging = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref anyoneHauls, "anyoneHauls", true);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
        }
    }

    public class RebuildUntilLegendaryMod : Mod
    {
        public const string PackageId = "Riketta.RebuildUntilLegendary";

        /// <summary>Kept in sync with About/About.xml modVersion.</summary>
        public const string Version = "1.1.0";

        public static RebuildUntilLegendarySettings Settings;

        public RebuildUntilLegendaryMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RebuildUntilLegendarySettings>();
            // Patch each class separately: a game update that renames one target must
            // degrade to "that vanilla behavior stays", never break the other patch.
            Harmony harmony = new Harmony(PackageId);
            PatchSafe(harmony, typeof(Patch_Thing_Destroy));
            PatchSafe(harmony, typeof(Patch_ThingWithComps_GetGizmos));
            PatchSafe(harmony, typeof(Patch_SupplyBlueprints_HasJobOnThing));
            PatchSafe(harmony, typeof(Patch_SupplyBlueprints_JobOnThing));
            PatchSafe(harmony, typeof(Patch_SupplyFrames_HasJobOnThing));
            PatchSafe(harmony, typeof(Patch_SupplyFrames_JobOnThing));
            PatchSafe(harmony, typeof(Patch_FinishFrames_JobOnThing));
            PatchSafe(harmony, typeof(Patch_GenConstruct_CanConstruct));
            Log.Message("[RebuildUntilLegendary] v" + Version + " loaded (debugLogging="
                + Settings.debugLogging.ToString().ToLowerInvariant() + ").");
        }

        private static void PatchSafe(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
            }
            catch (Exception e)
            {
                Log.Error("[RebuildUntilLegendary] Patch " + patchClass.Name
                    + " could not be applied (game update?). " + e);
                return;
            }
            // Kept outside the try: a logging hiccup (e.g. debug logging during mod
            // loading, when no save exists yet) must not be misreported as a failed
            // patch, nor hide that the patch actually applied.
            DebugLog.Log("applied " + patchClass.Name + ".");
        }

        public override string SettingsCategory()
        {
            return "RebuildUntilLegendary.SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);
            list.CheckboxLabeled("RebuildUntilLegendary.AnyoneHauls".Translate(),
                ref Settings.anyoneHauls, "RebuildUntilLegendary.AnyoneHaulsTip".Translate());
            list.Gap(12f);
            if (Prefs.DevMode)
            {
                list.CheckboxLabeled("RebuildUntilLegendary.DebugLogging".Translate(),
                    ref Settings.debugLogging, "RebuildUntilLegendary.DebugLoggingTip".Translate());
                if (Settings.debugLogging)
                {
                    list.CheckboxLabeled("RebuildUntilLegendary.VerboseLogging".Translate(),
                        ref Settings.verboseLogging, "RebuildUntilLegendary.VerboseLoggingTip".Translate());
                }
                list.Gap(4f);
            }
            list.End();
        }
    }
}

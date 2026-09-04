using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RebuildUntilLegendary
{
    /// <summary>
    /// The two pickers shown when the toggle is switched on: the target quality and
    /// the pawn allowed to build. Both are plain float menus in the style of the
    /// gene extractor's pawn selection, so they stay familiar and work with anything
    /// DLCs or mods add - quality categories, pawn kinds and storage buildings alike.
    /// </summary>
    internal static class RebuildSelectorMenus
    {
        public static void OpenQualityMenu(Action<QualityCategory> picked)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<QualityCategory> categories = QualityUtility.AllQualityCategories;
            for (int i = 0; i < categories.Count; i++)
            {
                QualityCategory quality = categories[i];
                options.Add(new FloatMenuOption(
                    "RebuildUntilLegendary.UntilQuality".Translate(quality.GetLabel().CapitalizeFirst()),
                    delegate
                    {
                        picked(quality);
                    }));
            }
            DebugLog.Log("opened the quality picker (" + options.Count + " categories).");
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void OpenBuilderMenu(Map map, Action<Pawn> picked)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RebuildUntilLegendary.Anyone".Translate(), delegate
                {
                    picked(null);
                })
            };
            foreach (Pawn pawn in BuilderCandidates(map))
            {
                Pawn candidate = pawn;
                string label = candidate.LabelShortCap + OptionSuffix(candidate);
                if (candidate.Downed)
                {
                    options.Add(new FloatMenuOption(label + ": " + "DownedLower".Translate(), null,
                        candidate, Color.white));
                }
                else
                {
                    options.Add(new FloatMenuOption(label, delegate
                    {
                        picked(candidate);
                    }, candidate, Color.white));
                }
            }
            DebugLog.Log("opened the builder picker (" + options.Count + " candidates).");
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string OptionSuffix(Pawn pawn)
        {
            if (pawn.skills != null)
            {
                SkillRecord skill = pawn.skills.GetSkill(SkillDefOf.Construction);
                if (skill != null)
                {
                    return " (" + SkillDefOf.Construction.LabelCap + " " + skill.Level + ")";
                }
            }
            return "";
        }

        /// <summary>Anyone of the player faction who could plausibly hold a hammer:
        /// humanlike colonists and slaves, plus colony mechs. Pawns whose backstory,
        /// genes or injuries forbid construction work are not offered at all.</summary>
        private static IEnumerable<Pawn> BuilderCandidates(Map map)
        {
            return map.mapPawns.AllPawnsSpawned
                .Where(p => p.Faction == Faction.OfPlayer
                    && (p.RaceProps.Humanlike || p.IsColonyMech)
                    && !p.WorkTagIsDisabled(WorkTags.Constructing))
                .OrderBy(p => p.IsColonist ? 0 : (p.IsSlaveOfColony ? 1 : 2))
                .ThenBy(p => p.LabelShortCap);
        }
    }
}

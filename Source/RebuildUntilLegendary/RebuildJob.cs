using RimWorld;
using Verse;

namespace RebuildUntilLegendary
{
    /// <summary>
    /// One tracked "rebuild until quality" slot: the building that stands (or stood)
    /// at a cell, its material, style and rotation so every replacement blueprint is
    /// reproduced faithfully, the quality to reach and the pawn allowed to build it.
    /// Tracked by cell + def rather than by a thing reference, because the occupant
    /// changes identity as it cycles blueprint -> frame -> building.
    /// </summary>
    public class RebuildJob : IExposable
    {
        public IntVec3 cell;

        public Rot4 rotation;

        public ThingDef buildDef;

        public ThingDef stuff;

        public Precept_ThingStyle styleSourcePrecept;

        public ThingStyleDef styleDef;

        public QualityCategory targetQuality;

        /// <summary>Null means anyone may build.</summary>
        public Pawn builder;

        public int attempts;

        public bool pendingPlacement;

        public int retryAtTick;

        /// <summary>Thing id of the current blueprint/frame/building, used to notice
        /// when the building was minified or otherwise moved away, and to tell a
        /// legitimate successor apart from a same-def blueprint the player placed.</summary>
        public int occupantIdNumber = -1;

        public StorageSettings storageSettings;

        /// <summary>Set right before the mod itself destroys a below-target building,
        /// so the destroy handler can tell its own deconstruction from a player's.
        /// Consumed synchronously by the Thing.Destroy prefix, never persisted.</summary>
        public bool modInitiatedDeconstruct;

        /// <summary>Tick until which a new occupant at the cell is trusted as the
        /// successor of a tracked thing (blueprint - frame - building handover, or a
        /// blueprint vanilla re-placed after failed construction or destruction).
        /// Transient; successors appear within the same tick as the destroy.</summary>
        public int expectSuccessorUntilTick;

        private int missingSinceTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref rotation, "rotation", Rot4.North);
            Scribe_Defs.Look(ref buildDef, "buildDef");
            Scribe_Defs.Look(ref stuff, "stuff");
            Scribe_References.Look(ref styleSourcePrecept, "styleSourcePrecept");
            Scribe_Defs.Look(ref styleDef, "styleDef");
            Scribe_Values.Look(ref targetQuality, "targetQuality", QualityCategory.Legendary);
            Scribe_References.Look(ref builder, "builder");
            Scribe_Values.Look(ref attempts, "attempts", 0);
            Scribe_Values.Look(ref pendingPlacement, "pendingPlacement", false);
            Scribe_Values.Look(ref retryAtTick, "retryAtTick", 0);
            Scribe_Values.Look(ref occupantIdNumber, "occupantIdNumber", -1);
            Scribe_Deep.Look(ref storageSettings, "storageSettings");
        }

        public string DescribeTarget()
        {
            return targetQuality.GetLabel().CapitalizeFirst();
        }

        public string DescribeBuilder()
        {
            return builder != null ? builder.LabelShortCap : "RebuildUntilLegendary.Anyone".Translate().ToString();
        }

        public string DescribeBuilding()
        {
            if (stuff != null)
            {
                return "ThingMadeOfStuffLabel".Translate(stuff.LabelAsStuff, buildDef.label).ToString();
            }
            return buildDef.label;
        }

        public string DescribeCell()
        {
            return cell.ToString();
        }

        /// <summary>True when the thing occupies this job's cell as the tracked
        /// buildable; works for the finished building, its frame and its blueprint.</summary>
        public bool Matches(Thing t)
        {
            if (t == null || !t.Spawned || t.Destroyed || t.Position != cell)
            {
                return false;
            }
            return BuildDefOf(t) == buildDef;
        }

        /// <summary>The thing a blueprint or frame will become once finished.</summary>
        public static ThingDef BuildDefOf(Thing t)
        {
            if (t is Blueprint blueprint)
            {
                return blueprint.def.entityDefToBuild as ThingDef;
            }
            if (t is Frame frame)
            {
                return frame.def.entityDefToBuild as ThingDef;
            }
            return t.def;
        }

        public void CaptureStorageSettings(StorageSettings source)
        {
            if (source == null)
            {
                return;
            }
            if (storageSettings == null)
            {
                storageSettings = new StorageSettings();
            }
            storageSettings.CopyFrom(source);
        }

        public void NotifyOccupantSeen()
        {
            missingSinceTick = -1;
        }

        public void NotifyOccupantMissing(int now, out bool expired)
        {
            if (missingSinceTick < 0)
            {
                missingSinceTick = now;
            }
            expired = now - missingSinceTick > 1000;
        }
    }
}

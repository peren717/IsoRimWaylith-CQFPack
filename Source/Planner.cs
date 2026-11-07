using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ProjectZ;
using QuestEditor_Library;
using RimWorld.Planet;
using Verse;

namespace IsoRimCQFPack
{
    // The layout of a site's map, from the def it will be made of. Sites only: the only maps generated whole from one def,
    // whose size CQF asks for (site, main site, the editor opening a saved map). The floors are the def's, not the game's: the
    // map stays as it was made.
    internal static class Planner
    {
        private static readonly Func<MapParent, MapFloorPlan?> Plan = PlanFor;

        internal static void Register() => Api.RegisterMapFloorPlanner(Plan);

        private static MapFloorPlan? PlanFor(MapParent parent)
        {
            CustomMapDataDef def = MapDefOf(parent);
            if (def == null || !Marker.TryRead(def, out Layout l) || !l.FitsDef(def))
            {
                return null;
            }
            return new MapFloorPlan { groundSize = new IntVec3(l.groundX, 1, l.groundZ), floors = l.floors, keepUpperContent = true };
        }

        // The def a parent's map is made of, for the planner (a main site picks one now, see MainPick).
        private static CustomMapDataDef MapDefOf(MapParent parent)
        {
            switch (parent)
            {
                case MainSite main: return MainPick.Pick(main);
                case CustomSite site: return site.mapDef;
                case EditorMapObject editor: return editor.mapDef;
                default: return null;
            }
        }

        // Was the map planned for this def? Asked by the placement: a settlement or a pocket map is not, even when its size
        // happens to match. (A main site's def is set by its genStep by then.)
        internal static bool PlannedFor(MapParent parent, CustomMapDataDef def)
        {
            switch (parent)
            {
                case CustomSite site: return site.mapDef == def;
                case EditorMapObject editor: return editor.mapDef == def;
                default: return false;
            }
        }
    }

    // A main site picks its def in its genStep, by conditions and by chance, after the map's size is fixed. So the pick is made
    // when the planner asks, by CQF's own code, and the genStep then gets the same answer. A site without a pick asks CQF.
    internal static class MainPick
    {
        private static readonly MethodInfo Lookup = AccessTools.Method(typeof(GenStep_MainMap), "GetMainSiteMap");
        private static readonly Dictionary<MainSite, CustomMapDataDef> picks = new Dictionary<MainSite, CustomMapDataDef>();

        internal static CustomMapDataDef Pick(MainSite site)
        {
            picks.Remove(site);
            CustomMapDataDef def = (CustomMapDataDef)Lookup.Invoke(new GenStep_MainMap(), new object[] { site });
            picks[site] = def;
            return def;
        }

        // The genStep's lookup. False: no pick waiting, CQF looks it up.
        internal static bool TakePick(MainSite site, out CustomMapDataDef def)
        {
            if (site != null && picks.TryGetValue(site, out def))
            {
                picks.Remove(site);
                return true;
            }
            def = null;
            return false;
        }
    }
}

using System.Collections.Generic;
using ProjectZ;
using QuestEditor_Library;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace IsoRimCQFPack
{
    // What can't keep floors: short warnings, and a refusal where the result would be wrong.
    internal static class Guards
    {
        // Times each guard fired this session, by kind.
        public static readonly Dictionary<string, int> Fired = new Dictionary<string, int>();

        private static void Count(string kind)
        {
            Fired.TryGetValue(kind, out int n);
            Fired[kind] = n + 1;
        }

        internal static void Refuse(CustomMapDataDef def, string kind, string what)
        {
            Count(kind);
            Log.Warning(Pack.Tag + $"{def?.defName} has floors, not placed ({kind}{(what == "this use" ? "" : ": " + what)}).");
        }

        internal static void Warn(CustomMapDataDef def, string kind, string what)
        {
            Count(kind);
            Log.Warning(Pack.Tag + $"{def?.defName}: {what}.");
        }

        // A message for the player. (Saves run in a long event: shown when it ends.)
        internal static void Tell(string key)
        {
            string text = key.Translate();
            LongEventHandler.ExecuteWhenFinished(() => Messages.Message(text, MessageTypeDefOf.RejectInput, historical: false));
        }

        // Which use a refused placement was.
        internal static string Kind(Map map, bool isSubMap, bool isGenerateByCore)
        {
            MapParent parent = map?.Parent;
            if (parent is PocketMapParent)
            {
                return "Pocket";
            }
            if (isSubMap || isGenerateByCore)
            {
                return "Part";
            }
            if (Find.GameInitData != null || parent is Settlement s && s.Faction == Faction.OfPlayer)
            {
                return "Start";
            }
            return parent is Settlement ? "Settlement" : parent is Site ? "Outpost" : "Other";
        }

        // Cells of more than one floor, or of a void row.
        internal static bool SpansFloors(Map map, List<IntVec3> cells)
        {
            int floor = int.MinValue;
            for (int i = 0; i < cells.Count; i++)
            {
                int f = Api.FloorOf(cells[i], map);
                if (f < 0 || floor != int.MinValue && f != floor)
                {
                    return true;
                }
                floor = f;
            }
            return false;
        }

        // Cells above the ground (slabs laid flat), or stairs (they would lead nowhere).
        internal static bool PartLosesFloors(Map map, List<IntVec3> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (Api.FloorOf(cells[i], map) != 0)
                {
                    return true;
                }
                List<Thing> things = cells[i].GetThingList(map);
                for (int k = 0; k < things.Count; k++)
                {
                    if (things[k] is Building && Api.TryGetStairGeometry(things[k], out StairGeometry _))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Natural rock on an upper floor: a source with tall mountains. The rock is saved, what the game knows about it is not
        // (the map's heights and how far it was explored): on the new map it stands as plain rock, nothing more.
        internal static void CheckSource(Map map)
        {
            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t.def.building != null && t.def.building.isNaturalRock && Api.FloorOf(t.Position, map) > 0)
                {
                    Count("TallSource");
                    Log.Warning(Pack.Tag + "the source map has tall mountains: their upper floors are saved as plain rock.");
                    Tell("IsoRimCQF_TallSource");
                    return;
                }
            }
        }
    }
}

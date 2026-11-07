using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ProjectZ;
using QuestEditor_Library;
using Verse;

namespace IsoRimCQFPack
{
    // CQF saves a thing's def, stuff, rotation and cell: a ladder's link is lost, and a raid's ladder (not rotatable, placed on
    // its foot) can't work it out again. So each link goes into a tag of its own at the save, "IsoRimZ.ladder=41,50,42,152,0"
    // (foot, top, the top was a wall a raider clears), and comes back on the site's map once its content is written. The rules
    // are ProjectZ's (LadderSite), and the link is made by the ladder's own SpawnSetup: respawned as a loaded ladder, it
    // registers what it holds. Only on a map placed at the full size: a def without floors never gets here.
    internal static class Ladders
    {
        private const string Start = "IsoRimZ.ladder=";
        private static readonly Regex Shape = new Regex(@"^IsoRimZ\.ladder=(\d+),(\d+),(\d+),(\d+),([01])$", RegexOptions.CultureInvariant);

        private struct Saved
        {
            public IntVec3 foot, top;
            public bool blocker;
        }

        internal static bool IsTag(string tag) => tag != null && tag.StartsWith(Start, System.StringComparison.Ordinal);

        internal static void Record(CustomMapDataDef def, Map map)
        {
            foreach (Building_SiegeLadder b in map.listerThings.AllThings.OfType<Building_SiegeLadder>())
            {
                if (b.top.IsValid && b.foot.IsValid && b.span > 0)
                {
                    def.tags.Add(string.Format(CultureInfo.InvariantCulture, "{0}{1},{2},{3},{4},{5}", Start, b.foot.x, b.foot.z, b.top.x, b.top.z, b.topBlocker ? 1 : 0));
                }
            }
        }

        private static List<Saved> Read(CustomMapDataDef def)
        {
            List<Saved> saved = new List<Saved>();
            foreach (string tag in def.tags)
            {
                Match m = IsTag(tag) ? Shape.Match(tag) : Match.Empty;
                if (m.Success)
                {
                    int N(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
                    saved.Add(new Saved { foot = new IntVec3(N(1), 0, N(2)), top = new IntVec3(N(3), 0, N(4)), blocker = N(5) == 1 });
                }
            }
            return saved;
        }

        // After the things, with the map at its full size and before the stairwell roofs (the column must read open).
        internal static void Restore(CustomMapDataDef def, Map map)
        {
            List<Building_SiegeLadder> ladders = map.listerThings.AllThings.OfType<Building_SiegeLadder>().ToList();
            if (ladders.Count == 0)
            {
                return;
            }
            List<Saved> saved = Read(def);
            foreach (Building_SiegeLadder b in ladders)
            {
                int i = saved.FindIndex(s => b.Position == s.foot || b.Position == s.top);
                if (i >= 0)
                {
                    Saved s = saved[i];
                    saved.RemoveAt(i);
                    if (b.top != s.top && !FromSaved(b, s, map) && !b.top.IsValid)
                    {
                        Guards.Warn(def, "LadderNoSite", $"{b.def.defName} at {b.Position} has no site, left unlinked");
                    }
                }
                else if (!b.top.IsValid && !FromCell(b, map))
                {
                    Guards.Warn(def, "LadderNoSite", $"{b.def.defName} at {b.Position} has no site, left unlinked");
                }
            }
        }

        // The saved link, if the site is still good.
        private static bool FromSaved(Building_SiegeLadder b, Saved s, Map map)
        {
            IntVec3 side = Api.GroundCellOf(s.foot, map) - Api.GroundCellOf(s.top, map);
            for (int i = 0; i < 4; i++)
            {
                Rot4 r = new Rot4(i);
                if (r.FacingCell == side)
                {
                    if (LadderSite.Find(s.top, r, map, out IntVec3 foot, out int span, out _, s.blocker, b) && foot == s.foot)
                    {
                        Link(b, foot, s.top, span, map);
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        // A def saved before the tag: what the ladder's cell and rotation give by the normal rules. A ladder placed on its foot
        // has no side of its own, so each is tried, its rotation first.
        private static bool FromCell(Building_SiegeLadder b, Map map)
        {
            if (b.Placement == LadderPlacement.Top)
            {
                if (LadderSite.Find(b.Position, b.Rotation, map, out IntVec3 foot, out int span, out _, false, b))
                {
                    Link(b, foot, b.Position, span, map);
                    return true;
                }
                return false;
            }
            for (int i = 0; i < 4; i++)
            {
                if (LadderSite.FindFromFoot(b.Position, new Rot4((b.Rotation.AsInt + i) % 4), map, out IntVec3 top, out int span, out _, false, b))
                {
                    Link(b, b.Position, top, span, map);
                    return true;
                }
            }
            return false;
        }

        // The ladder's own spawn registers the link and opens its column, as for a ladder loaded from a save.
        private static void Link(Building_SiegeLadder b, IntVec3 foot, IntVec3 top, int span, Map map)
        {
            IntVec3 at = b.Position;
            Rot4 rot = b.Rotation;
            b.DeSpawn();
            b.foot = foot;
            b.top = top;
            b.span = span;
            b.topBlocker = !top.Walkable(map);
            GenSpawn.Spawn(b, at, map, rot, WipeMode.Vanish, respawningAfterLoad: true);
        }
    }
}

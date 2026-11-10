using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ProjectZ;
using QuestEditor_Library;
using RimWorld;
using Verse;

namespace IsoRimCQFPack
{
    // Every patch of the pack. Each is applied on its own, and each body catches what it throws: a CQF update that moves one
    // loses that one (with a warning), the rest and CQF itself go on.
    internal static class Patches
    {
        internal const int Count = 6;

        internal static int Apply(Harmony h)
        {
            int n = 0;
            n += One(h, "stamp", typeof(CustomMapDataDef), "LoadData", new[] { typeof(Map) }, postfix: nameof(Stamp.Postfix), owner: typeof(Stamp));
            n += One(h, "part", typeof(CustomMapDataDef), "LoadData", new[] { typeof(Map), typeof(List<IntVec3>), typeof(IntVec3) }, prefix: nameof(Part.Prefix), postfix: nameof(Part.Postfix), owner: typeof(Part));
            n += One(h, "placement", typeof(GenStep_CustomMap), "SpawnCustomMap", null, prefix: nameof(Placement.Prefix), finalizer: nameof(Placement.Finalizer), owner: typeof(Placement));
            n += One(h, "main site", typeof(GenStep_MainMap), "GetMainSiteMap", null, prefix: nameof(MainLookup.Prefix), owner: typeof(MainLookup));
            n += One(h, "entrance", typeof(CustomMapEntrance), "GenerateCustomMap", null, prefix: nameof(Entrance.Prefix), owner: typeof(Entrance));
            n += One(h, "submap", typeof(CustomMapDataDef), "GenerateAsSubmap", null, prefix: nameof(Submap.Prefix), owner: typeof(Submap));
            return n;
        }

        private static int One(Harmony h, string what, Type type, string name, Type[] args, Type owner, string prefix = null, string postfix = null, string finalizer = null)
        {
            try
            {
                MethodInfo target = args == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args);
                if (target == null)
                {
                    Log.Warning(Pack.Tag + $"{type.Name}.{name} not found: {what} is off.");
                    return 0;
                }
                HarmonyMethod M(string m) => m == null ? null : new HarmonyMethod(AccessTools.Method(owner, m));
                h.Patch(target, prefix: M(prefix), postfix: M(postfix), finalizer: M(finalizer));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + $"{what} is off: {e.Message}");
                return 0;
            }
        }
    }

    // CustomMapDataDef.LoadData(Map): a whole map saved. Its floors go into the tag; a tag from an earlier save never stays.
    internal static class Stamp
    {
        public static void Postfix(CustomMapDataDef __instance, Map map)
        {
            try
            {
                __instance.tags.RemoveAll(t => Marker.IsMarker(t) || Ladders.IsTag(t));
                if (map == null || !Api.HasFloors(map))
                {
                    return;
                }
                CellRect ground = Api.GroundRect(map);
                __instance.tags.Add(Marker.Format(new Layout { floors = Api.FloorCount(map), groundX = ground.Width, groundZ = ground.Height, stride = Stride(map) }));
                Ladders.Record(__instance, map);
                Guards.CheckSource(map);
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + "stamp: " + e);
            }
        }

        // Rows from one floor's band to the next, as ProjectZ lays them out.
        private static int Stride(Map map) => Api.Version >= 10 ? StrideApi10(map) : map.Size.z / Api.FloorCount(map);   // (fallback for ProjectZ older than Api 10: its layout, FloorCount bands of equal rows)

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int StrideApi10(Map map) => Api.FloorStride(map);
    }

    // CustomMapDataDef.LoadData(map, cells, size): a part of a map. A part has no floors: its cells are laid out flat, so cells
    // of two floors would land side by side, and it is refused. One floor is fine, with a warning when something is lost.
    internal static class Part
    {
        public static bool Prefix(CustomMapDataDef __instance, Map map, List<IntVec3> poss, IntVec3 size)
        {
            try
            {
                if (map != null && Api.HasFloors(map) && Guards.SpansFloors(map, poss))
                {
                    __instance.size = size;
                    Guards.Refuse(__instance, "PartAcrossFloors", "a part across floors");
                    Guards.Tell("IsoRimCQF_PartAcrossFloors");
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + "part: " + e);
            }
            return true;
        }

        public static void Postfix(CustomMapDataDef __instance, Map map, List<IntVec3> poss)
        {
            try
            {
                __instance.tags.RemoveAll(t => Marker.IsMarker(t) || Ladders.IsTag(t));
                if (map != null && Api.HasFloors(map) && Guards.PartLosesFloors(map, poss))
                {
                    Guards.Warn(__instance, "PartLosesFloors", "the part keeps no floors");
                    Guards.Tell("IsoRimCQF_PartLosesFloors");
                }
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + "part: " + e);
            }
        }
    }

    // GenStep_CustomMap.SpawnCustomMap, the one place every CQF map and part is placed. An unmarked def passes untouched. A
    // marked one runs at the map's full size from the origin, on the map planned for it; anywhere else it is refused.
    internal static class Placement
    {
        internal struct State
        {
            public bool open;
            public ThingData reserve;
            public CustomMapDataDef def;
            public List<IntVec3> wellRoofs;
            public List<CellRect> wellRects;
        }

        public static bool Prefix(Map map, CustomMapDataDef def, bool load, bool isSubMap, bool isGenerateByCore, ref bool replaceMap, ref List<Thing> __result, out State __state)
        {
            __state = default(State);
            try
            {
                if (def == null || !Marker.TryRead(def, out Layout l))
                {
                    return true;
                }
                if (!isSubMap && !isGenerateByCore && map != null && l.FitsDef(def) && Planner.PlannedFor(map.Parent, def) && Api.TryBeginFullMapSize(map))
                {
                    if (map.Size.x == def.size.x && map.Size.z == def.size.z)
                    {
                        __state.open = true;
                        __state.def = def;
                        replaceMap = true;
                        HoldWellRoofs(def, ref __state);
                        if (!load && def.reserveThing != null)
                        {
                            // (CQF fills every empty cell of the map with it, the open air and the void rows too: done after the
                            // window instead, on the ground)
                            __state.reserve = def.reserveThing;
                            def.reserveThing = null;
                        }
                        return true;
                    }
                    Api.EndFullMapSize(map);
                }
                Guards.Refuse(def, Guards.Kind(map, isSubMap, isGenerateByCore), "this use");
                __result = new List<Thing>();
                return false;
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + "placement: " + e);
                return true;
            }
        }

        // The stairwell roof of a shaft or a ladder column only holds over the thing that opens the column: CQF writes roofs
        // before things, which would turn it into a built roof, and a ladder then finds its column closed. So it is written
        // after the things.
        private static void HoldWellRoofs(CustomMapDataDef def, ref State state)
        {
            RoofDef well = PZDefOf.PZ_StairwellRoof;
            if (well == null)
            {
                return;
            }
            if (def.roofs.TryGetValue(well, out state.wellRoofs))
            {
                def.roofs.Remove(well);
            }
            if (def.roofRects.TryGetValue(well, out state.wellRects))
            {
                def.roofRects.Remove(well);
            }
        }

        private static void PutWellRoofs(Map map, CustomMapDataDef def, State state, bool spawned)
        {
            RoofDef well = PZDefOf.PZ_StairwellRoof;
            if (state.wellRoofs != null)
            {
                def.roofs[well] = state.wellRoofs;
                if (spawned)
                {
                    foreach (IntVec3 c in state.wellRoofs)
                    {
                        map.roofGrid.SetRoof(c, well);
                    }
                }
            }
            if (state.wellRects != null)
            {
                def.roofRects[well] = state.wellRects;
                if (spawned)
                {
                    foreach (CellRect r in state.wellRects)
                    {
                        foreach (IntVec3 c in r)
                        {
                            map.roofGrid.SetRoof(c, well);
                        }
                    }
                }
            }
        }

        public static Exception Finalizer(Exception __exception, Map map, State __state)
        {
            if (__state.open)
            {
                try
                {
                    if (__exception == null)
                    {
                        Ladders.Restore(__state.def, map);
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(Pack.Tag + "ladders: " + e);
                }
                try
                {
                    PutWellRoofs(map, __state.def, __state, __exception == null);
                }
                catch (Exception e)
                {
                    Log.Warning(Pack.Tag + "well roofs: " + e);
                }
                Api.EndFullMapSize(map);
            }
            if (__state.reserve != null)
            {
                __state.def.reserveThing = __state.reserve;
                try
                {
                    if (__exception == null)
                    {
                        FillGround(map, __state.def, __state.reserve);
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(Pack.Tag + "reserve: " + e);
                }
            }
            return __exception;
        }

        private static void FillGround(Map map, CustomMapDataDef def, ThingData data)
        {
            HashSet<IntVec3> skip = new HashSet<IntVec3>(def.disgenerate);
            foreach (IntVec3 c in map.AllCells.ToList())
            {
                if (!skip.Contains(c) && c.GetFirstBuilding(map) == null)
                {
                    data.Spawn(map, c, (d, stuff) => d);
                }
            }
        }
    }

    // GenStep_MainMap.GetMainSiteMap: the pick the planner made.
    internal static class MainLookup
    {
        public static bool Prefix(MainSite site, ref CustomMapDataDef __result)
        {
            try
            {
                if (MainPick.TakePick(site, out CustomMapDataDef def))
                {
                    __result = def;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + "main site: " + e);
            }
            return true;
        }
    }

    // A marked def behind an entrance: no pocket map is made for it (it would be one flat map of all its rows, and empty once
    // refused). The entrance acts as a blocked one.
    internal static class Entrance
    {
        public static bool Prefix(CustomMapEntrance __instance)
        {
            try
            {
                CustomMapDataDef def = __instance.MapDef;
                if (Marker.TryRead(def, out Layout _))
                {
                    Guards.Refuse(def, "Pocket", "a pocket map");
                    Guards.Tell("IsoRimCQF_NoPocket");
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + "entrance: " + e);
            }
            return true;
        }
    }

    internal static class Submap
    {
        public static bool Prefix(CustomMapDataDef __instance)
        {
            try
            {
                if (Marker.TryRead(__instance, out Layout _))
                {
                    Guards.Refuse(__instance, "Pocket", "a pocket map");
                    Guards.Tell("IsoRimCQF_NoPocket");
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning(Pack.Tag + "submap: " + e);
            }
            return true;
        }
    }
}

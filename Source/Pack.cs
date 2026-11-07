using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ProjectZ;
using Verse;

namespace IsoRimCQFPack
{
    // IsoRim Waylith - Custom Quest Framework Pack (needs ProjectZ Api 9; older: this does nothing, logged once, CQF runs as it
    // does without the pack).
    //
    // A CQF map saved on a map with floors keeps them: LoadData stamps a tag (Marker) with the source's layout; the map of a site
    // made from such a def gets that layout (Planner, through ProjectZ's floor plan), whatever the game's floor count; CQF's own
    // placement then runs at the full size from the origin (Placement), so its rows land on the floors. Everything else of CQF
    // is left alone: a def without the tag goes through untouched. Uses that can't keep floors are refused (Guards).
    [StaticConstructorOnStartup]
    public static class Pack
    {
        internal const string Tag = "[IsoRim CQF Pack] ";
        internal const int NeededApi = 9;

        static Pack()
        {
            try
            {
                if (Api.Version < NeededApi)
                {
                    Log.Message(Tag + $"IsoRimZ is older than Api {NeededApi}: CQF maps behave as without the pack.");
                    return;
                }
                Run();
            }
            catch (Exception e)
            {
                Log.Error(Tag + "Setup failed (CQF maps behave as without the pack): " + e);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run()
        {
            Harmony h = new Harmony("peren.isorimwaylith.cqfpack");
            int applied = Patches.Apply(h);
            Planner.Register();
            Log.Message(Tag + $"ready, {applied} of {Patches.Count} patches.");
        }
    }
}

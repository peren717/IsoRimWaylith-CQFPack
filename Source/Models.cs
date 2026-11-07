using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using QuestEditor_Library;
using UnityEngine;
using Verse;

namespace IsoRimCQFPack
{
    // The pack's model part (needs IsoRim Api 50; older: this does nothing, logged once, and Patches/ binds no model).
    //  - A cage (CustomContainer) has no Graphic override: the game shows Cage_opened, and only its DrawAt draws the closed look (front, the prisoner, back) while it
    //    holds something. So "holds something" is a model state, and while the model stands in that DrawAt draws only the prisoner.
    //  - Everything else that opens swaps its sprite through Graphic (LootBox, CustomMapEntrance): ModelDef.byGraphic follows it with no code.
    // Independent of the floors part: it runs on any IsoRimZ. A failure here leaves CQF's own drawing in 3D (logged once).
    internal static class Models
    {
        internal const string CageFull = "cqf_cage_full";
        private const int NeededApi = 50;

        internal static void Run()
        {
            try
            {
                if (IsoRim.Api.Version < NeededApi)
                {
                    Log.Message(Pack.Tag + $"IsoRim is older than Api {NeededApi}: CQF keeps its sprites.");
                    return;
                }
                Setup();
            }
            catch (Exception e)
            {
                Log.Error(Pack.Tag + "Model setup failed (CQF's own drawing stays in 3D): " + e);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Setup()
        {
            IsoRim.Api.AddModelState(CageState);
            new Harmony("peren.isorimwaylith.cqfpack.models").Patch(AccessTools.Method(typeof(CustomContainer), "DrawAt"), prefix: new HarmonyMethod(typeof(Models), nameof(CageDrawAt_Prefix)));
        }

        private static string CageState(Thing t) => t is CustomContainer c && c.HasAnyContents ? CageFull : null;

        // As CustomContainer.DrawAt, without the two flat sprites.
        public static bool CageDrawAt_Prefix(CustomContainer __instance)
        {
            try
            {
                if (!__instance.HasAnyContents || !IsoRim.Api.ModelStandsIn(__instance))
                {
                    return true;
                }
                ModExtension_CustomThing e = __instance.Extension;
                if (e != null && e.showInnerThings)
                {
                    __instance.ContainedThing.DynamicDrawPhaseAt(DrawPhase.Draw, __instance.DrawPos + e.caturedDrawOffset, false);
                }
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorOnce(Pack.Tag + "Drawing a cage failed, its own drawing goes on: " + e, 83611401);
                return true;
            }
        }
    }
}

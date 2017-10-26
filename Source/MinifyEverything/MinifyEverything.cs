using RimWorld;
using System;
using System.Reflection;
using System.Linq;
using Verse;
using Harmony;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse.AI;

namespace MinifyEverything
{
    [StaticConstructorOnStartup]
    static class MinifyEverything
    {

        static MinifyEverything()
        {
            MethodInfo minifiedDefInfo = AccessTools.Method(typeof(ThingDefGenerator_Buildings), "NewBlueprintDef_Thing");
            MethodInfo shortHashGiver = AccessTools.Method(typeof(ShortHashGiver), "GiveShortHash");

            DefDatabase<ThingDef>.AllDefsListForReading.ForEach(td =>
            {
                if(td.building != null && td.blueprintDef != null && !td.Minifiable)
                {
                    td.minifiedDef = ThingDef.Named("MinifiedFurniture");
                    ThingDef minifiedDef = (ThingDef)minifiedDefInfo.Invoke(null, new object[] {td, true, td.blueprintDef });
                    minifiedDef.deepCommonality = 0f;
                    minifiedDef.ResolveReferences();
                    minifiedDef.PostLoad();
                    shortHashGiver.Invoke(null, new object[] { minifiedDef, typeof(ThingDef) });
                    DefDatabase<ThingDef>.Add(minifiedDef);
                }
            });

            HarmonyInstance harmony = HarmonyInstance.Create("rimworld.erdelf.minify_everything");
            harmony.Patch(AccessTools.Method(typeof(Blueprint_Install), nameof(Blueprint_Install.TryReplaceWithSolidThing)), null, new HarmonyMethod(typeof(MinifyEverything), nameof(AfterInstall)));
            harmony.Patch(AccessTools.Method(typeof(WorkGiver_ConstructDeliverResources), "InstallJob"), null, null, new HarmonyMethod(typeof(MinifyEverything), nameof(InstallJobTranspiler)));
            harmony.Patch(AccessTools.Method(typeof(Designator_Build), nameof(Designator.DesignateSingleCell)), null, null, new HarmonyMethod(typeof(MinifyEverything), nameof(DesignateSingleCellTranspiler)));
        }

        public static IEnumerable<CodeInstruction> DesignateSingleCellTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo placeBlueprint = AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForBuild));
            foreach (CodeInstruction instruction in instructions)
                yield return (instruction.opcode == OpCodes.Call && instruction.operand == placeBlueprint) ? new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MinifyEverything), nameof(ReplaceBlueprintForBuild))) : instruction;
        }

        static HashSet<WeakReference> usedStuff = new HashSet<WeakReference>();

        public static Blueprint ReplaceBlueprintForBuild(BuildableDef sourceDef, IntVec3 center, Map map, Rot4 rotation, Faction faction, ThingDef stuff)
        {
            if (sourceDef is ThingDef td &&
                td.Minifiable &&
                map.listerThings.ThingsOfDef(td.minifiedDef).OfType<MinifiedThing>().Where(t => t.GetInnerIfMinified().Stuff == stuff).FirstOrDefault(m => map.reachability.CanReach(center, m, PathEndMode.Touch, TraverseMode.ByPawn, Danger.Deadly)) is MinifiedThing mini &&
                !mini.IsForbidden(faction) &&
                !map.reservationManager.IsReservedByAnyoneOf(mini, faction) && !usedStuff.Any(wr => wr.IsAlive && wr.Target == mini))
            {
                usedStuff.Add(new WeakReference(mini));
                return GenConstruct.PlaceBlueprintForInstall(mini, center, map, rotation, faction);
            }
            else
                return GenConstruct.PlaceBlueprintForBuild(sourceDef, center, map, rotation, faction, stuff);
        }

        public static IEnumerable<CodeInstruction> InstallJobTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instructionList = instructions.ToList();
            MethodInfo implicitConverter = AccessTools.Method(typeof(LocalTargetInfo), "op_Implicit", new Type[] { typeof(Pawn) });

            for (int i = 0; i < instructionList.Count; i++)
            {
                CodeInstruction instruction = instructionList[i];
                yield return instruction;
                if (instruction.opcode == OpCodes.Call && instruction.operand == implicitConverter && instructionList[i + 1].opcode == OpCodes.Ldc_I4_1)
                    instructionList[i + 1].opcode = OpCodes.Ldc_I4_2;
            }
        }

        public static void AfterInstall(Thing createdThing)
        {
            createdThing = createdThing?.GetInnerIfMinified();
            if(createdThing is IThingHolder container)
                Find.CameraDriver.StartCoroutine(DoStuff(() => container.GetDirectlyHeldThings().RemoveAll(t => t.GetInnerIfMinified() == null)));
        }

        static public IEnumerator DoStuff(Action action)
        {
            yield return 500;
            action();
        }
    }
}
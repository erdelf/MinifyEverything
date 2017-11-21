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
using System.Threading;

namespace MinifyEverything
{
    
    class MinifySettings : ModSettings
    {
        public List<ThingDef> disabledDefList = new List<ThingDef>();

        public override void ExposeData()
        {
            base.ExposeData();
            List<string> list = this.disabledDefList?.Select(td => td.defName).ToList() ?? new List<string>();
            Scribe_Collections.Look(ref list, "disabledDefList");
            this.disabledDefList = list.Select(s => DefDatabase<ThingDef>.GetNamedSilentFail(s)).OfType<ThingDef>().ToList();
        }
    }

    [StaticConstructorOnStartup]
    class MinifyMod : Mod
    {
        public static MinifyMod instance;
        MinifySettings settings;
        Vector2 leftScrollPosition;
        Vector2 rightScrollPosition;
        string searchTerm = "";
        ThingDef leftSelectedDef;
        ThingDef rightSelectedDef;

        public MinifyMod(ModContentPack content) : base(content) => instance = this;

        internal MinifySettings Settings
        {
            get => settings ?? (settings = GetSettings<MinifySettings>());
            set => settings = value;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            Text.Font = GameFont.Medium;
            Rect topRect = inRect.TopPart(0.05f);
            this.searchTerm = Widgets.TextField(topRect.RightPart(0.95f).LeftPart(0.95f), this.searchTerm);
            Rect labelRect = inRect.TopPart(0.1f).BottomHalf();
            Rect bottomRect = inRect.BottomPart(0.9f);

            #region leftSide
            Rect leftRect = bottomRect.LeftHalf().RightPart(0.9f).LeftPart(0.9f);
            GUI.BeginGroup(leftRect, new GUIStyle(GUI.skin.box));
            List<ThingDef> found = DefDatabase<ThingDef>.AllDefs.Where(td =>
                td.Minifiable && (td.defName.Contains(this.searchTerm) || td.label.Contains(this.searchTerm)) && !this.Settings.disabledDefList.Contains(td)).OrderBy(td => td.LabelCap ?? td.defName).ToList();
            float num = 3f;
            Widgets.BeginScrollView(leftRect.AtZero(), ref this.leftScrollPosition, new Rect(0f, 0f, leftRect.width / 10 * 9, found.Count() * 32f));
            if (!found.NullOrEmpty())
            {
                foreach (ThingDef def in found)
                {
                    Rect rowRect = new Rect(5, num, leftRect.width - 6, 30);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    if (def == this.leftSelectedDef)
                        Widgets.DrawHighlightSelected(rowRect);
                    Widgets.Label(rowRect, def.LabelCap ?? def.defName);
                    if (Widgets.ButtonInvisible(rowRect))
                        this.leftSelectedDef = def;

                    num += 32f;
                }
            }
            Widgets.EndScrollView();
            GUI.EndGroup();
            #endregion


            #region rightSide

            Widgets.Label(labelRect.RightHalf().RightPart(0.9f), "Disabled Minifying for:");
            Rect rightRect = bottomRect.RightHalf().RightPart(0.9f).LeftPart(0.9f);
            GUI.BeginGroup(rightRect, GUI.skin.box);
            num = 6f;
            Widgets.BeginScrollView(rightRect.AtZero(), ref this.rightScrollPosition, new Rect(0f, 0f, rightRect.width / 5 * 4, this.Settings.disabledDefList.Count * 32f));
            if (!this.Settings.disabledDefList.NullOrEmpty())
            {
                foreach (ThingDef def in this.Settings.disabledDefList.Where(def => (def.defName.Contains(this.searchTerm) || def.label.Contains(this.searchTerm))))
                {
                    Rect rowRect = new Rect(5, num, leftRect.width - 6, 30);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    if (def == this.rightSelectedDef)
                        Widgets.DrawHighlightSelected(rowRect);
                    Widgets.Label(rowRect, def.LabelCap ?? def.defName);
                    if (Widgets.ButtonInvisible(rowRect))
                        this.rightSelectedDef = def;

                    num += 32f;
                }
            }
            Widgets.EndScrollView();
            GUI.EndGroup();
            #endregion


            #region buttons
            if (Widgets.ButtonImage(bottomRect.BottomPart(0.6f).TopPart(0.1f).RightPart(0.525f).LeftPart(0.1f), TexUI.ArrowTexRight) && this.leftSelectedDef != null)
            {
                this.Settings.disabledDefList.Add(this.leftSelectedDef);
                this.Settings.disabledDefList = this.Settings.disabledDefList.OrderBy(td => td.LabelCap ?? td.defName).ToList();
                this.rightSelectedDef = this.leftSelectedDef;
                this.leftSelectedDef = null;
                MinifyEverything.RemoveMinifiedFor(this.rightSelectedDef);
            }
            if (Widgets.ButtonImage(bottomRect.BottomPart(0.4f).TopPart(0.15f).RightPart(0.525f).LeftPart(0.1f), TexUI.ArrowTexLeft) && this.rightSelectedDef != null)
            {
                this.Settings.disabledDefList.Remove(this.rightSelectedDef);
                this.leftSelectedDef = this.rightSelectedDef;
                this.rightSelectedDef = null;
                MinifyEverything.AddMinifiedFor(this.leftSelectedDef);
            }
            #endregion

            this.Settings.Write();
        }

        public override string SettingsCategory() => "Minify Everything";
    }

    [StaticConstructorOnStartup]
    static class MinifyEverything
    {
        static MinifyEverything()
        {   
            minified = ThingDef.Named("MinifiedFurniture");
            DefDatabase<ThingDef>.AllDefsListForReading.ForEach(td =>
            {
                if(td.building != null && td.blueprintDef != null && !td.Minifiable)
                {
                        if(td.defName.Contains("Bed"))
                        Log.Message("hey");
                    AddMinifiedFor(td);
                }
            });
            MinifyMod.instance.Settings.disabledDefList.ForEach(td => RemoveMinifiedFor(td));
            HarmonyInstance harmony = HarmonyInstance.Create("rimworld.erdelf.minify_everything");
            harmony.Patch(AccessTools.Method(typeof(Blueprint_Install), nameof(Blueprint_Install.TryReplaceWithSolidThing)), null, new HarmonyMethod(typeof(MinifyEverything), nameof(AfterInstall)));
            harmony.Patch(AccessTools.Method(typeof(WorkGiver_ConstructDeliverResources), "InstallJob"), null, null, new HarmonyMethod(typeof(MinifyEverything), nameof(InstallJobTranspiler)));
            //harmony.Patch(AccessTools.Method(typeof(Designator_Build), nameof(Designator.DesignateSingleCell)), null, null, new HarmonyMethod(typeof(MinifyEverything), nameof(DesignateSingleCellTranspiler)));
            harmony.Patch(AccessTools.Method(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), nameof(WorkGiver_Scanner.JobOnThing)), new HarmonyMethod(typeof(MinifyEverything), nameof(JobOnThingPrefix)), null);
        }
        static MethodInfo shortHashGiver = AccessTools.Method(typeof(ShortHashGiver), "GiveShortHash");
        static ThingDef minified;

        public static void AddMinifiedFor(ThingDef def)
        {
            def.minifiedDef = minified;
            ThingDef minifiedDef = Traverse.Create(typeof(ThingDefGenerator_Buildings)).Method("NewBlueprintDef_Thing", def, true, def.blueprintDef).GetValue<ThingDef>();
            minifiedDef.deepCommonality = 0f;
            minifiedDef.ResolveReferences();
            minifiedDef.PostLoad();
            shortHashGiver.Invoke(null, new object[] { minifiedDef, typeof(ThingDef) });
            //Log.Message(minifiedDef.defName);
            DefDatabase<ThingDef>.Add(minifiedDef);
        }

        public static void RemoveMinifiedFor(ThingDef def)
        {
            ThingDef td = ThingDef.Named(def.defName + ThingDefGenerator_Buildings.BlueprintDefNameSuffix + ThingDefGenerator_Buildings.InstallBlueprintDefNameSuffix);
            Traverse.Create(typeof(DefDatabase<ThingDef>)).Method("Remove", new Type[] { typeof(ThingDef) }).GetValue(td);
            def.minifiedDef = null;
        }

        public static bool JobOnThingPrefix(Pawn pawn, Thing t)
        {
            try
            {
                if (t is Blueprint_Build bb)
                {
                    Def sourceDef = bb.def.entityDefToBuild;
                    if (sourceDef is ThingDef td && td.Minifiable &&
                            t.Map.listerThings.ThingsOfDef(td.minifiedDef).OfType<MinifiedThing>().Where(mf =>
                            mf.GetInnerIfMinified().Stuff == bb.stuffToUse).FirstOrDefault(m =>
                            pawn.CanReserveAndReach(m, PathEndMode.Touch, Danger.Deadly)) is MinifiedThing mini &&
                            !mini.IsForbidden(pawn.Faction) && mini.GetInnerIfMinified().def == td &&
                            InstallBlueprintUtility.ExistingBlueprintFor(mini) == null)
                    {
                        IntVec3 pos = t.Position;
                        Rot4 rot4 = t.Rotation;
                        Faction fac = t.Faction;
                        t.Destroy();
                        GenConstruct.PlaceBlueprintForInstall(mini, pos, mini.Map, rot4, fac);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Message(ex.ToString());
            }
            return true;
        }

        public static IEnumerable<CodeInstruction> DesignateSingleCellTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo placeBlueprint = AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForBuild));
            foreach (CodeInstruction instruction in instructions)
                yield return (instruction.opcode == OpCodes.Call && instruction.operand == placeBlueprint) ? 
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MinifyEverything), nameof(ReplaceBlueprintForBuild))) : 
                    instruction;
        }

        public static Blueprint ReplaceBlueprintForBuild(BuildableDef sourceDef, IntVec3 center, Map map, Rot4 rotation, Faction faction, ThingDef stuff)
        {
            if (sourceDef is ThingDef td &&
                td.Minifiable &&
                map.listerThings.ThingsOfDef(td.minifiedDef).OfType<MinifiedThing>().Where(t => t.GetInnerIfMinified().Stuff == stuff).FirstOrDefault(m => map.reachability.CanReach(center, m, PathEndMode.Touch, TraverseMode.ByPawn, Danger.Deadly)) is MinifiedThing mini &&
                !mini.IsForbidden(faction) && mini.GetInnerIfMinified().def == td &&
                !map.reservationManager.IsReservedByAnyoneOf(mini, faction) &&
                InstallBlueprintUtility.ExistingBlueprintFor(mini) == null)
            {
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
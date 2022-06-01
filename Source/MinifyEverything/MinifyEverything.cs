using RimWorld;
using System;
using System.Reflection;
using System.Linq;
using Verse;
using HarmonyLib;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse.AI;

namespace MinifyEverything
{
    internal class MinifySettings : ModSettings
    {
        public List<ThingDef> disabledDefList = new List<ThingDef>();

        public override void ExposeData()
        {
            base.ExposeData();
            List<string> list = this.disabledDefList?.Select(selector: td => td.defName).ToList() ?? new List<string>();
            Scribe_Collections.Look(list: ref list, label: "disabledDefList");
            this.disabledDefList = list.Select(selector: DefDatabase<ThingDef>.GetNamedSilentFail).Where(predicate: td => td != null).ToList();
        }
    }

    [StaticConstructorOnStartup]
    internal class MinifyMod : Mod
    {
        public static MinifyMod      instance;
        private       MinifySettings settings;
        private       Vector2        leftScrollPosition;
        private       Vector2        rightScrollPosition;
        private       string         searchTerm = "";
        private       ThingDef       leftSelectedDef;
        private       ThingDef       rightSelectedDef;

        public MinifyMod(ModContentPack content) : base(content: content) => instance = this;

        internal MinifySettings Settings
        {
            get => this.settings ?? (this.settings = this.GetSettings<MinifySettings>());
            set => this.settings = value;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect: inRect);
            Text.Font = GameFont.Medium;
            Rect topRect = inRect.TopPart(pct: 0.05f);
            this.searchTerm = Widgets.TextField(rect: topRect.RightPart(pct: 0.95f).LeftPart(pct: 0.95f), text: this.searchTerm);
            Rect labelRect  = inRect.TopPart(pct: 0.1f).BottomHalf();
            Rect bottomRect = inRect.BottomPart(pct: 0.9f);

        #region leftSide

            Rect leftRect = bottomRect.LeftHalf().RightPart(pct: 0.9f).LeftPart(pct: 0.9f);
            GUI.BeginGroup(position: leftRect, style: new GUIStyle(other: GUI.skin.box));
            List<ThingDef> found = DefDatabase<ThingDef>.AllDefs.Where(predicate: td =>
                                                                                      td.Minifiable                                                                              &&
                                                                                      (td.defName.Contains(value: this.searchTerm) || td.label.Contains(value: this.searchTerm)) &&
                                                                                      !this.Settings.disabledDefList.Contains(item: td)).OrderBy(keySelector: td => td.LabelCap.RawText ?? td.defName)
                                                     .ToList();
            float num = 3f;
            Widgets.BeginScrollView(outRect: leftRect.AtZero(), scrollPosition: ref this.leftScrollPosition,
                                    viewRect: new Rect(x: 0f, y: 0f, width: leftRect.width / 10 * 9, height: found.Count * 32f));
            if (!found.NullOrEmpty())
            {
                foreach (ThingDef def in found)
                {
                    Rect rowRect = new Rect(x: 5, y: num, width: leftRect.width - 6, height: 30);
                    Widgets.DrawHighlightIfMouseover(rect: rowRect);
                    if (def == this.leftSelectedDef)
                        Widgets.DrawHighlightSelected(rect: rowRect);
                    Widgets.Label(rect: rowRect, label: def.LabelCap.RawText ?? def.defName);
                    if (Widgets.ButtonInvisible(butRect: rowRect))
                        this.leftSelectedDef = def;

                    num += 32f;
                }
            }

            Widgets.EndScrollView();
            GUI.EndGroup();

        #endregion


        #region rightSide

            Widgets.Label(rect: labelRect.RightHalf().RightPart(pct: 0.9f), label: "Disabled Minifying for:");
            Rect rightRect = bottomRect.RightHalf().RightPart(pct: 0.9f).LeftPart(pct: 0.9f);
            GUI.BeginGroup(position: rightRect, style: GUI.skin.box);
            num = 6f;
            Widgets.BeginScrollView(outRect: rightRect.AtZero(), scrollPosition: ref this.rightScrollPosition,
                                    viewRect: new Rect(x: 0f, y: 0f, width: rightRect.width / 5 * 4, height: this.Settings.disabledDefList.Count * 32f));
            if (!this.Settings.disabledDefList.NullOrEmpty())
            {
                foreach (ThingDef def in this.Settings.disabledDefList.Where(predicate: def => (def.defName.Contains(value: this.searchTerm) || def.label.Contains(value: this.searchTerm))))
                {
                    Rect rowRect = new Rect(x: 5, y: num, width: leftRect.width - 6, height: 30);
                    Widgets.DrawHighlightIfMouseover(rect: rowRect);
                    if (def == this.rightSelectedDef)
                        Widgets.DrawHighlightSelected(rect: rowRect);
                    Widgets.Label(rect: rowRect, label: def.LabelCap.RawText ?? def.defName);
                    if (Widgets.ButtonInvisible(butRect: rowRect))
                        this.rightSelectedDef = def;

                    num += 32f;
                }
            }

            Widgets.EndScrollView();
            GUI.EndGroup();

        #endregion


        #region buttons

            if (Widgets.ButtonImage(butRect: bottomRect.BottomPart(pct: 0.6f).TopPart(pct: 0.1f).RightPart(pct: 0.525f).LeftPart(pct: 0.1f), tex: TexUI.ArrowTexRight) &&
                this.leftSelectedDef != null)
            {
                this.Settings.disabledDefList.Add(item: this.leftSelectedDef);
                this.Settings.disabledDefList = this.Settings.disabledDefList.OrderBy(keySelector: td => td.LabelCap.RawText ?? td.defName).ToList();
                this.rightSelectedDef         = this.leftSelectedDef;
                this.leftSelectedDef          = null;
                MinifyEverything.RemoveMinifiedFor(def: this.rightSelectedDef);
            }

            if (Widgets.ButtonImage(butRect: bottomRect.BottomPart(pct: 0.4f).TopPart(pct: 0.15f).RightPart(pct: 0.525f).LeftPart(pct: 0.1f), tex: TexUI.ArrowTexLeft) &&
                this.rightSelectedDef != null)
            {
                this.Settings.disabledDefList.Remove(item: this.rightSelectedDef);
                this.leftSelectedDef  = this.rightSelectedDef;
                this.rightSelectedDef = null;
                MinifyEverything.AddMinifiedFor(def: this.leftSelectedDef);
            }

        #endregion

            this.Settings.Write();
        }

        public override string SettingsCategory() => "Minify Everything";
    }

    [StaticConstructorOnStartup]
    public static class MinifyEverything
    {
        static MinifyEverything()
        {
            minified = ThingDef.Named(defName: "MinifiedThing");

            ThingCategoryDef category = ThingCategoryDef.Named(defName: "BuildingsMisc");

            IEnumerable<ThingDef> toPatch = DefDatabase<ThingDef>.AllDefsListForReading.Where(td =>
                                                                                              {
                                                                                                  if (td.defName.StartsWith("Smooth"))
                                                                                                      return false;

                                                                                                  if (!td.Claimable || td.graphicData == null || td.building.isNaturalRock)
                                                                                                      return false;
                                                                                                  if (td.thingCategories == null && !td.building.isNaturalRock)
                                                                                                  {
                                                                                                      td.thingCategories = new List<ThingCategoryDef> {category};
                                                                                                      category.childThingDefs.Add(item: td);
                                                                                                  }
                                                                                                  return !td.Minifiable;
                                                                                            });
            foreach (ThingDef thingDef in toPatch.ToHashSet()) 
                AddMinifiedFor(def: thingDef);


            MinifyMod.instance.Settings.disabledDefList.ForEach(action: RemoveMinifiedFor);
            Harmony harmony = new Harmony(id: "rimworld.erdelf.minify_everything");
            harmony.Patch(original: AccessTools.Method(type: typeof(Blueprint_Install), name: nameof(Blueprint_Install.TryReplaceWithSolidThing)), prefix: null,
                          postfix: new HarmonyMethod(methodType: typeof(MinifyEverything), methodName: nameof(AfterInstall)));
            harmony.Patch(original: AccessTools.Method(type: typeof(WorkGiver_ConstructDeliverResources), name: "InstallJob"), prefix: null, postfix: null,
                          transpiler: new HarmonyMethod(methodType: typeof(MinifyEverything), methodName: nameof(InstallJobTranspiler)));
            //harmony.Patch(AccessTools.Method(typeof(Designator_Build), nameof(Designator.DesignateSingleCell)), null, null, new HarmonyMethod(typeof(MinifyEverything), nameof(DesignateSingleCellTranspiler)));
            harmony.Patch(original: AccessTools.Method(type: typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), name: nameof(WorkGiver_Scanner.JobOnThing)),
                          prefix: new HarmonyMethod(methodType: typeof(MinifyEverything), methodName: nameof(JobOnThingPrefix)), postfix: null);
        }

        private static readonly MethodInfo shortHashGiver = AccessTools.Method(type: typeof(ShortHashGiver), name: "GiveShortHash");
        private static readonly ThingDef   minified;

        private static readonly MethodInfo blueprintInfo = AccessTools.Method(typeof(ThingDefGenerator_Buildings), "NewBlueprintDef_Thing");

        public static void AddMinifiedFor(ThingDef def)
        {
            def.minifiedDef = minified;

            

            if (def.blueprintDef == null)
                blueprintInfo.Invoke(null, new object[] {def, false, null});
            ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(null, new object[] {def, true, def.blueprintDef});
            minifiedDef.deepCommonality = 0f;
            minifiedDef.ResolveReferences();
            minifiedDef.PostLoad();
            shortHashGiver.Invoke(obj: null, parameters: new object[] {minifiedDef, typeof(ThingDef)});
            //Log.Message(minifiedDef.defName);
            DefDatabase<ThingDef>.Add(def: minifiedDef);
        }

        public static void RemoveMinifiedFor(ThingDef def)
        {
            ThingDef td = ThingDef.Named(defName: ThingDefGenerator_Buildings.BlueprintDefNamePrefix + ThingDefGenerator_Buildings.InstallBlueprintDefNamePrefix + def.defName);
            Traverse.Create(type: typeof(DefDatabase<ThingDef>)).Method(name: "Remove", paramTypes: new[] {typeof(ThingDef)}).GetValue(td);
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
                        t.Map.listerThings.ThingsOfDef(def: td.minifiedDef).OfType<MinifiedThing>().Where(predicate: mf =>
                                                                                                                         mf.GetInnerIfMinified().Stuff == bb.stuffToUse)
                      .FirstOrDefault(predicate: m =>
                                                     pawn.CanReserveAndReach(target: m, peMode: PathEndMode.Touch, maxDanger: Danger.Deadly)) is MinifiedThing mini &&
                        !mini.IsForbidden(faction: pawn.Faction)                                                                                                    &&
                        mini.GetInnerIfMinified().def == td                                                                                                         &&
                        InstallBlueprintUtility
                        .ExistingBlueprintFor(th: mini) == null)
                    {
                        IntVec3 pos  = t.Position;
                        Rot4    rot4 = t.Rotation;
                        Faction fac  = t.Faction;
                        t.Destroy();
                        GenConstruct.PlaceBlueprintForInstall(itemToInstall: mini, center: pos, map: mini.Map, rotation: rot4, faction: fac);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Message(text: ex.ToString());
            }

            return true;
        }

        public static IEnumerable<CodeInstruction> DesignateSingleCellTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo placeBlueprint = AccessTools.Method(type: typeof(GenConstruct), name: nameof(GenConstruct.PlaceBlueprintForBuild));
            foreach (CodeInstruction instruction in instructions)
                yield return (instruction.opcode == OpCodes.Call && instruction.operand == placeBlueprint)
                                 ? new CodeInstruction(opcode: OpCodes.Call, operand: AccessTools.Method(type: typeof(MinifyEverything), name: nameof(ReplaceBlueprintForBuild)))
                                 : instruction;
        }

        public static Blueprint ReplaceBlueprintForBuild(BuildableDef sourceDef, IntVec3 center, Map map, Rot4 rotation, Faction faction, ThingDef stuff)
        {
            if (sourceDef is ThingDef td &&
                td.Minifiable            &&
                map.listerThings.ThingsOfDef(def: td.minifiedDef).OfType<MinifiedThing>().Where(predicate: t => t.GetInnerIfMinified().Stuff == stuff)
                .FirstOrDefault(predicate: m => map.reachability.CanReach(start: center, dest: m, peMode: PathEndMode.Touch, traverseMode: TraverseMode.ByPawn, maxDanger: Danger.Deadly)) is
                    MinifiedThing mini                                                       &&
                !mini.IsForbidden(faction: faction)                                          && mini.GetInnerIfMinified().def == td &&
                !map.reservationManager.IsReservedByAnyoneOf(target: mini, faction: faction) &&
                InstallBlueprintUtility.ExistingBlueprintFor(th: mini) == null)
            {
                return GenConstruct.PlaceBlueprintForInstall(itemToInstall: mini, center: center, map: map, rotation: rotation, faction: faction);
            }
            else
                return GenConstruct.PlaceBlueprintForBuild(sourceDef: sourceDef, center: center, map: map, rotation: rotation, faction: faction, stuff: stuff);
        }

        public static IEnumerable<CodeInstruction> InstallJobTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instructionList   = instructions.ToList();
            MethodInfo            implicitConverter = AccessTools.Method(type: typeof(LocalTargetInfo), name: "op_Implicit", parameters: new[] {typeof(Pawn)});

            for (int i = 0; i < instructionList.Count; i++)
            {
                CodeInstruction instruction = instructionList[index: i];
                yield return instruction;
                if (instruction.opcode == OpCodes.Call && instruction.operand == implicitConverter && instructionList[index: i + 1].opcode == OpCodes.Ldc_I4_1)
                    instructionList[index: i                                                                                   + 1].opcode = OpCodes.Ldc_I4_2;
            }
        }

        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndexRef = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");

        public static void AfterInstall(Thing createdThing)
        {
            createdThing = createdThing?.GetInnerIfMinified();
            if (createdThing != null)
                Find.CameraDriver.StartCoroutine(routine: DoStuff(action: () =>
                                                                          {
                                                                              if (createdThing is IThingHolder container)
                                                                                  container.GetDirectlyHeldThings().RemoveAll(predicate: t => t.GetInnerIfMinified() == null);
                                                                              IntVec3 loc = createdThing.Position;
                                                                              Map map = createdThing.Map;
                                                                              Rot4 rotation = createdThing.Rotation;
                                                                              createdThing.DeSpawn();
                                                                              Find.CameraDriver.StartCoroutine(DoStuff(() => GenSpawn.Spawn(createdThing, loc, map, rotation)));
                                                                              //createdThing.SpawnSetup(map, false);
                                                                          }));
        }

        public static IEnumerator DoStuff(Action action)
        {
            yield return 500;
            action();
        }
    }
}
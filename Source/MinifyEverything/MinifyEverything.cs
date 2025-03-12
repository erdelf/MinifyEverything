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
    using JetBrains.Annotations;

    internal class MinifySettings : ModSettings
    {
        public List<ThingDef> disabledDefList = [];

        public override void ExposeData()
        {
            base.ExposeData();
            List<string> list = this.disabledDefList?.Select(td => td.defName).ToList() ?? [];
            Scribe_Collections.Look(ref list, "disabledDefList");
            this.disabledDefList = list.Select(DefDatabase<ThingDef>.GetNamedSilentFail).Where(td => td != null).ToList();
        }
    }

    [StaticConstructorOnStartup]
    [UsedImplicitly]
    internal class MinifyMod : Mod
    {
        public static bool listHandledByOtherMod = false;

        public static MinifyMod      instance;
        private       MinifySettings settings;
        private       Vector2        leftScrollPosition;
        private       Vector2        rightScrollPosition;
        private       string         searchTerm = "";
        private       ThingDef       leftSelectedDef;
        private       ThingDef       rightSelectedDef;

        public MinifyMod(ModContentPack content) : base(content)
        {
            instance = this;
            Harmony harmony = new("rimworld.erdelf.minify_everything");

            harmony.Patch(AccessTools.Method(typeof(MinifiedThing), "get_Graphic"), prefix: new HarmonyMethod(typeof(MinifyEverything), nameof(MinifyEverything.MinifiedThingGetGraphic)));
            harmony.Patch(AccessTools.Method(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PreResolve)), 
                                           postfix: new HarmonyMethod(typeof(MinifyEverything), nameof(MinifyEverything.GenerateImpliedDefsPostfix)));

            harmony.Patch(AccessTools.Method(typeof(Blueprint_Install), nameof(Blueprint_Install.TryReplaceWithSolidThing)),
                          postfix: new HarmonyMethod(typeof(MinifyEverything), nameof(MinifyEverything.AfterInstall)));
            harmony.Patch(AccessTools.Method(typeof(WorkGiver_ConstructDeliverResources), "InstallJob"), transpiler: new HarmonyMethod(typeof(MinifyEverything), nameof(MinifyEverything.InstallJobTranspiler)));
            //harmony.Patch(AccessTools.Method(typeof(Designator_Build), nameof(Designator.DesignateSingleCell)), null, null, new HarmonyMethod(typeof(MinifyEverything), nameof(DesignateSingleCellTranspiler)));
            harmony.Patch(AccessTools.Method(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), nameof(WorkGiver_Scanner.JobOnThing)),
                          new HarmonyMethod(typeof(MinifyEverything), nameof(MinifyEverything.JobOnThingPrefix)));
            harmony.Patch(AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(ThingDef), nameof(ThingDef.ConfigErrors))), transpiler: new HarmonyMethod(typeof(MinifyEverything), nameof(MinifyEverything.ConfigErrorTranspiler)));
        }

        internal MinifySettings Settings => this.settings ??= this.GetSettings<MinifySettings>();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            Text.Font = GameFont.Medium;
            Rect topRect = inRect.TopPart(0.05f);
            this.searchTerm = Widgets.TextField(topRect.RightPart(0.95f).LeftPart(0.95f), this.searchTerm);
            Rect labelRect  = inRect.TopPart(0.1f).BottomHalf();
            Rect bottomRect = inRect.BottomPart(0.9f);

        #region leftSide

            Rect leftRect = bottomRect.LeftHalf().RightPart(0.9f).LeftPart(0.9f);
            GUI.BeginGroup(leftRect, new GUIStyle(GUI.skin.box));
            List<ThingDef> found = DefDatabase<ThingDef>.AllDefs.Where(td =>
                                                                                      td.Minifiable                                                                              &&
                                                                                      (td.defName.Contains(this.searchTerm) || td.label.Contains(this.searchTerm)) &&
                                                                                      !this.Settings.disabledDefList.Contains(td)).OrderBy(td => td.LabelCap.RawText ?? td.defName)
                                                     .ToList();
            float num = 3f;
            Widgets.BeginScrollView(leftRect.AtZero(), ref this.leftScrollPosition,
                                    new Rect(0f, 0f, leftRect.width / 10 * 9, found.Count * 32f));
            if (!found.NullOrEmpty())
            {
                foreach (ThingDef def in found)
                {
                    Rect rowRect = new(5, num, leftRect.width - 6, 30);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    if (def == this.leftSelectedDef)
                        Widgets.DrawHighlightSelected(rowRect);
                    Widgets.Label(rowRect, def.LabelCap.RawText ?? def.defName);
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
            Widgets.BeginScrollView(rightRect.AtZero(), ref this.rightScrollPosition,
                                    new Rect(0f, 0f, rightRect.width / 5 * 4, this.Settings.disabledDefList.Count * 32f));
            if (!this.Settings.disabledDefList.NullOrEmpty())
            {
                foreach (ThingDef def in this.Settings.disabledDefList.Where(def => (def.defName.Contains(this.searchTerm) || def.label.Contains(this.searchTerm))))
                {
                    Rect rowRect = new(5, num, leftRect.width - 6, 30);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    if (def == this.rightSelectedDef)
                        Widgets.DrawHighlightSelected(rowRect);
                    Widgets.Label(rowRect, def.LabelCap.RawText ?? def.defName);
                    if (Widgets.ButtonInvisible(rowRect))
                        this.rightSelectedDef = def;

                    num += 32f;
                }
            }

            Widgets.EndScrollView();
            GUI.EndGroup();

        #endregion


        #region buttons

            if (Widgets.ButtonImage(bottomRect.BottomPart(0.6f).TopPart(0.1f).RightPart(0.525f).LeftPart(0.1f), TexUI.ArrowTexRight) &&
                this.leftSelectedDef != null)
            {
                this.Settings.disabledDefList.Add(this.leftSelectedDef);
                this.Settings.disabledDefList = this.Settings.disabledDefList.OrderBy(td => td.LabelCap.RawText ?? td.defName).ToList();
                this.rightSelectedDef         = this.leftSelectedDef;
                this.leftSelectedDef          = null;
                MinifyEverything.RemoveMinifiedFor(this.rightSelectedDef);
            }

            if (Widgets.ButtonImage(bottomRect.BottomPart(0.4f).TopPart(0.15f).RightPart(0.525f).LeftPart(0.1f), TexUI.ArrowTexLeft) &&
                this.rightSelectedDef != null)
            {
                this.Settings.disabledDefList.Remove(this.rightSelectedDef);
                this.leftSelectedDef  = this.rightSelectedDef;
                this.rightSelectedDef = null;
                MinifyEverything.AddMinifiedFor(this.leftSelectedDef);
            }

        #endregion

            this.Settings.Write();
        }

        public override string SettingsCategory() => "Minify Everything";
    }

    public static class MinifyEverything
    {
        private delegate        void          GiveShortHash(Def def, Type defType, HashSet<ushort> takenHashes);
        private static readonly GiveShortHash giveShortHash = AccessTools.MethodDelegate<GiveShortHash>(AccessTools.Method(typeof(ShortHashGiver), "GiveShortHash"));

        private static ThingDef         minified;
        private static ThingCategoryDef defaultCategory;

        private delegate        ThingDef              NewBlueprintDef_Thing(ThingDef def, bool isInstallBlueprint, ThingDef normalBlueprint = null, bool hotReload = false);
        private static readonly NewBlueprintDef_Thing blueprintGen = AccessTools.MethodDelegate<NewBlueprintDef_Thing>(AccessTools.Method(typeof(ThingDefGenerator_Buildings), "NewBlueprintDef_Thing"));

        public static readonly AccessTools.FieldRef<Dictionary<Type, HashSet<ushort>>> takenShortHashes =
            AccessTools.StaticFieldRefAccess<Dictionary<Type, HashSet<ushort>>>(AccessTools.Field(typeof(ShortHashGiver), "takenHashesPerDeftype"));

        private static readonly AccessTools.FieldRef<MinifiedThing, Graphic> cachedGraphic = AccessTools.FieldRefAccess<MinifiedThing, Graphic>("cachedGraphic");
		
        public static bool MinifiedThingGetGraphic(MinifiedThing __instance, ref Graphic __result)
        {
	        ref var cache = ref cachedGraphic(__instance);
	        if (cache != null)
	        {
		        __result = cache;
                return false;
	        }

	        try
	        {
		        _ = __instance.InnerThing.Graphic;
		        return true;
	        }
	        catch (Exception)
	        {
		        cache = __instance.InnerThing.DefaultGraphic;
		        __result = cache;
		        return false;
            }
        }

        public static void AddMinifiedFor(ThingDef def, bool hash = true)
        {
            def.minifiedDef = minified;

            if (def.blueprintDef == null)
                blueprintGen(def, false);

            //AddCategoriesIfNeeded(def, true);

            ThingDef minifiedDef = blueprintGen(def, true, def.blueprintDef);
            minifiedDef.deepCommonality = 0f;
            minifiedDef.ResolveReferences();
            minifiedDef.PostLoad();
            if(hash)
                giveShortHash(minifiedDef, typeof(ThingDef), takenShortHashes()[typeof(ThingDef)]);
            //Log.Message(minifiedDef.defName);
            DefDatabase<ThingDef>.Add(minifiedDef);
        }

        [Obsolete]
        public static void AddMinifiedFor(ThingDef def)
        {
            AddMinifiedFor(def, true);
        }

        public static void RemoveMinifiedFor(ThingDef def)
        {
            ThingDef td = ThingDef.Named(ThingDefGenerator_Buildings.BlueprintDefNamePrefix + ThingDefGenerator_Buildings.InstallBlueprintDefNamePrefix + def.defName);
            if (td != null)
            {
                Traverse.Create(typeof(DefDatabase<ThingDef>)).Method("Remove", [typeof(ThingDef)]).GetValue(td);
                def.minifiedDef = null;
            }
        }

        public static void GenerateImpliedDefsPostfix()
        {
            minified        = ThingDefOf.MinifiedThing;
            defaultCategory = ThingCategoryDef.Named("BuildingsMisc");

            if (!MinifyMod.listHandledByOtherMod)
            {
                IEnumerable<ThingDef> toPatch = DefDatabase<ThingDef>.AllDefsListForReading.Where(td =>
                {
                    if (td.defName.StartsWith("Smooth"))
                        return false;

                    if (!td.Claimable || td.graphicData == null || td.building.isNaturalRock)
                        return false;

                    AddCategoriesIfNeeded(td, false);

                    if (!td.StatBaseDefined(StatDefOf.Mass)) 
                        td.SetStatBaseValue(StatDefOf.Mass, td.CostList?.Sum(tdcc => tdcc.thingDef.BaseMass * tdcc.count) * 0.1f ?? 1);

                    return !td.Minifiable;
                });

                takenShortHashes().Add(typeof(ThingDef), []);

                foreach (ThingDef thingDef in toPatch.ToHashSet())
                    AddMinifiedFor(thingDef, false);

                foreach (ThingDef thingDef in MinifyMod.instance.Settings.disabledDefList) 
                    RemoveMinifiedFor(thingDef);
            }
        }

        private static void AddCategoriesIfNeeded(ThingDef td, bool addToCategory)
        {
            if (td.thingCategories.NullOrEmpty() && !td.building.isNaturalRock)
            {
                td.thingCategories ??= [];

                ThingCategoryDef category = defaultCategory;

                if (td.designationCategory != null)
                {
                    ThingCategoryDef categoryDef = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Buildings" + td.designationCategory.defName);
                    if (categoryDef != null)
	                    category = categoryDef;
                }

                td.thingCategories.Add(category);
                if (addToCategory)
                    defaultCategory.childThingDefs.Add(td);
            }
        }

        public static bool JobOnThingPrefix(Pawn pawn, Thing t)
        {
            try
            {
                if (t is Blueprint_Build bb)
                {
                    Def sourceDef = bb.def.entityDefToBuild;

                    if (sourceDef is ThingDef { Minifiable: true } td &&
                        t.Map.listerThings.ThingsOfDef(td.minifiedDef).OfType<MinifiedThing>().Where(mf => mf.GetInnerIfMinified().Stuff == bb.stuffToUse)
                         .FirstOrDefault(m => pawn.CanReserveAndReach(m, PathEndMode.Touch, Danger.Deadly)) is { } mini &&
                        !mini.IsForbidden(pawn.Faction) && mini.GetInnerIfMinified().def == td && InstallBlueprintUtility.ExistingBlueprintFor(mini) == null)
                    {
                        IntVec3 pos  = t.Position;
                        Rot4    rot4 = t.Rotation;
                        Faction fac  = t.Faction;
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
                yield return (instruction.Calls(placeBlueprint))
                                 ? new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MinifyEverything), nameof(ReplaceBlueprintForBuild)))
                                 : instruction;
        }

        public static Blueprint ReplaceBlueprintForBuild(BuildableDef sourceDef, IntVec3 center, Map map, Rot4 rotation, Faction faction, ThingDef stuff)
        {
            if (sourceDef is ThingDef { Minifiable: true } td &&
                map.listerThings.ThingsOfDef(td.minifiedDef).OfType<MinifiedThing>().Where(t => t.GetInnerIfMinified().Stuff == stuff)
                   .FirstOrDefault(m => map.reachability.CanReach(center, m, PathEndMode.Touch, TraverseMode.ByPawn, Danger.Deadly)) is { } mini &&
                !mini.IsForbidden(faction)                                                                                                       && mini.GetInnerIfMinified().def == td &&
                !map.reservationManager.IsReservedByAnyoneOf(mini, faction)                                                                      &&
                InstallBlueprintUtility.ExistingBlueprintFor(mini) == null)
            {
                return GenConstruct.PlaceBlueprintForInstall(mini, center, map, rotation, faction);
            }
            else
                return GenConstruct.PlaceBlueprintForBuild(sourceDef, center, map, rotation, faction, stuff);
        }

        public static IEnumerable<CodeInstruction> InstallJobTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instructionList   = instructions.ToList();
            MethodInfo            implicitConverter = AccessTools.Method(typeof(LocalTargetInfo), "op_Implicit", new[] {typeof(Pawn)});

            for (int i = 0; i < instructionList.Count; i++)
            {
                CodeInstruction instruction = instructionList[i];
                yield return instruction;
                if (instruction.Calls(implicitConverter) && instructionList[i + 1].opcode == OpCodes.Ldc_I4_1)
                    instructionList[i                                                                                   + 1].opcode = OpCodes.Ldc_I4_2;
            }
        }

        public static void AfterInstall(Thing createdThing)
        { 
            Thing innerThing = createdThing?.GetInnerIfMinified();
            if (innerThing != createdThing)
                Find.CameraDriver.StartCoroutine(DoStuff(() =>
                                                                          {
                                                                              if (innerThing is IThingHolder container)
                                                                                  container.GetDirectlyHeldThings().RemoveAll(t => t.GetInnerIfMinified() == null);
                                                                              IntVec3 loc = innerThing.Position;
                                                                              Map map = innerThing.Map;
                                                                              Rot4 rotation = innerThing.Rotation;
                                                                              innerThing.DeSpawn();
                                                                              Find.CameraDriver.StartCoroutine(DoStuff(() => GenSpawn.Spawn(innerThing, loc, map, rotation)));
                                                                              //createdThing.SpawnSetup(map, false);
                                                                          }));
        }

        public static IEnumerable<CodeInstruction> ConfigErrorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instructionList = instructions.ToList();

            FieldInfo  categoryInfo   = AccessTools.Field(typeof(ThingDef), nameof(ThingDef.category));
            MethodInfo minifiableInfo = AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.Minifiable));

            for (int index = 0; index < instructionList.Count; index++)
            {
                CodeInstruction instruction = instructionList[index];

                yield return instruction;

                if (instruction.opcode == OpCodes.Ldc_I4_3 && instructionList[index - 1].LoadsField(categoryInfo) && instructionList[index + 3].Calls(minifiableInfo))
                    instructionList[index + 1].opcode = OpCodes.Br_S;
            }
        }

        public static IEnumerator DoStuff(Action action)
        {
            yield return 500;
            action();
        }
    }
}
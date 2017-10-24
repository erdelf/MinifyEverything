using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;
using Harmony;
using UnityEngine;
using System.Collections;

namespace MinifyEverything
{
    [StaticConstructorOnStartup]
    class MinifyEverything : MonoBehaviour
    {
        static MinifyEverything instance;

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

            GameObject initializer = new GameObject("Ankh_Interpreter");
            instance = initializer.AddComponent<MinifyEverything>();
            DontDestroyOnLoad(initializer);
        }

        public static void AfterInstall(Thing createdThing)
        {
            createdThing = createdThing?.GetInnerIfMinified();

            if(createdThing is IThingHolder container)
                instance.StartCoroutine(nameof(DoStuff), (Action) (() => container.GetDirectlyHeldThings().RemoveAll(t => t.GetInnerIfMinified().Spawned)));
        }

        public IEnumerator DoStuff(Action action)
        {
            yield return 500;
            action();
        }
    }
}

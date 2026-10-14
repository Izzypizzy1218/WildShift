using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // An optional repair for missed apparel-change invalidation. Do not draw
    // apparel ourselves: the owning framework keeps texture/visibility rules.
    public static class AnimalApparelGraphics
    {
        public static void RefreshIfOutOfSync(Pawn animal)
        {
            if (animal == null || animal.Destroyed || animal.RaceProps == null
                || !animal.RaceProps.Animal || animal.apparel == null
                || !ModsConfig.IsActive("Ingendum.AnimalApparelFramework")
                || !TransformUtility.IsTransformedAnimal(animal)) return;

            PawnRenderTree tree = animal.Drawer.renderer.renderTree;
            // Normal change notifications already invalidate the tree. Let the
            // normal renderer rebuild it; there is nothing to repair here.
            if (!tree.Resolved) return;

            HashSet<Apparel> expected = new HashSet<Apparel>();
            foreach (Apparel apparel in animal.apparel.WornApparel)
            {
                if (!apparel.def.IsWeapon
                    && (apparel.def.apparel.tags == null
                        || !apparel.def.apparel.tags.Contains("AnimalInvisible")))
                    expected.Add(apparel);
            }

            HashSet<Apparel> displayed = new HashSet<Apparel>();
            CollectApparel(tree.rootNode, displayed);
            if (!expected.SetEquals(displayed))
                animal.Drawer.renderer.SetAllGraphicsDirty();
        }

        private static void CollectApparel(PawnRenderNode node, HashSet<Apparel> apparel)
        {
            if (node == null) return;
            if (node.apparel != null) apparel.Add(node.apparel);
            if (node.children == null) return;
            foreach (PawnRenderNode child in node.children) CollectApparel(child, apparel);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelAdded")]
    public static class Patch_TransformedApparelAdded
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn_ApparelTracker __instance)
        {
            AnimalApparelGraphics.RefreshIfOutOfSync(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelRemoved")]
    public static class Patch_TransformedApparelRemoved
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn_ApparelTracker __instance)
        {
            AnimalApparelGraphics.RefreshIfOutOfSync(__instance.pawn);
        }
    }
}

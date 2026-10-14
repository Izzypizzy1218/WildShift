using System.Reflection;
using HarmonyLib;
using Verse;

namespace WildShift
{
    // Optional: neither VEF nor an animal pack becomes a required dependency.
    public static class AnimalModCompatibility
    {
        public static void Install(Harmony harmony)
        {
            System.Type type = AccessTools.TypeByName("VEF.AnimalBehaviours.CompFixedGender");
            if (type == null) return;

            MethodInfo target = AccessTools.DeclaredMethod(type, "CompTick", System.Type.EmptyTypes);
            FieldInfo once = AccessTools.Field(type, "changeGenderOnce");
            if (target == null || once == null || once.FieldType != typeof(bool) || once.IsStatic || once.IsInitOnly)
            {
                Log.Warning("[WildShift] VEF fixed-gender hook was not found; transformed animal gender compatibility needs verification.");
                return;
            }

            try { harmony.Patch(target, prefix: new HarmonyMethod(typeof(AnimalModCompatibility), "FixedGenderPrefix")); }
            catch (System.Exception ex)
            {
                Log.Warning("[WildShift] Optional VEF gender integration could not be installed: " + ex);
            }
        }

        public static bool FixedGenderPrefix(ThingComp __instance, ref bool ___changeGenderOnce)
        {
            // Consume only the first-tick gender rewrite for transformed forms.
            // Leave the original callback running; later ticks need no health
            // lookup. No pawn cache: a newly added marker is checked while the
            // framework still has a pending gender rewrite.
            if (!___changeGenderOnce) return true;
            Pawn animal = __instance != null ? __instance.parent as Pawn : null;
            if (animal != null && animal.Spawned && TransformUtility.IsTransformedAnimal(animal))
                ___changeGenderOnce = false;
            return true;
        }
    }
}

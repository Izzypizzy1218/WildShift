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
            if (target == null)
            {
                Log.Warning("[WildShift] VEF fixed-gender hook was not found; transformed animal gender compatibility needs verification.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(AnimalModCompatibility), "FixedGenderPrefix"));
        }

        public static bool FixedGenderPrefix(ThingComp __instance)
        {
            // VEF normally forces clutch mothers to female on their first tick,
            // including after loading. Preserve WildShift's generation-time sex.
            // Use the existing component callback; no new pawn/tick scan is added.
            Pawn animal = __instance != null ? __instance.parent as Pawn : null;
            return !TransformUtility.IsTransformedAnimal(animal);
        }
    }
}

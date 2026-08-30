using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn), "get_IsColonistPlayerControlled")]
    public static class Patch_TransformedAnimalControl
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (__result
                || __instance == null
                || __instance.Dead
                || !__instance.Spawned
                || __instance.Faction != Faction.OfPlayer
                || __instance.RaceProps == null
                || !__instance.RaceProps.Animal
                || !TransformUtility.IsTransformedAnimal(__instance))
            {
                return;
            }

            TransformUtility.EnsureTransformedAnimalControl(__instance, true);
            __result = true;
        }
    }
}

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
                || __instance.RaceProps == null
                || !__instance.RaceProps.Animal
                || !__instance.Spawned
                // World generation queries ordinary faction leaders before a
                // player faction exists. Read this pawn's faction directly.
                || __instance.Faction?.IsPlayer != true
                || __instance.MentalStateDef != null
                || !TransformUtility.IsTransformedAnimal(__instance))
            {
                return;
            }

            TransformUtility.EnsureTransformedAnimalControl(__instance, true);
            __result = true;
        }
    }
}

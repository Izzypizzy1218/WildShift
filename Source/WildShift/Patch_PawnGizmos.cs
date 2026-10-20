using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Patch_PawnGizmos
    {
        private static readonly System.WeakReference<Game> checkedGame = new System.WeakReference<Game>(null);
        private static bool recoveryCheckCompleted;

        public static void Prefix(Pawn __instance)
        {
            Game currentGame = Current.Game;
            if (currentGame == null) return;
            Game previousGame;
            if (!checkedGame.TryGetTarget(out previousGame) || previousGame != currentGame)
            {
                checkedGame.SetTarget(currentGame);
                recoveryCheckCompleted = false;
            }

            if (recoveryCheckCompleted
                || __instance == null
                || __instance.Dead
                || !__instance.Spawned
                || __instance.Faction != Faction.OfPlayer
                || __instance.RaceProps == null
                || !__instance.RaceProps.Humanlike
                || !LoneBeastkinUtility.IsActiveScenario())
            {
                return;
            }

            if (TransformUtility.IsShapeshifter(__instance)
                || LoneBeastkinUtility.HasAnyPlayerShapeshifter())
            {
                recoveryCheckCompleted = true;
                return;
            }

            PawnKindDef kind = LoneBeastkinUtility.GetStartingAnimalKind();
            if (kind == null)
            {
                return;
            }

            TransformUtility.AddOrGetShapeshifter(__instance, kind, true);
            recoveryCheckCompleted = true;
            Log.Message("[WildShift] Restored missing Lone Beastkin shapeshifter state.");
        }
    }
}

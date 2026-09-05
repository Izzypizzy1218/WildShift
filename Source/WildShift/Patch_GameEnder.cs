using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(GameEnder), "CheckOrUpdateGameOver")]
    public static class Patch_GameEnder
    {
        private const int CacheDurationTicks = 120;
        private static Game cachedGame;
        private static int cacheExpiresAt = -1;
        private static bool cachedHasLivingColonist;

        public static bool Prefix(ref bool ___gameEnding, ref int ___ticksToGameOver)
        {
            if (!HasLivingTransformedColonist())
            {
                return true;
            }

            ___gameEnding = false;
            ___ticksToGameOver = 0;
            return false;
        }

        private static bool HasLivingTransformedColonist()
        {
            Game currentGame = Current.Game;
            int currentTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (cachedGame == currentGame && currentTick <= cacheExpiresAt)
            {
                return cachedHasLivingColonist;
            }

            cachedGame = currentGame;
            cacheExpiresAt = currentTick + CacheDurationTicks;
            cachedHasLivingColonist = ScanForLivingTransformedColonist();
            return cachedHasLivingColonist;
        }

        private static bool ScanForLivingTransformedColonist()
        {
            List<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int pawnIndex = 0; pawnIndex < pawns.Count; pawnIndex++)
            {
                Pawn animal = pawns[pawnIndex];
                // A transformed form is always an animal. Avoid scanning the
                // health tracker of every ordinary human colonist whenever
                // RimWorld checks its game-over state.
                if (animal == null
                    || animal.RaceProps == null
                    || !animal.RaceProps.Animal)
                {
                    continue;
                }

                HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(animal);
                Pawn human = comp != null ? comp.StoredPawn : null;
                if (human != null
                    && !human.Dead
                    && human.Faction == Faction.OfPlayer
                    && human.IsFreeColonist)
                {
                    return true;
                }
            }

            return false;
        }

        public static void InvalidateCache()
        {
            cachedGame = null;
            cacheExpiresAt = -1;
        }
    }
}

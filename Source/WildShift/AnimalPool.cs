using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WildShift
{
    public static class AnimalPool
    {
        private static List<PawnKindDef> cachedEligibleKinds;
        private static bool cachedAllowInsectoids;
        private static int cachedPawnKindCount = -1;

        private static readonly HashSet<string> BlacklistedRaceDefs = new HashSet<string>
        {
            "Cobra",
            "Fox_Arctic",
            "Fox_Fennec",
            "Fox_Red",
            "Lynx",
            "Wolverine"
        };

        public static IEnumerable<PawnKindDef> EligibleAnimals()
        {
            EnsureCache();
            return cachedEligibleKinds;
        }

        public static PawnKindDef RandomEligibleKind()
        {
            EnsureCache();
            return cachedEligibleKinds.RandomElementWithFallback();
        }

        public static bool IsEligible(Pawn pawn)
        {
            return pawn != null && IsEligible(pawn.kindDef);
        }

        public static bool IsEligible(PawnKindDef kind)
        {
            if (kind == null || kind.race == null)
            {
                return false;
            }

            return IsEligibleRace(kind.race);
        }

        public static bool IsEligibleRace(ThingDef raceDef)
        {
            RaceProperties race = raceDef != null ? raceDef.race : null;
            if (race == null || !race.Animal)
            {
                return false;
            }

            if (race.IsMechanoid)
            {
                return false;
            }

            if (BlacklistedRaceDefs.Contains(raceDef.defName))
            {
                return false;
            }

            bool allowInsectoids = WildShiftMod.Settings != null && WildShiftMod.Settings.allowInsectoids;
            if (!allowInsectoids && race.FleshType == FleshTypeDefOf.Insectoid)
            {
                return false;
            }

            // VERIFY: RimWorld 1.6 RaceProperties still exposes predator/baseBodySize with these names.
            return race.predator || race.baseBodySize > 1f;
        }

        private static void EnsureCache()
        {
            bool allowInsectoids = WildShiftMod.Settings != null && WildShiftMod.Settings.allowInsectoids;
            List<PawnKindDef> allKinds = DefDatabase<PawnKindDef>.AllDefsListForReading;
            if (cachedEligibleKinds != null
                && cachedAllowInsectoids == allowInsectoids
                && cachedPawnKindCount == allKinds.Count)
            {
                return;
            }

            cachedEligibleKinds = new List<PawnKindDef>();
            cachedAllowInsectoids = allowInsectoids;
            cachedPawnKindCount = allKinds.Count;
            for (int i = 0; i < allKinds.Count; i++)
            {
                PawnKindDef kind = allKinds[i];
                if (IsEligible(kind))
                {
                    cachedEligibleKinds.Add(kind);
                }
            }
        }
    }
}

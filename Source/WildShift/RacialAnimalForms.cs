using System.Collections.Generic;
using Verse;

namespace WildShift
{
    // Optional integrations use exact Def names, never translated labels or
    // package-name substring matching. No HAR or race-mod assembly is required.
    public static class RacialAnimalForms
    {
        public const float PreferenceChance = 0.5f;

        private static readonly Dictionary<string, string[]> RaceForms = new Dictionary<string, string[]>
        {
            { "Ratkin", new[] { "Rat" } },
            { "Kiiro_Race", new[] { "Cat" } },
            { "Alien_Nyaron", new[] { "Cat" } },
            { "Kurin_Race", new[] { "Fox_Red", "Fox_Arctic" } },
            { "ReviaRaceAlien", new[] { "Fox_Red" } },
            { "Alien_Miho", new[] { "Fox_Red" } },
            { "Rabbie", new[] { "Hare", "Snowhare" } },
            { "Yuran_Race", new[] { "Hare", "Snowhare" } },
            { "Alien_Bori", new[] { "Husky", "LabradorRetriever" } },
            { "Alien_SP", new[] { "Sheep" } }
        };

        private static readonly Dictionary<string, string[]> XenotypeForms = new Dictionary<string, string[]>
        {
            { "RK_XenoType_Ratkin", new[] { "Rat" } },
            { "YuranXenotype", new[] { "Hare", "Snowhare" } },
            { "Xeno_CelestialMiho", new[] { "Fox_Red" } },
            { "Xeno_CelestialMiho_Arctic", new[] { "Fox_Arctic" } },
            { "Xeno_CelestialMiho_Desert", new[] { "Fox_Fennec" } },
            { "Xeno_CelestialMiho_Highland", new[] { "Fox_Red" } },
            { "Xeno_CelestialMiho_Highmate", new[] { "Fox_Red" } },
            { "Xeno_CelestialMiho_Voidborn", new[] { "Fox_Red" } }
        };

        // Called only when assigning a new form, not every tick or transformation.
        public static PawnKindDef Choose(Pawn pawn, PawnKindDef fallback = null)
        {
            string[] names = GetPreferredNames(pawn);
            if (names != null && Rand.Chance(PreferenceChance))
            {
                List<PawnKindDef> available = new List<PawnKindDef>();
                for (int i = 0; i < names.Length; i++)
                {
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(names[i]);
                    if (IsSafeAnimal(kind))
                    {
                        available.Add(kind);
                    }
                }

                if (available.Count > 0)
                {
                    return available.RandomElement();
                }
            }

            return AnimalPool.IsEligible(fallback) ? fallback : AnimalPool.RandomEligibleKind();
        }

        public static bool IsAllowed(Pawn pawn, PawnKindDef kind)
        {
            if (AnimalPool.IsEligible(kind))
            {
                return true;
            }

            if (!IsSafeAnimal(kind))
            {
                return false;
            }

            string[] names = GetPreferredNames(pawn);
            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (kind.defName == names[i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsSafeAnimal(PawnKindDef kind)
        {
            return kind != null && kind.race != null && kind.race.race != null
                && kind.race.race.Animal && !kind.race.race.IsMechanoid;
        }

        private static string[] GetPreferredNames(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
            {
                return null;
            }

            string[] names;
            // Xenotype variants take precedence (e.g. arctic/desert Miho).
            if (pawn.genes != null && pawn.genes.Xenotype != null
                && XenotypeForms.TryGetValue(pawn.genes.Xenotype.defName, out names))
            {
                return names;
            }

            return pawn.def != null && RaceForms.TryGetValue(pawn.def.defName, out names) ? names : null;
        }
    }
}

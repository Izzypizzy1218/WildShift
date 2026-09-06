using RimWorld;
using Verse;

namespace WildShift
{
    public static class AnimalFormGender
    {
        public static PawnGenerationRequest CreateHumanRequest(Pawn animal)
        {
            // A genderless animal has no sex to carry over to a human body.
            Gender? gender = animal.gender == Gender.None ? (Gender?)null : animal.gender;
            return new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, fixedGender: gender);
        }

        public static PawnGenerationRequest CreateRequest(Pawn human, PawnKindDef kind)
        {
            // Supply gender before generation so sex-dependent graphics and
            // components are initialized consistently. Never reuse a world pawn.
            // Truly genderless species remain genderless.
            Gender gender = kind.race.race.hasGenders ? human.gender : Gender.None;
            return new PawnGenerationRequest(kind, human.Faction,
                forceGenerateNewPawn: true, fixedGender: gender);
        }
    }
}

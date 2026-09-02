using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(ReleaseAnimalToWildUtility), "DoReleaseAnimal")]
    public static class Patch_ReleasedTransformedAnimal
    {
        public static void Postfix(Pawn animal)
        {
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(animal);
            Pawn human = comp != null ? comp.StoredPawn : null;
            if (human == null)
            {
                return;
            }

            if (human.Faction != animal.Faction)
            {
                human.SetFaction(animal.Faction);
            }

            if (animal.drafter != null && animal.Drafted)
            {
                animal.drafter.Drafted = false;
            }

            Patch_GameEnder.InvalidateCache();
            if (Find.ColonistBar != null)
            {
                Find.ColonistBar.MarkColonistsDirty();
            }
        }
    }
}

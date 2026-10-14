using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Record only marked animals actually harmed by a player-owned instigator.
    // Stable IDs survive death/resurrection without retaining Pawn references or
    // changing the serialized type of latent hediffs in existing saves.
    public class GameComponent_LatentAnimalHarm : GameComponent
    {
        private HashSet<int> playerHarmedAnimals = new HashSet<int>();
        private HashSet<int> blockedMedicalRecruits = new HashSet<int>();

        public GameComponent_LatentAnimalHarm(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref playerHarmedAnimals, "wildShiftPlayerHarmedAnimals", LookMode.Value);
            if (playerHarmedAnimals == null) playerHarmedAnimals = new HashSet<int>();
            Scribe_Collections.Look(ref blockedMedicalRecruits, "wildShiftBlockedMedicalRecruits", LookMode.Value);
            if (blockedMedicalRecruits == null) blockedMedicalRecruits = new HashSet<int>();
        }

        public bool WasHarmed(Pawn animal)
        {
            return animal != null && playerHarmedAnimals.Contains(animal.thingIDNumber);
        }

        public bool WasMedicalAwakeningBlocked(Pawn animal)
        {
            return animal != null && blockedMedicalRecruits.Contains(animal.thingIDNumber);
        }

        public void RecordMedicalRecruitment(Pawn animal)
        {
            if (animal == null) return;
            if (WasHarmed(animal)) blockedMedicalRecruits.Add(animal.thingIDNumber);
            else blockedMedicalRecruits.Remove(animal.thingIDNumber);
        }

        public void RecordPurchase(Pawn animal)
        {
            if (animal != null) blockedMedicalRecruits.Remove(animal.thingIDNumber);
        }

        public void RecordDamage(Pawn animal, DamageInfo damage, float totalDamageDealt)
        {
            if (animal == null || totalDamageDealt <= 0f
                || damage.Instigator == null || damage.Instigator.Faction == null
                || !damage.Instigator.Faction.IsPlayer
                || animal.RaceProps == null || !animal.RaceProps.Animal
                || animal.health == null
                || animal.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker) == null)
                return;

            // Do not reject Dead: lethal hits also need to block serum exploits.
            playerHarmedAnimals.Add(animal.thingIDNumber);
        }
    }

    [HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
    public static class Patch_LatentAnimalPlayerHarm
    {
        public static void Prefix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            // Fast rejection before component/health lookup for ordinary damage.
            if (__instance == null || __instance.RaceProps == null || !__instance.RaceProps.Animal
                || totalDamageDealt <= 0f || dinfo.Instigator == null
                || dinfo.Instigator.Faction == null || !dinfo.Instigator.Faction.IsPlayer)
                return;
            Current.Game?.GetComponent<GameComponent_LatentAnimalHarm>()?.RecordDamage(__instance, dinfo, totalDamageDealt);
        }
    }

    [HarmonyPatch(typeof(TendUtility), "DoTend")]
    public static class Patch_MedicalBondAwakening
    {
        private static bool WasPlayerHarmed(Pawn animal)
        {
            return Current.Game?.GetComponent<GameComponent_LatentAnimalHarm>()?.WasHarmed(animal) ?? false;
        }

        public static void Prefix(Pawn doctor, Pawn patient, out bool __state)
        {
            __state = doctor != null && doctor.Faction != null && doctor.Faction.IsPlayer
                && patient != null && patient.Faction != doctor.Faction
                && Patch_Taming.IsLatentWildShapeshifter(patient);
        }

        public static void Postfix(Pawn patient, bool __state)
        {
            if (!__state || patient == null || patient.Faction != Faction.OfPlayer) return;
            Current.Game?.GetComponent<GameComponent_LatentAnimalHarm>()?.RecordMedicalRecruitment(patient);
            if (WasPlayerHarmed(patient)) return;
            // DoTend performs the native bond roll and recruitment. Wait until
            // the entire treatment finishes before replacing its patient; a
            // DoRecruit hook could destroy it while DoTend still uses it.
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                if (WasPlayerHarmed(patient))
                    Current.Game?.GetComponent<GameComponent_LatentAnimalHarm>()?.RecordMedicalRecruitment(patient);
                else Patch_Taming.RevealTamedShapeshifter(patient);
            });
        }
    }
}

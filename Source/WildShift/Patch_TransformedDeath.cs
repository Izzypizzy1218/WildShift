using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class Patch_TransformedDeath
    {
        // VERIFY: RimWorld 1.6 Pawn.Kill still has a DamageInfo? parameter named "dinfo".
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            HediffComp_Transformed transformed = TransformUtility.TryGetTransformedComp(__instance);
            if (transformed == null)
            {
                return true;
            }

            // ExecutionUtility can ask to kill its victim again after lethal
            // damage. The first pass has already reverted and vanished this
            // animal, so do not run Pawn.Kill on the obsolete shell a second
            // time.
            if (!transformed.HasStoredPawn && __instance.Destroyed)
            {
                return false;
            }

            Patch_GameEnder.InvalidateCache();

            // Slaughter uses ExecutionCut. It is an intentional execution, not
            // ordinary combat damage, so the stored human must die as well.
            bool executedBySlaughter = dinfo.HasValue && dinfo.Value.Def == DamageDefOf.ExecutionCut;
            float deathChance = WildShiftMod.Settings != null ? WildShiftMod.Settings.deathChance : 0.2f;
            bool humanDies = executedBySlaughter || Rand.Chance(deathChance);
            Pawn human = TransformUtility.RevertToHuman(__instance, !humanDies);
            if (human == null)
            {
                return true;
            }

            if (humanDies)
            {
                human.Kill(dinfo);
                return false;
            }

            ApplySpilloverDamage(human, dinfo);
            return false;
        }

        private static void ApplySpilloverDamage(Pawn human, DamageInfo? dinfo)
        {
            if (human == null || dinfo == null || WildShiftMod.Settings == null)
            {
                return;
            }

            float factor = WildShiftMod.Settings.spilloverDamageFactor;
            if (factor <= 0f)
            {
                return;
            }

            // TODO: Replace this coarse spillover with exact overkill-derived damage after verifying
            // RimWorld 1.6 DamageWorker/Pawn.Kill internals. Default setting is off.
            float amount = Mathf.Clamp(dinfo.Value.Amount * factor, 1f, 20f);
            human.TakeDamage(new DamageInfo(DamageDefOf.Blunt, amount));
        }
    }
}

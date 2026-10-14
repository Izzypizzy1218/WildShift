using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WildShift
{
    public static class RevengeCompanions
    {
        // Reserve the sold leader and at least one ordinary animal. Additional
        // people consume the same arrival budget using their actual animal form.
        public static float Prepare(float baseline, PawnKindDef leaderKind, Faction faction, List<Pawn> companions)
        {
            float available = baseline * SoldRevengeBalance.PointsMultiplier - 2f * leaderKind.combatPower;
            if (available <= 0f || float.IsNaN(available) || float.IsInfinity(available)) return 0f;
            float target = Math.Min(available, (baseline * SoldRevengeBalance.PointsMultiplier - leaderKind.combatPower)
                * SoldRevengeBalance.ShapeshifterShare);
            var kinds = new List<PawnKindDef>();
            var races = new HashSet<ThingDef>();
            foreach (PawnKindDef kind in AnimalPool.EligibleAnimals())
                if (!kind.forceDeathOnDowned && kind.combatPower > 0f && kind.combatPower <= available
                    && races.Add(kind.race)) kinds.Add(kind);
            // Prefer a different species when one fits. Each ally still has
            // one stable assigned form and its own independently generated body.
            if (kinds.Exists(k => k.race != leaderKind.race)) kinds.RemoveAll(k => k.race == leaderKind.race);
            float used = 0f;
            int added = 0;
            while (kinds.Count > 0)
            {
                var affordable = kinds.FindAll(k => k.combatPower <= target - used);
                // A small mixed force can exceed the 30% target for its first
                // ally, but never spend the leader/ordinary-animal reservation.
                if (affordable.Count == 0 && added == 0) affordable = kinds.FindAll(k => k.combatPower <= available);
                if (affordable.Count == 0) break;
                PawnKindDef assigned = affordable.RandomElement();
                Pawn ally = null;
                bool accepted = false;
                try
                {
                    ally = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, faction,
                        forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true));
                    if (ally == null || ally.Dead || ally.Destroyed || ally.Downed || !ally.RaceProps.Humanlike) continue;
                    TransformUtility.AddOrGetShapeshifter(ally, assigned);
                    var comp = TransformUtility.TryGetShapeshifterComp(ally);
                    PawnKindDef actual = comp?.assignedKind;
                    if (actual == null || actual.forceDeathOnDowned || actual.combatPower <= 0f
                        || float.IsNaN(actual.combatPower) || actual.combatPower > available - used) continue;
                    comp.enemyRaidParticipant = true;
                    comp.enemyShiftUsed = false;
                    companions.Add(ally);
                    used += actual.combatPower;
                    added++;
                    accepted = true;
                }
                finally
                {
                    if (!accepted)
                    {
                        kinds.Remove(assigned);
                        if (ally != null && !ally.Destroyed && !ally.SpawnedOrAnyParentSpawned && ally.holdingOwner == null)
                            ally.Destroy(DestroyMode.Vanish);
                    }
                }
            }
            return used;
        }
    }
}

using System;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace WildShift
{
    public static class EnemyShapeshifterUtility
    {
        public static bool IsHostile(Pawn pawn)
        {
            Faction player = Find.FactionManager != null ? Find.FactionManager.OfPlayer : null;
            return pawn != null && !pawn.Dead && !pawn.Destroyed
                && player != null
                && pawn.Faction != null && !pawn.Faction.IsPlayer
                && pawn.HostFaction == null && !pawn.IsPrisoner && !pawn.IsSlave
                && pawn.Faction.HostileTo(player);
        }

        public static bool CanShift(Pawn pawn)
        {
            if (!IsHostile(pawn) || !pawn.Spawned || pawn.Downed || pawn.InMentalState
                || !pawn.RaceProps.Humanlike || pawn.lord == null
                || !(pawn.lord.CurLordToil is LordToil_AssaultColony)) return false;
            // The sold original may retain tags from the quest that recruited
            // them. Preserve those tags without treating this personal return
            // as a new, randomly generated quest raider.
            if (pawn.questTags != null && pawn.questTags.Count > 0 && !SoldShapeshifterRevenge.IsRevengePawn(pawn)) return false;
            HediffComp_Shapeshifter comp = TransformUtility.TryGetShapeshifterComp(pawn);
            return comp != null && comp.enemyRaidParticipant && !comp.enemyShiftUsed
                && comp.assignedKind != null && !comp.assignedKind.forceDeathOnDowned;
        }

        public static bool IsCombatForm(Pawn pawn)
        {
            // Health checks run for ordinary wildlife too. Reject impossible
            // enemy forms before searching the hediff list on these hot paths.
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Destroyed
                || pawn.Faction == null || pawn.Faction.IsPlayer
                || pawn.RaceProps == null || !pawn.RaceProps.Animal) return false;
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(pawn);
            return comp != null && comp.enemyCombatForm && comp.HasStoredPawn && IsHostile(pawn);
        }

        public static bool IsReturnPending(Pawn pawn)
        {
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(pawn);
            return comp != null && comp.enemyReturnPending && comp.HasStoredPawn;
        }

        public static void OnTransformed(Pawn human, Pawn animal, HediffComp_Transformed form)
        {
            HediffComp_Shapeshifter comp = TransformUtility.TryGetShapeshifterComp(human);
            if (comp == null || !comp.enemyRaidParticipant || !IsHostile(human)) return;
            comp.enemyShiftUsed = true;
            form.enemyCombatForm = true;
            ReplaceLordPawn(human, animal);
        }

        public static void ReplaceLordPawn(Pawn source, Pawn destination)
        {
            // Replace in place: RemovePawn/AddPawn would inflate raid size, and
            // Notify_PawnLost can destroy a solo raider's Lord before the swap.
            Lord lord = source.lord;
            if (lord == null || destination.lord != null || !destination.Spawned
                || destination.Map != lord.Map || destination.Faction != lord.faction) return;
            int index = lord.ownedPawns.IndexOf(source);
            if (index < 0) return;
            lord.ownedPawns[index] = destination;
            destination.lord = lord;
            source.lord = null;
            if (destination.mindState != null && source.mindState != null)
            {
                destination.mindState.duty = source.mindState.duty;
                source.mindState.duty = null;
            }
            lord.Map.attackTargetsCache.UpdateTarget(destination);
            RestoreMissingDuty(destination);
        }

        public static void RestoreMissingDuty(Pawn pawn)
        {
            // MakeDowned -> ClearMind_NewTemp -> MindState.Reset clears duty.
            // Do not copy an old AssaultColony duty back over a retreat: ask the
            // current LordToil to assign its current orders after membership swaps.
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed
                || pawn.mindState == null || pawn.mindState.duty != null) return;
            Lord lord = pawn.lord;
            if (lord == null || lord.CurLordToil == null || !lord.CurLordToil.AssignsDuties
                || lord.Map != pawn.Map || lord.faction != pawn.Faction
                || !lord.ownedPawns.Contains(pawn)) return;
            try
            {
                lord.CurLordToil.UpdateAllDuties();
            }
            catch (Exception ex)
            {
                // A third-party toil exception must not abort an already
                // committed body transfer before the obsolete shell is removed.
                Log.Error("[WildShift] Could not restore raid duty after body transfer: " + ex);
            }
        }
    }
}

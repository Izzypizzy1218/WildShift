using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WildShift
{
    public static class FormTransferUtility
    {
        private static readonly HashSet<Pawn> returningForms = new HashSet<Pawn>();

        public static bool TryBeginReturn(Pawn animal) { return animal != null && returningForms.Add(animal); }
        public static void EndReturn(Pawn animal) { returningForms.Remove(animal); }
        public static bool IsReturning(Pawn animal) { return returningForms.Contains(animal); }

        public static bool TrySpawn(Pawn pawn, IntVec3 cell, Map map)
        {
            if (pawn == null || pawn.Destroyed || map == null)
            {
                return false;
            }
            try
            {
                if (!pawn.Spawned)
                {
                    GenSpawn.Spawn(pawn, cell, map);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Pawn placement failed; preserving the original body: " + ex);
            }
            // Spawn callbacks can throw after registration. Do not store or
            // duplicate a pawn that is already present on the destination map.
            return pawn.Spawned && pawn.Map == map;
        }

        public static bool TryStore(Pawn human, ThingOwner storage)
        {
            if (human == null || human.Destroyed || storage == null)
            {
                return false;
            }
            if (storage.Contains(human))
            {
                return true;
            }
            if (storage.Count > 0 || !storage.CanAcceptAnyOf(human, false))
            {
                return false;
            }

            Map oldMap = human.Map;
            IntVec3 oldCell = human.Position;
            ThingOwner oldOwner = human.holdingOwner;
            bool wasDrafted = human.Drafted;
            try
            {
                if (human.Spawned)
                {
                    human.DeSpawn(DestroyMode.Vanish);
                }
                if (storage.TryAddOrTransfer(human, false))
                {
                    RemoveStoredWorldPawn(human);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Human storage failed; rolling back: " + ex);
            }

            if (storage.Contains(human))
            {
                RemoveStoredWorldPawn(human);
                return true;
            }
            if (oldMap != null && TrySpawn(human, oldCell, oldMap))
            {
                if (wasDrafted && human.drafter != null) human.drafter.Drafted = true;
                return false;
            }
            if (oldOwner != null && TryRestoreOwner(human, oldOwner))
            {
                return false;
            }
            PreserveDetachedPawn(human);
            return false;
        }

        private static void RemoveStoredWorldPawn(Pawn human)
        {
            // Emergency recovery can temporarily retain this pawn in the
            // world registry. A later successful retry must not own it twice.
            if (Find.WorldPawns != null && WorldPawnsUtility.IsWorldPawn(human))
            {
                Find.WorldPawns.RemovePawn(human);
            }
        }

        public static bool TryRestoreOwner(Pawn pawn, ThingOwner owner)
        {
            try
            {
                return owner.Contains(pawn) || owner.TryAddOrTransfer(pawn, false);
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Container rollback failed: " + ex);
                return owner.Contains(pawn);
            }
        }

        public static bool PreserveDetachedPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return false;
            if (pawn.Spawned || pawn.holdingOwner != null) return true;
            if (Find.WorldPawns == null) return false;
            try
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                Log.Error("[WildShift] Emergency recovery: " + pawn.LabelShortCap
                    + " was retained as a world pawn because placement/rollback failed. The pawn was not deleted.");
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] World-pawn recovery failed: " + ex);
            }
            return WorldPawnsUtility.IsWorldPawn(pawn);
        }

        public static Pawn TryReleaseToWorld(HediffComp_Transformed comp, bool emergency = false)
        {
            if (comp == null || !comp.HasStoredPawn || Find.WorldPawns == null) return null;
            Pawn human = comp.ReleaseStoredPawn();
            try
            {
                Find.WorldPawns.PassToWorld(human, PawnDiscardDecideMode.KeepForever);
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] World-pawn transfer failed: " + ex);
            }
            if (WorldPawnsUtility.IsWorldPawn(human))
            {
                if (emergency) Log.Error("[WildShift] Emergency recovery: retained " + human.LabelShortCap
                    + " as a world pawn after failed lethal-damage reversion. The original body was not deleted.");
                return human;
            }
            if (!comp.TryStore(human)) PreserveDetachedPawn(human);
            return null;
        }

        public static void DestroyEmptyForm(Pawn animal)
        {
            if (animal == null || animal.Destroyed) return;
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(animal);
            if (comp != null && comp.HasStoredPawn)
            {
                Log.Error("[WildShift] Refusing to discard an animal that still holds its human body.");
                return;
            }
            try
            {
                animal.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                // The human has already been committed to a safe destination.
                // Never roll it back because a shell-cleanup callback failed.
                Log.Error("[WildShift] Empty animal cleanup failed after body transfer: " + ex);
            }
        }
    }
}

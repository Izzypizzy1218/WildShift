using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI.Group;

namespace WildShift
{
    public class MapComponent_EnemyShapeshifters : MapComponent
    {
        // Weak map keys avoid retaining unloaded maps or previous games. The
        // component registers once, rather than scanning Map.components per tick.
        private static readonly ConditionalWeakTable<Map, MapComponent_EnemyShapeshifters> byMap
            = new ConditionalWeakTable<Map, MapComponent_EnemyShapeshifters>();
        private readonly HashSet<Pawn> shifts = new HashSet<Pawn>();
        private readonly HashSet<Pawn> returns = new HashSet<Pawn>();

        public MapComponent_EnemyShapeshifters(Map map) : base(map)
        {
            if (map != null) { byMap.Remove(map); byMap.Add(map, this); }
        }

        public static MapComponent_EnemyShapeshifters For(Map map)
        {
            if (map == null) return null;
            MapComponent_EnemyShapeshifters component;
            return byMap.TryGetValue(map, out component) ? component : map.GetComponent<MapComponent_EnemyShapeshifters>();
        }

        public void QueueShift(Pawn pawn) { if (EnemyShapeshifterUtility.CanShift(pawn)) shifts.Add(pawn); }
        public void QueueReturn(Pawn pawn) { returns.Add(pawn); }

        public override void FinalizeInit()
        {
            // Reconstruct transient queues once after loading; no pawn scans
            // during ticks. The one-use and pending flags live on saved hediffs.
            foreach (Pawn pawn in new List<Pawn>(map.mapPawns.AllPawnsSpawned))
            {
                // Repair saved v1.2.0 raid members with the same missing-duty
                // bug, without rerolling forms or resetting the spent shift flag.
                HediffComp_Shapeshifter human = TransformUtility.TryGetShapeshifterComp(pawn);
                if (EnemyShapeshifterUtility.IsCombatForm(pawn)
                    || (human != null && human.enemyRaidParticipant && EnemyShapeshifterUtility.IsHostile(pawn)))
                    EnemyShapeshifterUtility.RestoreMissingDuty(pawn);
                QueueShift(pawn);
                if (EnemyShapeshifterUtility.IsReturnPending(pawn)) QueueReturn(pawn);
            }
        }

        public void ProcessPending()
        {
            // Called before the native Lord tick, outside health/job iterations.
            if (returns.Count > 0)
            {
                Pawn[] batch = new Pawn[returns.Count];
                returns.CopyTo(batch);
                returns.Clear();
                foreach (Pawn pawn in batch)
                {
                    HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(pawn);
                    if (comp == null || !comp.enemyReturnPending) continue;
                    bool returned = false;
                    try
                    {
                        if (EnemyShapeshifterUtility.IsCombatForm(pawn) && pawn.Map == map && pawn.Downed)
                            returned = TransformUtility.RevertToHuman(pawn, false) != null;
                    }
                    catch (Exception ex) { Log.Error("[WildShift] Enemy return failed: " + ex); }
                    finally
                    {
                        comp.enemyReturnPending = false;
                        // Failed placement must not leave a downed raider
                        // permanently immune to the normal raid-loss logic.
                        if (!returned && pawn.Spawned && pawn.Downed && pawn.lord != null)
                        {
                            try { pawn.lord.Notify_PawnLost(pawn, PawnLostCondition.Incapped); }
                            catch (Exception ex) { Log.ErrorOnce("[WildShift] Raid-loss callback failed after return rollback: " + ex, 81729174); }
                        }
                    }
                }
            }

            if (shifts.Count == 0) return;
            Pawn[] candidates = new Pawn[shifts.Count];
            shifts.CopyTo(candidates);
            shifts.Clear();
            foreach (Pawn pawn in candidates)
            {
                if (pawn.Map != map || !EnemyShapeshifterUtility.CanShift(pawn)) continue;
                HediffComp_Shapeshifter comp = TransformUtility.TryGetShapeshifterComp(pawn);
                // Spend before generation: even a third-party generation error
                // cannot cause a retry/error storm on each duty update.
                comp.enemyShiftUsed = true;
                try { TransformUtility.TransformToAnimal(pawn, comp.assignedKind); }
                catch (Exception ex) { Log.Error("[WildShift] Enemy shift failed: " + ex); }
            }
        }
    }
}

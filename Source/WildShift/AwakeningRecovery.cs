using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace WildShift
{
    // Failed candidate cleanup must survive saving even when the candidate was
    // never spawned. Map/world/owner-held pawns already have a serialization
    // owner; only otherwise detached candidates are saved deeply here.
    public class AwakeningRollback : IExposable
    {
        public int animalId;
        public Pawn human;
        private bool saveHumanDeep;

        public void ExposeData()
        {
            Scribe_Values.Look(ref animalId, "animalId");
            if (Scribe.mode == LoadSaveMode.Saving)
                saveHumanDeep = human != null && !human.Destroyed && !human.Spawned
                    && human.holdingOwner == null
                    && (Find.WorldPawns == null || !WorldPawnsUtility.IsWorldPawn(human));
            Scribe_Values.Look(ref saveHumanDeep, "saveHumanDeep", false);
            if (saveHumanDeep) Scribe_Deep.Look(ref human, "human");
            else Scribe_References.Look(ref human, "human");
        }
    }

    public class GameComponent_AwakeningRecovery : GameComponent
    {
        // Keep consumed IDs even after cleanup: resurrection or a stale action
        // on an obsolete body must never roll a second human identity.
        private HashSet<int> completed = new HashSet<int>();
        private List<AwakeningRollback> rollbacks = new List<AwakeningRollback>();

        public GameComponent_AwakeningRecovery(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
                rollbacks.RemoveAll(entry => entry.human != null && entry.human.Destroyed);
            Scribe_Collections.Look(ref completed, "wildShiftCompletedAwakenings", LookMode.Value);
            Scribe_Collections.Look(ref rollbacks, "wildShiftAwakeningRollbacks", LookMode.Deep);
            if (completed == null) completed = new HashSet<int>();
            if (rollbacks == null) rollbacks = new List<AwakeningRollback>();
        }

        public bool IsCompleted(Pawn animal) { return completed.Contains(animal.thingIDNumber); }
        public void Complete(Pawn animal) { completed.Add(animal.thingIDNumber); }

        public bool TryClearRollback(Pawn animal)
        {
            for (int i = rollbacks.Count - 1; i >= 0; i--)
            {
                AwakeningRollback entry = rollbacks[i];
                if (entry.animalId != animal.thingIDNumber) continue;
                // An unresolved save reference is not proof of deletion. Keep
                // the block rather than generating a potentially duplicate pawn.
                if (entry.human == null || !FormTransferUtility.DestroyEmptyForm(entry.human)) return false;
                rollbacks.RemoveAt(i);
            }
            return true;
        }

        public void RollBack(Pawn animal, Pawn human)
        {
            if (!FormTransferUtility.DestroyEmptyForm(human))
                rollbacks.Add(new AwakeningRollback { animalId = animal.thingIDNumber, human = human });
        }
    }
}

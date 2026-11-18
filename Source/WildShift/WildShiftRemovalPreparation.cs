using System;
using System.Collections.Generic;
using System.IO;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WildShift
{
    internal static class WildShiftRemovalPreparation
    {
        internal static void Confirm()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "WildShift_RemovalPrepareConfirm".Translate(),
                () => LongEventHandler.QueueLongEvent(PrepareAndSave, "SavingLongEvent", false, null)));
        }

        private static void PrepareAndSave()
        {
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null) return;
            Find.TickManager.Pause();

            try
            {
                if (!TryRestoreHumans(out int restored, out Pawn blocked))
                {
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "WildShift_RemovalPrepareBlocked".Translate(restored, blocked.LabelShortCap)));
                    return;
                }

                // Keep the previous save and write a separate recovery slot.
                string slot = "WildShift-Recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string name = slot;
                int suffix = 2;
                while (File.Exists(GenFilePaths.FilePathForSavedGame(name)))
                    name = slot + "-" + suffix++;

                GameDataSaveLoader.SaveGame(name);
                if (!File.Exists(GenFilePaths.FilePathForSavedGame(name)))
                {
                    Find.WindowStack.Add(new Dialog_MessageBox("WildShift_RemovalPrepareSaveFailed".Translate()));
                    return;
                }

                Find.WindowStack.Add(new Dialog_MessageBox(
                    "WildShift_RemovalPrepareDone".Translate(restored, name)));
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Removal preparation failed: " + ex);
                Find.WindowStack.Add(new Dialog_MessageBox("WildShift_RemovalPrepareFailed".Translate()));
            }
        }

        internal static bool TryRestoreHumans(out int restored, out Pawn blocked)
        {
            restored = 0;
            // Snapshot first. A successful return changes both map and holder lists.
            foreach (Pawn form in FindStoredHumanForms())
            {
                var storage = TransformUtility.TryGetTransformedComp(form);
                Pawn human = storage?.StoredPawn;
                if (human == null) continue;

                if (form.Destroyed || form.Dead
                    || !ReferenceEquals(TransformUtility.RevertToHuman(form, false), human)
                    || human.Destroyed || (!human.Spawned && human.holdingOwner == null && !human.IsWorldPawn()))
                {
                    blocked = form;
                    return false;
                }
                restored++;
            }

            // A callback may have introduced another stored human while returning.
            blocked = null;
            foreach (Pawn form in FindStoredHumanForms())
            {
                blocked = form;
                return false;
            }

            return Current.Game.GetComponent<GameComponent_AwakeningRecovery>()?.PreserveDetachedForRemoval(out blocked) != false;
        }

        private static HashSet<Pawn> FindStoredHumanForms()
        {
            var pawns = new HashSet<Pawn>(PawnsFinder.All_AliveOrDead);
            var heldPawns = new List<Pawn>();
            // Include corpses the normal map pawn census leaves out.
            foreach (Map map in Find.Maps)
            {
                ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Pawn),
                    heldPawns, alsoGetSpawnedThings: false);
                pawns.UnionWith(heldPawns);
            }

            var heldThings = new List<Thing>();
            foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
            {
                if (!(worldObject is IThingHolder holder)) continue;
                ThingOwnerUtility.GetAllThingsRecursively(holder, heldThings);
                foreach (Thing thing in heldThings)
                    if (thing is Pawn pawn) pawns.Add(pawn);
            }

            GameComponent_SoldShapeshifterRevenge.Current?.AddRetainedPawns(pawns);
            pawns.RemoveWhere(pawn => TransformUtility.TryGetTransformedComp(pawn)?.HasStoredPawn != true);
            return pawns;
        }
    }
}

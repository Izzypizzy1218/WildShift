using System;
using Verse;

namespace WildShift
{
    public static class FormPossessions
    {
        public static bool TryUnload(Pawn animal, Pawn human)
        {
            if (!AwakeningPossessions.HasPossessions(animal)) return true;
            if (animal.Spawned && AwakeningPossessions.TryDropAll(animal)) return true;
            // Use the preserved human's inventory for anything the ground
            // could not accept, and for caravans/world containers with no map.
            // Already dropped items stay on the ground; never re-equip animal
            // clothing on the human or create replacement copies.
            if (human?.inventory == null) return false;
            try
            {
                var destination = human.inventory.innerContainer;
                if (!TryTransfer(animal.apparel?.GetDirectlyHeldThings(), destination)
                    || !TryTransfer(animal.equipment?.GetDirectlyHeldThings(), destination)
                    || !TryTransfer(animal.inventory?.GetDirectlyHeldThings(), destination)
                    || !TryTransfer(animal.carryTracker?.GetDirectlyHeldThings(), destination)) return false;
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Could not transfer animal-form possessions; preserving both bodies and remaining items: " + ex);
                return false;
            }
            // Partial successful transfers remain with the stored human when a
            // later transfer or body placement fails. Retrying cannot duplicate.
            return !AwakeningPossessions.HasPossessions(animal);
        }

        private static bool TryTransfer(ThingOwner source, ThingOwner destination)
        {
            if (source == null || source == destination) return true;
            for (int i = source.Count - 1; i >= 0; i--)
            {
                Thing item = source[i];
                try
                {
                    if (!source.TryTransferToContainer(item, destination, false)) return false;
                }
                catch
                {
                    // Native transfer callbacks can throw after detaching an
                    // item. Restore that exact object, with stacking disabled.
                    if (!item.Destroyed && !item.Spawned && item.holdingOwner == null)
                    {
                        try { source.TryAdd(item, false); }
                        catch { if (!source.Contains(item)) throw; }
                    }
                    throw;
                }
            }
            return source.Count == 0;
        }
    }
}

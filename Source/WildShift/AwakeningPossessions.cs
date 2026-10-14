using System;
using Verse;

namespace WildShift
{
    public static class AwakeningPossessions
    {
        public static bool HasPossessions(Pawn animal)
        {
            return (animal.inventory != null && animal.inventory.innerContainer.Count > 0)
                || (animal.equipment != null && animal.equipment.AllEquipmentListForReading.Count > 0)
                || (animal.apparel != null && animal.apparel.WornApparelCount > 0)
                || (animal.carryTracker != null && animal.carryTracker.CarriedThing != null);
        }

        public static bool TryDropAll(Pawn animal)
        {
            if (animal == null || animal.Destroyed || !animal.Spawned) return false;
            try
            {
                animal.inventory?.DropAllNearPawn(animal.Position, forbid: false, unforbid: true);
                animal.equipment?.DropAllEquipment(animal.Position, forbid: false);
                animal.apparel?.DropAll(animal.Position, forbid: false, dropLocked: true);
                if (animal.carryTracker != null && animal.carryTracker.CarriedThing != null)
                {
                    Thing dropped;
                    animal.carryTracker.TryDropCarriedThing(animal.Position, ThingPlaceMode.Near, out dropped);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[WildShift] Could not unload animal; preserving its body and remaining items: " + ex);
                return false;
            }
            // Native drops may fail without throwing. Never destroy a body that
            // still owns items; successful partial drops remain safe on the map.
            return !HasPossessions(animal);
        }
    }
}

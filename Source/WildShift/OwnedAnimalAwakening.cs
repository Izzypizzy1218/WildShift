using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    public static class OwnedAnimalAwakening
    {
        public static bool IsOwnedLatentAnimal(Pawn animal)
        {
            return animal != null && !animal.Destroyed && animal.Faction == Faction.OfPlayer
                && Patch_Taming.IsLatentWildShapeshifter(animal);
        }

        private static string DisabledReason(Pawn animal)
        {
            if (!IsOwnedLatentAnimal(animal)) return "WildShift_ShiftDisabledBadPawn";
            if (!animal.Spawned) return "WildShift_AwakenNeedsMap";
            if (animal.Downed || animal.InMentalState) return "WildShift_ShiftDisabledBadPawn";
            if (Current.Game?.GetComponent<GameComponent_LatentAnimalHarm>()?.WasMedicalAwakeningBlocked(animal) ?? false)
                return "WildShift_AwakenPlayerHarm";
            return null;
        }

        public static Command_Action GetCommand(Pawn animal)
        {
            if (!IsOwnedLatentAnimal(animal)) return null;
            bool completed = Current.Game?.GetComponent<GameComponent_AwakeningRecovery>()?.IsCompleted(animal) ?? false;
            var command = new Command_Action
            {
                defaultLabel = (completed ? "WildShift_CommandFinishAwakeningLabel" : "WildShift_CommandAwakenLabel").Translate(),
                defaultDesc = (completed ? "WildShift_CommandFinishAwakeningDesc" : "WildShift_CommandAwakenDesc").Translate(),
                icon = WildShiftTextures.Transform,
                action = delegate
                {
                    LongEventHandler.ExecuteWhenFinished(delegate { TryAwaken(animal); });
                }
            };
            string reason = DisabledReason(animal);
            if (reason != null) command.Disable(reason.Translate());
            return command;
        }

        public static void TryAwaken(Pawn animal)
        {
            // Revalidate stale UI actions, including ownership and medical origin.
            if (DisabledReason(animal) != null) return;
            Patch_Taming.RevealTamedShapeshifter(animal);
        }
    }

    [HarmonyPatch(typeof(Pawn), "PreTraded")]
    public static class Patch_PurchasedAnimalAwakening
    {
        public static void Postfix(Pawn __instance, TradeAction action)
        {
            // Native purchase has set the faction, but delivery may still be in
            // progress. Only clear the medical-origin block; never replace here.
            if (action == TradeAction.PlayerBuys && OwnedAnimalAwakening.IsOwnedLatentAnimal(__instance))
                Current.Game?.GetComponent<GameComponent_LatentAnimalHarm>()?.RecordPurchase(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Patch_OwnedAnimalAwakeningGizmos
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // Most selections are humans or ordinary animals: leave their
            // original iterator intact without allocating another wrapper.
            if (OwnedAnimalAwakening.IsOwnedLatentAnimal(__instance))
                __result = Append(__result, __instance);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> original, Pawn animal)
        {
            if (original != null)
                foreach (Gizmo gizmo in original) yield return gizmo;
            Command_Action command = OwnedAnimalAwakening.GetCommand(animal);
            if (command != null) yield return command;
        }
    }
}

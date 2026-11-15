using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WildShift
{
    public static class TransformedAnimalSaleProtection
    {
        // Inspect the native transfer selection, without changing prices,
        // counts or ownership. A previously open trade window can still hold
        // an offer created before the player disabled shapeshifter sales.
        public static Pawn FirstBlockedSale(TradeDeal deal)
        {
            if (WildShiftMod.Settings == null || WildShiftMod.Settings.allowTransformedSale
                || deal?.AllTradeables == null) return null;

            List<Tradeable> rows = deal.AllTradeables;
            for (int i = 0; i < rows.Count; i++)
            {
                Tradeable row = rows[i];
                if (row == null || row.ActionToDo != TradeAction.PlayerSells
                    || row.thingsColony == null) continue;
                int remaining = row.CountToTransferToDestination;
                if (remaining <= 0) continue;

                // Tradeable_Pawn.ResolveTrade takes the first N Pawn entries.
                // Other native tradeables use TransferNoSplit, which walks
                // forward and consumes stack counts. Match both operations so
                // an unsold protected member cannot veto an ordinary sale.
                bool pawnRow = row is Tradeable_Pawn;
                for (int j = 0; j < row.thingsColony.Count && remaining > 0; j++)
                {
                    Thing thing = row.thingsColony[j];
                    int selected = pawnRow ? 1 : Math.Min(remaining, thing?.stackCount ?? 0);
                    if (selected <= 0) continue;
                    remaining -= selected;
                    if (TransformedAnimalProtection.SaleBlocked(thing)) return thing as Pawn;
                }
            }
            return null;
        }

        public static void NotifyBlocked(Thing thing)
        {
            Messages.Message("WildShift_SaleBlocked".Translate(), thing,
                MessageTypeDefOf.RejectInput, historical: false);
        }
    }

    [HarmonyPatch(typeof(TradeUtility), "PlayerSellableNow", new[] { typeof(Thing), typeof(ITrader) })]
    public static class Patch_TransformedAnimalSaleOffer
    {
        public static void Postfix(Thing __0, ref bool __result)
        {
            if (__result && TransformedAnimalProtection.SaleBlocked(__0)) __result = false;
        }
    }

    [HarmonyPatch(typeof(TradeDeal), "TryExecute")]
    public static class Patch_TransformedAnimalSaleTransaction
    {
        public static bool Prefix(TradeDeal __instance, ref bool actuallyTraded, ref bool __result)
        {
            Pawn blocked = TransformedAnimalSaleProtection.FirstBlockedSale(__instance);
            if (blocked == null) return true;
            // This runs before the native normal/gift branches change silver,
            // goodwill or any offered item. Cancel the whole stale transaction.
            actuallyTraded = false;
            __result = false;
            TransformedAnimalSaleProtection.NotifyBlocked(blocked);
            return false;
        }
    }

    [HarmonyPatch]
    public static class Patch_TransformedAnimalSaleHandoff
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] traders = { typeof(Pawn_TraderTracker), typeof(TradeShip),
                typeof(Settlement_TraderTracker), typeof(Caravan_TraderTracker) };
            Type[] parameters = { typeof(Thing), typeof(int), typeof(Pawn) };
            foreach (Type trader in traders)
            {
                MethodInfo method = AccessTools.Method(trader, "GiveSoldThingToTrader", parameters);
                if (method == null) throw new MissingMethodException(trader.FullName, "GiveSoldThingToTrader");
                yield return method;
            }
        }

        public static bool Prefix(Thing __0, int __1)
        {
            if (__1 <= 0 || !TransformedAnimalProtection.SaleBlocked(__0)) return true;
            // Direct native handoffs also stop before SplitOff/PreTraded. Do
            // not cancel Pawn.PreTraded: it is too late for atomic trade safety.
            TransformedAnimalSaleProtection.NotifyBlocked(__0);
            return false;
        }
    }
}

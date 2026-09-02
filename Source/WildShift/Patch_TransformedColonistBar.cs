using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class Patch_TransformedColonistBar
    {
        private static readonly List<Map> Maps = new List<Map>();
        private static readonly List<Caravan> Caravans = new List<Caravan>();

        public static void Postfix(
            ColonistBar __instance,
            List<ColonistBar.Entry> ___cachedEntries,
            List<Vector2> ___cachedDrawLocs,
            List<int> ___cachedReorderableGroups,
            ColonistBarDrawLocsFinder ___drawLocsFinder,
            ref float ___cachedScale)
        {
            if (__instance == null
                || ___cachedEntries == null
                || !Find.PlaySettings.showColonistBar)
            {
                return;
            }

            bool changed = false;
            Maps.Clear();
            Maps.AddRange(Find.Maps);
            Maps.SortBy((Map map) => !map.IsPlayerHome, (Map map) => map.uniqueID);

            int group = 0;
            for (int i = 0; i < Maps.Count; i++)
            {
                IReadOnlyList<Pawn> pawns = Maps[i].mapPawns.AllPawnsSpawned;
                for (int j = 0; j < pawns.Count; j++)
                {
                    changed |= AddTransformedPawn(___cachedEntries, pawns[j], Maps[i], group);
                }

                group++;
            }

            Caravans.Clear();
            if (Find.WorldObjects != null)
            {
                Caravans.AddRange(Find.WorldObjects.Caravans);
                Caravans.SortBy((Caravan caravan) => caravan.ID);
            }

            for (int i = 0; i < Caravans.Count; i++)
            {
                Caravan caravan = Caravans[i];
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }

                List<Pawn> pawns = caravan.PawnsListForReading;
                for (int j = 0; j < pawns.Count; j++)
                {
                    changed |= AddTransformedPawn(___cachedEntries, pawns[j], null, group);
                }

                group++;
            }

            Maps.Clear();
            Caravans.Clear();

            if (!changed)
            {
                return;
            }

            ___cachedEntries.Sort(CompareEntries);
            ___cachedReorderableGroups.Clear();
            for (int i = 0; i < ___cachedEntries.Count; i++)
            {
                ___cachedReorderableGroups.Add(-1);
            }

            __instance.drawer.Notify_RecachedEntries();
            ___drawLocsFinder.CalculateDrawLocs(
                ___cachedDrawLocs,
                out ___cachedScale,
                Math.Max(group, 1));
        }

        private static bool AddTransformedPawn(
            List<ColonistBar.Entry> entries,
            Pawn pawn,
            Map map,
            int group)
        {
            if (pawn == null
                || pawn.Dead
                || pawn.Faction != Faction.OfPlayer
                || !TransformUtility.IsTransformedAnimal(pawn))
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].pawn == pawn)
                {
                    return false;
                }
            }

            TransformUtility.EnsureTransformedAnimalControl(pawn, true);
            if (pawn.playerSettings.displayOrder == Pawn_PlayerSettings.UnsetDisplayOrder)
            {
                HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(pawn);
                Pawn human = comp != null ? comp.StoredPawn : null;
                if (human != null && human.playerSettings != null)
                {
                    pawn.playerSettings.displayOrder = human.playerSettings.displayOrder;
                }
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].group == group && entries[i].pawn == null)
                {
                    entries.RemoveAt(i);
                }
            }

            entries.Add(new ColonistBar.Entry(pawn, map, group));
            return true;
        }

        private static int CompareEntries(ColonistBar.Entry left, ColonistBar.Entry right)
        {
            int groupComparison = left.group.CompareTo(right.group);
            if (groupComparison != 0)
            {
                return groupComparison;
            }

            if (left.pawn == null)
            {
                return right.pawn == null ? 0 : 1;
            }

            if (right.pawn == null)
            {
                return -1;
            }

            int leftOrder = left.pawn.playerSettings != null
                ? left.pawn.playerSettings.displayOrder
                : Pawn_PlayerSettings.UnsetDisplayOrder;
            int rightOrder = right.pawn.playerSettings != null
                ? right.pawn.playerSettings.displayOrder
                : Pawn_PlayerSettings.UnsetDisplayOrder;
            int orderComparison = leftOrder.CompareTo(rightOrder);
            return orderComparison != 0
                ? orderComparison
                : left.pawn.thingIDNumber.CompareTo(right.pawn.thingIDNumber);
        }
    }
}

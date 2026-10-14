using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    public static class AwakeningRaceSelection
    {
        private static Func<XenotypeDef, ThingDef, bool> harAllowsXenotype;
        private static bool checkedHar;

        public static PawnGenerationRequest CreateRequest(Gender? gender)
        {
            Faction player = Faction.OfPlayer;
            PawnKindDef playerKind = player?.def?.basicMemberKind;
            if (!SafeKind(playerKind)) playerKind = PawnKindDefOf.Colonist;
            // This branch deliberately retains the faction's own gene rules.
            if (Rand.Chance(0.5f)) return Request(playerKind, player, gender, null);

            var kinds = new Dictionary<ThingDef, PawnKindDef>();
            foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (!SafeKind(kind)) continue;
                PawnKindDef current;
                if (!kinds.TryGetValue(kind.race, out current) || Prefer(kind, current)) kinds[kind.race] = kind;
            }
            // Never select refugee/slave jobs for vanilla humans: HAR has
            // independent replacement rules for those exact PawnKindDefs.
            kinds[PawnKindDefOf.Colonist.race] = PawnKindDefOf.Colonist;
            var alternatives = new List<Choice>();
            ThingDef humanRace = PawnKindDefOf.Colonist.race;
            XenotypeDef playerXenotype = null;
            if (ModsConfig.BiotechActive && playerKind.race == humanRace)
                playerXenotype = DefaultXenotype(playerKind, player);

            foreach (var pair in kinds)
            {
                if (pair.Key == humanRace && ModsConfig.BiotechActive)
                {
                    foreach (XenotypeDef xeno in DefDatabase<XenotypeDef>.AllDefsListForReading)
                        if (xeno != playerXenotype && Allows(xeno, humanRace))
                            alternatives.Add(new Choice { kind = PawnKindDefOf.Colonist, xenotype = xeno });
                }
                else if (pair.Key != playerKind.race)
                    alternatives.Add(new Choice { kind = pair.Value });
            }
            if (alternatives.Count == 0) return Request(playerKind, player, gender, null);
            // Stable ordering and one slot per body race / human xenotype:
            // installing many soldier/trader jobs cannot increase race weight.
            alternatives.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
            Choice chosen = alternatives[Rand.Range(0, alternatives.Count)];
            XenotypeDef xenotype = chosen.xenotype;
            if (ModsConfig.BiotechActive && xenotype == null)
                xenotype = DefaultXenotype(chosen.kind, null);
            // No player faction during generation: neither HAR's Colonist
            // rewrite nor faction/ideology xenotypes can undo the selected race.
            return Request(chosen.kind, null, gender, xenotype);
        }

        private class Choice
        {
            public PawnKindDef kind;
            public XenotypeDef xenotype;
            public string Key { get { return kind.race.defName + "/" + (xenotype?.defName ?? ""); } }
        }

        private static PawnGenerationRequest Request(PawnKindDef kind, Faction faction, Gender? gender, XenotypeDef xeno)
        {
            return new PawnGenerationRequest(kind, faction, forceGenerateNewPawn: true,
                fixedGender: gender, forcedXenotype: xeno);
        }

        private static bool SafeKind(PawnKindDef kind)
        {
            return kind?.race?.race != null && kind.race.race.Humanlike
                && !kind.race.race.IsMechanoid && !kind.race.race.IsAnomalyEntity && !kind.race.race.IsDrone
                && kind.mutant == null && !kind.isBoss && !kind.factionLeader;
        }

        private static bool Prefer(PawnKindDef candidate, PawnKindDef current)
        {
            int a = (candidate.defaultFactionDef?.isPlayer == true ? 2 : 0) + (!candidate.isFighter ? 1 : 0);
            int b = (current.defaultFactionDef?.isPlayer == true ? 2 : 0) + (!current.isFighter ? 1 : 0);
            if (a != b) return a > b;
            if (candidate.combatPower != current.combatPower) return candidate.combatPower < current.combatPower;
            return StringComparer.Ordinal.Compare(candidate.defName, current.defName) < 0;
        }

        private static XenotypeDef DefaultXenotype(PawnKindDef kind, Faction faction)
        {
            XenotypeDef best = null;
            float weight = -1f;
            // This native method returns a shared dictionary: consume it here
            // without retaining or modifying it. Respect HAR's race restrictions.
            foreach (var pair in PawnGenerator.XenotypesAvailableFor(kind, null, faction))
                if (pair.Value > 0f && Allows(pair.Key, kind.race)
                    && (pair.Value > weight || (pair.Value == weight && StringComparer.Ordinal.Compare(pair.Key.defName, best?.defName) < 0)))
                { best = pair.Key; weight = pair.Value; }
            if (best != null) return best;
            return Allows(XenotypeDefOf.Baseliner, kind.race) ? XenotypeDefOf.Baseliner : null;
        }

        private static bool Allows(XenotypeDef xeno, ThingDef race)
        {
            if (!checkedHar)
            {
                Type type = AccessTools.TypeByName("AlienRace.RaceRestrictionSettings");
                if (type != null)
                {
                    var method = AccessTools.Method(type, "CanUseXenotype", new[] { typeof(XenotypeDef), typeof(ThingDef) });
                    if (method == null) throw new InvalidOperationException("[WildShift] HAR xenotype restriction API unavailable; preserving awakening animal.");
                    harAllowsXenotype = (Func<XenotypeDef, ThingDef, bool>)Delegate.CreateDelegate(typeof(Func<XenotypeDef, ThingDef, bool>), method);
                }
                checkedHar = true;
            }
            return xeno != null && (harAllowsXenotype == null || harAllowsXenotype(xeno, race));
        }
    }
}

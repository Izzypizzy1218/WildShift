using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Real native command aura and recipients. The observer counts
    // identity queries without supplying/replacing a gameplay result.
    public static class SharedAuraPerformanceEngineChecks
    {
        private static Pawn watchedForm, watchedOrdinary;
        private static int farFormQueries, farOrdinaryQueries;
        private static HediffComp_GiveHediffsInRange skippedAura;
        private static HediffComp_Link skippedLink;

        public static bool SkipAura(HediffComp_GiveHediffsInRange __instance)
        {
            return !ReferenceEquals(__instance, skippedAura);
        }

        public static bool SkipLink(HediffComp_Link __instance)
        {
            return !ReferenceEquals(__instance, skippedLink);
        }

        public static void ObserveIdentity(Pawn __0)
        {
            bool form = ReferenceEquals(__0, watchedForm), ordinary = ReferenceEquals(__0, watchedOrdinary);
            if (!form && !ordinary) return;
            if (form) farFormQueries++;
            if (ordinary) farOrdinaryQueries++;
        }
        public static void Run(Map map, Action<bool, string> check)
        {
            var humans = new List<Pawn>();
            var forms = new List<Pawn>();
            var observer = new Harmony("wildshift.tests.aura-identity-observer");
            MethodInfo identity = AccessTools.Method(typeof(SharedFormIdentity), nameof(SharedFormIdentity.HumanFor));
            MethodInfo observe = AccessTools.Method(typeof(SharedAuraPerformanceEngineChecks), nameof(ObserveIdentity));
            try
            {
                IntVec3 origin = map.AllCells.First(c => Valid(map, c)
                    && Valid(map, c + new IntVec3(4, 0, 0)) && Valid(map, c + new IntVec3(12, 0, 0))
                    && Valid(map, c + new IntVec3(0, 0, 2)) && Valid(map, c + new IntVec3(12, 0, 2))
                    && !map.mapPawns.AllPawnsSpawned.Any(p => p.Position.DistanceTo(c) <= 6f));
                Pawn source = Human(map, origin, "AuraSource", humans);
                Pawn near = Human(map, origin + new IntVec3(4, 0, 0), "AuraNearForm", humans);
                Pawn far = Human(map, origin + new IntVec3(12, 0, 0), "AuraFarForm", humans);
                Pawn nearOrdinary = Human(map, origin + new IntVec3(0, 0, 2), "AuraNearOrdinary", humans);
                Pawn farOrdinary = Human(map, origin + new IntVec3(12, 0, 2), "AuraFarOrdinary", humans);
                Pawn nearBody = Transform(near, forms, check), farBody = Transform(far, forms, check);
                HediffDef auraDef = DefDatabase<HediffDef>.GetNamed("CombatCommand");
                HediffDef buffDef = DefDatabase<HediffDef>.GetNamed("CombatCommandBuff");
                Hediff aura = source.health.AddHediff(auraDef, source.health.hediffSet.GetBrain());
                HediffComp_GiveHediffsInRange comp = aura.TryGetComp<HediffComp_GiveHediffsInRange>();
                Require(comp != null && SharedFormHealth.IsSupported(aura),
                    "aura-performance/actual native command source is supported by production forwarding", check);
                HediffCompProperties_GiveHediffsInRange native = comp.Props;
                // Instance-local native props; shared game definitions are not modified.
                var props = new HediffCompProperties_GiveHediffsInRange { hediff = native.hediff,
                    initialSeverity = native.initialSeverity, range = 4f, onlyPawnsInSameFaction = true,
                    targetingParameters = native.targetingParameters };
                comp.props = props;
                Require(props.hediff == buffDef && source.Awake() && !source.health.InPainShock,
                    "aura-performance/native awake source and configured recipient buff are valid", check);

                watchedForm = farBody; watchedOrdinary = farOrdinary;
                observer.Patch(identity, prefix: new HarmonyMethod(observe));
                ResetCounts();
                SharedFormIdentity.HumanFor(farBody); SharedFormIdentity.HumanFor(farOrdinary);
                check(farFormQueries > 0 && farOrdinaryQueries > 0,
                    "aura-performance/manual positive controls verify the total identity observer for both far pawns");
                ResetCounts();
                Invoke(comp);
                check(farFormQueries == 0 && farOrdinaryQueries == 0,
                    "aura-performance/ordinary source rejects far pawns before observed identity lookup"
                    + Counts());
                Hediff nearBuff = Raw(near, buffDef), ordinaryBuff = Raw(nearOrdinary, buffDef);
                CheckRecipients(source, near, nearBody, far, farBody, nearOrdinary, farOrdinary, buffDef, check, "ordinary-source");
                Require(nearBuff != null && ordinaryBuff != null, "aura-performance/direct native CompPostTick creates real recipient buffs", check);
                CheckSkippedTicks(comp, source, nearBuff, ordinaryBuff, check);
                nearBuff.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 1;
                ordinaryBuff.TryGetComp<HediffComp_Disappears>().ticksToDisappear = 1;
                Invoke(comp);
                check(ReferenceEquals(Raw(near, buffDef), nearBuff) && ReferenceEquals(Raw(nearOrdinary, buffDef), ordinaryBuff)
                    && nearBuff.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 5
                    && ordinaryBuff.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 5,
                    "aura-performance/native repeat refreshes the same real buff instances and disappearance timers");

                // Force a new native ordinary-recipient link to be made by the
                // physical animal source, then verify canonical link rewriting.
                nearOrdinary.health.RemoveHediff(ordinaryBuff);
                Pawn sourceBody = Transform(source, forms, check);
                MethodInfo onBody = AccessTools.Method(typeof(SharedFormHealth), "OnBody");
                Require(onBody != null, "aura-performance/production body-forwarding scope is available", check);
                ResetCounts();
                Action nativeTick = () => Invoke(comp);
                onBody.Invoke(null, new object[] { aura, sourceBody, nativeTick });
                // The transformed source's retained canonical-link second
                // pass can reach HumanFor through patched buff lookup. Record
                // totals; do not infer a caller from Mono/JIT stack frames.
                check(aura.pawn == source && comp.Pawn == sourceBody,
                    "aura-performance/production scope restores canonical source owner while component follows active body; transformed-source indirect identity totals are informational"
                    + Counts());
                CheckRecipients(source, near, nearBody, far, farBody, nearOrdinary, farOrdinary, buffDef, check, "transformed-source");
                check(ReferenceEquals(Raw(near, buffDef), nearBuff),
                    "aura-performance/source transformation retains existing canonical recipient buff");
            }
            finally
            {
                watchedForm = watchedOrdinary = null;
                observer.Unpatch(identity, observe);
                foreach (Pawn form in forms)
                    if (SharedFormIdentity.HumanFor(form) != null) TransformUtility.RevertToHuman(form, false);
                foreach (Pawn form in forms) if (!form.Destroyed) form.Destroy(DestroyMode.Vanish);
                foreach (Pawn human in humans) if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
            }
        }

        private static void CheckSkippedTicks(HediffComp_GiveHediffsInRange aura, Pawn source,
            Hediff transformedBuff, Hediff ordinaryBuff, Action<bool, string> check)
        {
            var skipper = new Harmony("wildshift.tests.skipped-native-health-tick");
            MethodInfo auraTick = AccessTools.Method(typeof(HediffComp_GiveHediffsInRange), "CompPostTick");
            MethodInfo linkTick = AccessTools.Method(typeof(HediffComp_Link), "CompPostTick");
            MethodInfo skipAura = AccessTools.Method(typeof(SharedAuraPerformanceEngineChecks), nameof(SkipAura));
            MethodInfo skipLink = AccessTools.Method(typeof(SharedAuraPerformanceEngineChecks), nameof(SkipLink));
            try
            {
                skippedAura = aura;
                skippedLink = ordinaryBuff.TryGetComp<HediffComp_Link>();
                Require(skippedLink?.other == source, "aura-performance/ordinary recipient starts with its native link", check);
                skipper.Patch(auraTick, prefix: new HarmonyMethod(skipAura) { priority = Priority.First });
                skipper.Patch(linkTick, prefix: new HarmonyMethod(skipLink) { priority = Priority.First });

                float adjustment = 0f;
                skippedLink.CompPostTick(ref adjustment);
                check(skippedLink.other == source,
                    "aura-performance/foreign prefix skipping a native link tick leaves its target intact");

                HediffComp_Disappears timer = transformedBuff.TryGetComp<HediffComp_Disappears>();
                timer.ticksToDisappear = 1;
                Invoke(aura);
                check(timer.ticksToDisappear == 1,
                    "aura-performance/foreign prefix skipping a native aura also skips shared recipients");
            }
            finally
            {
                skippedAura = null;
                skippedLink = null;
                skipper.Unpatch(auraTick, skipAura);
                skipper.Unpatch(linkTick, skipLink);
            }
        }

        private static void ResetCounts() { farFormQueries = farOrdinaryQueries = 0; }
        private static string Counts()
        {
            return " [total form=" + farFormQueries + ", total ordinary=" + farOrdinaryQueries + "]";
        }
        private static void CheckRecipients(Pawn source, Pawn near, Pawn body, Pawn far, Pawn farBody,
            Pawn ordinary, Pawn farOrdinary, HediffDef def, Action<bool, string> check, string stage)
        {
            Hediff buff = Raw(near, def), ordinaryBuff = Raw(ordinary, def);
            check(buff != null && ReferenceEquals(body.health.hediffSet.GetFirstHediffOfDef(def), buff)
                && body.health.hediffSet.hediffs.OfType<Hediff_SharedFormDrugEffect>().Count(h => ReferenceEquals(h.source, buff)) == 1,
                "aura-performance/" + stage + " boundary-distance transformed recipient has one canonical buff and one projection");
            check(buff?.TryGetComp<HediffComp_Link>()?.other == source
                && buff?.TryGetComp<HediffComp_Disappears>()?.ticksToDisappear == 5,
                "aura-performance/" + stage + " transformed recipient keeps canonical source link and native five-tick expiration");
            check(ordinaryBuff?.TryGetComp<HediffComp_Link>()?.other == source
                && ordinaryBuff?.TryGetComp<HediffComp_Disappears>()?.ticksToDisappear == 5,
                "aura-performance/" + stage + " ordinary recipient keeps canonical source link and native expiration");
            check(Raw(far, def) == null && Raw(farBody, def) == null && Raw(farOrdinary, def) == null,
                "aura-performance/" + stage + " far transformed and ordinary recipients remain unbuffed");
        }
        private static void Invoke(HediffComp_GiveHediffsInRange comp) { float adjustment = 0f; comp.CompPostTick(ref adjustment); }
        private static Hediff Raw(Pawn pawn, HediffDef def) { return pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def == def); }
        private static bool Valid(Map map, IntVec3 cell)
        { return cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map) && cell.GetFirstPawn(map) == null; }
        private static Pawn Human(Map map, IntVec3 cell, string name, List<Pawn> humans)
        {
            Pawn human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                forcedXenotype: XenotypeDefOf.Baseliner));
            human.Name = new NameSingle(name); human.health.RemoveAllHediffs();
            GenSpawn.Spawn(human, cell, map); humans.Add(human); return human;
        }
        private static Pawn Transform(Pawn human, List<Pawn> forms, Action<bool, string> check)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
            TransformUtility.AddOrGetShapeshifter(human, kind);
            Require(TransformUtility.TransformToAnimal(human, kind), "aura-performance/native transformation succeeds", check);
            Pawn body = SharedFormIdentity.ActiveFormFor(human);
            Require(body != null, "aura-performance/active native body exists", check);
            forms.Add(body); return body;
        }
        private static void Require(bool value, string label, Action<bool, string> check)
        { check(value, label); if (!value) throw new InvalidOperationException(label); }
    }
}

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Native lifecycle coverage is valid on the old runtime too.
    // Candidate-only assertions inspect the optional private registration gate.
    public static class RuntimeRegistrationEngineChecks
    {
        private sealed class ExpectedInitializationFailure : Exception { }
        private static Pawn failingBody;

        public static void FailLateInitialization(Pawn __0)
        {
            if (ReferenceEquals(__0, failingBody)) throw new ExpectedInitializationFailure();
        }

        public static void Run(Map map, Action<bool, string> check)
        {
            Pawn human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                forcedXenotype: XenotypeDefOf.Baseliner));
            human.Name = new NameSingle("RuntimeRegistrationCheck");
            human.health.RemoveAllHediffs();
            GenSpawn.Spawn(human, CellFinder.StandableCellNear(map.Center, map, 12f), map);
            Pawn body = null;
            FieldInfo registry = AccessTools.Field(typeof(SharedFormRuntime), "initializedBodies");
            FieldInfo systems = AccessTools.Field(typeof(SharedFormSystems), "bindings");
            Pawn_SkillTracker sourceSkills = human.skills;
            try
            {
                SharedFormRuntime.Initialize(human);
                if (registry != null) check(!Contains(registry, human), "runtime-registration/ordinary original is not registered by Initialize");
                else check(true, "runtime-registration/baseline has no optional registry; candidate-only assertions are skipped");

                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
                TransformUtility.AddOrGetShapeshifter(human, kind);
                Require(TransformUtility.TransformToAnimal(human, kind), "runtime-registration/native transformation succeeds", check);
                body = SharedFormIdentity.ActiveFormFor(human);
                Require(body != null && SharedFormIdentity.HumanFor(body) == human,
                    "runtime-registration/native transformation retains the exact source identity", check);
                if (registry != null) check(Contains(registry, body), "runtime-registration/successful native transformation registers active body");

                SharedFormRuntime.Release(body);
                check(SharedFormIdentity.HumanFor(body) == human,
                    "runtime-registration/runtime release leaves canonical storage intact until body transfer commits");
                if (systems != null) check(!Contains(systems, body), "runtime-registration/release removes borrowed system binding");
                if (registry != null)
                {
                    check(!Contains(registry, body), "runtime-registration/release unregisters before clock callbacks");
                    SharedFormRuntime.Tick(body);
                    SharedFormRuntime.TickInterval(body, 1);
                    if (systems != null) check(!Contains(systems, body),
                        "runtime-registration/released callbacks do not recreate borrowed system bindings");
                }

                SharedFormRuntime.Initialize(body);
                check(SharedFormIdentity.HumanFor(body) == human && body.skills is SharedPawnSkillTracker,
                    "runtime-registration/reinitialization restores the same live shared body");
                if (registry != null) check(Contains(registry, body), "runtime-registration/reinitialization restores runtime registration");

                if (registry != null)
                {
                    // A late module failure must revoke the former successful
                    // registration and propagate the original exception.
                    MethodInfo target = AccessTools.Method(typeof(SharedFormAbilities), "Initialize");
                    MethodInfo prefix = AccessTools.Method(typeof(RuntimeRegistrationEngineChecks), nameof(FailLateInitialization));
                    var observer = new Harmony("wildshift.tests.runtime-registration-failure");
                    bool caught = false;
                    observer.Patch(target, prefix: new HarmonyMethod(prefix));
                    try
                    {
                        failingBody = body;
                        try { SharedFormRuntime.Initialize(body); }
                        catch (ExpectedInitializationFailure) { caught = true; }
                    }
                    finally
                    {
                        failingBody = null;
                        observer.Unpatch(target, prefix);
                    }
                    check(caught, "runtime-registration/late initialization failure propagates without being swallowed");
                    check(!Contains(registry, body), "runtime-registration/partial initialization leaves no active registration");
                    long age = human.ageTracker.AgeBiologicalTicks;
                    SharedFormRuntime.Tick(body);
                    SharedFormRuntime.TickInterval(body, 60);
                    check(human.ageTracker.AgeBiologicalTicks == age,
                        "runtime-registration/failed initialization cannot advance the source clock through runtime callbacks");
                    SharedFormRuntime.Initialize(body);
                    check(Contains(registry, body), "runtime-registration/successful retry registers after a failed initialization");
                }

                HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(body);
                Hediff holding = comp.parent;
                holding.ageTicks = 25;
                var hediffs = body.health.hediffSet.hediffs;
                int hediffCount = hediffs.Count;
                Require(hediffs.Remove(holding), "runtime-registration/B&S fixture detaches the real holder hediff", check);
                try
                {
                    var swap = new SapientAnimalsCompatibility.SwapState { oldPawn = body, storage = comp };
                    SapientAnimalsCompatibility.AfterSwap(body, swap);
                    SapientAnimalsCompatibility.AfterSwap(body, swap);
                    Require(hediffs.Count == hediffCount && hediffs.Contains(holding) && holding.ageTicks == 25
                        && SharedFormIdentity.HumanFor(body) == human,
                        "runtime-registration/failed B&S swap restores the exact original holder once", check);
                }
                finally
                {
                    if (!hediffs.Contains(holding)) hediffs.Add(holding);
                }

                // Even a still-registered body must reject a detached original.
                ThingOwner owner = comp.GetDirectlyHeldThings();
                Action failRemoval = delegate { throw new InvalidOperationException("Test listener failed after native removal"); };
                Pawn detached;
                owner.OnContentsChanged += failRemoval;
                try { detached = comp.ReleaseStoredPawn(); }
                finally { owner.OnContentsChanged -= failRemoval; }
                Require(detached == human, "runtime-registration/native holder releases the exact source", check);
                try
                {
                    long age = human.ageTracker.AgeBiologicalTicks;
                    SharedFormRuntime.Tick(body);
                    SharedFormRuntime.TickInterval(body, 60);
                    check(SharedFormIdentity.HumanFor(body) == null && human.ageTracker.AgeBiologicalTicks == age,
                        "runtime-registration/existing live-owner validation rejects detached source despite optional membership");
                }
                finally
                {
                    Require(comp.TryStore(human), "runtime-registration/native holder restores original ownership after validation", check);
                    SharedFormRuntime.Initialize(body);
                }

                Pawn returned = TransformUtility.RevertToHuman(body, false);
                Require(returned == human && human.Spawned && human.skills == sourceSkills,
                    "runtime-registration/native return restores the same pawn and original skill tracker", check);
                if (registry != null) check(!Contains(registry, body), "runtime-registration/native return unregisters cached inactive form");

                Pawn cached = body;
                Require(TransformUtility.TransformToAnimal(human, kind), "runtime-registration/next native transformation succeeds", check);
                body = SharedFormIdentity.ActiveFormFor(human);
                check(body == cached, "runtime-registration/next transformation retains the same cached body");
                if (registry != null) check(Contains(registry, body), "runtime-registration/reused cached form is registered again");
                Require(TransformUtility.RevertToHuman(body, false) == human,
                    "runtime-registration/second native return succeeds after cache reuse", check);
                if (registry != null) check(!Contains(registry, body), "runtime-registration/second return removes reused form registration");
            }
            finally
            {
                failingBody = null;
                if (body != null && SharedFormIdentity.HumanFor(body) == human) TransformUtility.RevertToHuman(body, false);
                if (body != null && !body.Destroyed && SharedFormIdentity.HumanFor(body) == null) body.Destroy(DestroyMode.Vanish);
                if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
            }
        }

        private static bool Contains(FieldInfo field, Pawn pawn)
        {
            object table = field.GetValue(null);
            return (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, new object[] { pawn, null });
        }
        private static void Require(bool value, string label, Action<bool, string> check)
        {
            check(value, label);
            if (!value) throw new InvalidOperationException(label);
        }
    }
}

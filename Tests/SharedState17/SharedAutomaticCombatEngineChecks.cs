using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY: native effective-verb selection and explicit manual queries.
    // A temporary actor-specific fault hook checks the real Harmony cleanup;
    // it is removed before this method returns and never runs a game tick.
    public static class SharedAutomaticCombatEngineChecks
    {
        private const string FaultMessage = "WildShift automatic-combat fixture fault";
        private static Pawn probeOuter, probeNpc, probePartial;
        private static Verb probeNpcManual;
        private static bool nestedBefore, nestedNpc, nestedPartial, nestedAfter;
        private static bool earlyAttack, earlyBefore, earlyAfter, earlyExpected;
        private static int earlyMode, earlyCalls, eligibilityCalls;

        public static void Run(Map map, Action<bool,string> check)
        {
            Pawn human = null, ordinary = null, npc = null;
            try
            {
                human = Human(map, "AutomaticCombatOriginal");
                Apparel pack = Pack(human);
                CompApparelReloadable reload = pack.GetComp<CompApparelReloadable>();
                Verb_Jump jump = reload.AllVerbs.OfType<Verb_Jump>().Single();
                Require(reload.RemainingCharges > 0 && jump.Available(),
                    "autocombat/native original jump pack is charged and usable", check);
                check(human.CurrentEffectiveVerb?.IsMeleeAttack == true
                    && ReferenceEquals(human.TryGetAttackVerb(null, true), jump),
                    "autocombat/ordinary human keeps native melee default and explicit manual jump selection");

                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
                TransformUtility.AddOrGetShapeshifter(human, kind);
                Require(TransformUtility.TransformToAnimal(human, kind),
                    "autocombat/production transformation creates the player animal form", check);
                Pawn body = SharedFormIdentity.ActiveFormFor(human);
                Require(body != null && body.drafter != null,
                    "autocombat/active form keeps its original person and draft controller", check);
                body.drafter.Drafted = true;
                int charges = reload.RemainingCharges;
                Verb automatic = body.CurrentEffectiveVerb;
                Require(automatic != null && automatic.IsMeleeAttack && automatic.CasterPawn == body,
                    "autocombat/drafted unarmed form defaults to its native melee rather than the manual jump", check);
                check(ReferenceEquals(body.TryGetAttackVerb(null, true), jump)
                    && jump.CasterPawn == body && jump.Available(),
                    "autocombat/explicit allow-manual query still returns the same borrowed jump verb");
                check(reload.RemainingCharges == charges
                    && pack.holdingOwner == human.apparel.GetDirectlyHeldThings(),
                    "autocombat/automatic and manual selection spend no charges and preserve the actual item owner");

                ordinary = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
                ordinary.health.RemoveAllHediffs();
                GenSpawn.Spawn(ordinary, Open(map), map);
                // A real Warg lacks the human waist groups needed to wear a
                // jump pack. Keep this control anatomically native rather
                // than forcing an invalid apparel holder onto an animal.
                Require(SharedFormIdentity.HumanFor(ordinary) == null && ordinary.RaceProps.Animal,
                    "autocombat/control is a real ordinary animal without a stored person", check);
                Verb ordinaryAutomatic = ordinary.CurrentEffectiveVerb;
                check(ordinaryAutomatic?.IsMeleeAttack == true && ordinaryAutomatic.CasterPawn == ordinary
                    && ReferenceEquals(ordinary.TryGetAttackVerb(null, true), ordinaryAutomatic),
                    "autocombat/ordinary animal retains native natural melee for automatic and explicit queries");

                npc = Human(map, "AutomaticCombatNpc");
                Faction other = Find.FactionManager.AllFactionsListForReading.First(f => !f.IsPlayer && f.def.humanlikeFaction);
                npc.SetFaction(other);
                Apparel npcPack = Pack(npc);
                Verb npcManual = npcPack.GetComp<CompApparelReloadable>().AllVerbs.OfType<Verb_Jump>().Single();
                Require(npcManual.Available() && !npc.Faction.IsPlayer,
                    "autocombat/native nonplayer control has a usable manual apparel verb", check);
                check(ReferenceEquals(npc.CurrentEffectiveVerb, npcManual),
                    "autocombat/nonplayer pawn retains its existing native automatic verb policy");

                Require(body.mindState.mentalStateHandler.TryStartMentalState(
                    DefDatabase<MentalStateDef>.GetNamed("Wander_Sad"), forced:true, forceWake:true, causedByMood:true),
                    "autocombat/native mental state starts on the transformed person", check);
                check(ReferenceEquals(body.CurrentEffectiveVerb, jump),
                    "autocombat/mental-state form retains the uncontrolled native manual-verb policy");
                body.MentalState.RecoverFromState();
                body.drafter.Drafted = true;
                Require(body.CurrentEffectiveVerb?.IsMeleeAttack == true,
                    "autocombat/recovered player form immediately regains its normal automatic melee policy", check);

                ScopeCleanup(body, jump, npc, npcManual, check);
                EarlierPrefixCleanup(body, jump, npc, npcManual, check);

                ThingWithComps rifle = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle"));
                human.equipment.AddEquipment(rifle);
                Verb ranged = body.CurrentEffectiveVerb;
                check(ranged != null && !ranged.IsMeleeAttack && !ranged.verbProps.onlyManualCast
                    && ranged.EquipmentSource == rifle && ranged.CasterPawn == body,
                    "autocombat/ordinary borrowed rifle remains the active automatic ranged verb");
                check(ReferenceEquals(body.TryGetAttackVerb(null, true), ranged)
                    && rifle.holdingOwner == human.equipment.GetDirectlyHeldThings(),
                    "autocombat/normal rifle priority and original weapon ownership remain native");
                Require(TransformUtility.RevertToHuman(body, false) == human,
                    "autocombat/return restores the exact original after automatic selection probes", check);
                check(pack.holdingOwner == human.apparel.GetDirectlyHeldThings()
                    && rifle.holdingOwner == human.equipment.GetDirectlyHeldThings()
                    && reload.RemainingCharges == charges,
                    "autocombat/return preserves source gear and does not consume or refill manual charges");
            }
            finally
            {
                ClearProbe();
                Cleanup(human);
                Cleanup(ordinary);
                Cleanup(npc);
            }
        }

        private static void ScopeCleanup(Pawn body, Verb manual, Pawn npc, Verb npcManual, Action<bool,string> check)
        {
            var harmony = new Harmony("wildshift.tests.automaticcombat.scope");
            var getter = AccessTools.PropertyGetter(typeof(Pawn), "CurrentEffectiveVerb");
            probeOuter = body; probeNpc = npc; probeNpcManual = npcManual;
            // A deliberately incomplete Pawn simulates a third-party prefix
            // answering a query before native health initialization. Only its
            // actor-specific test hook returns null; native code is not run.
            probePartial = new Pawn();
            probePartial.health = null;
            nestedBefore = nestedNpc = nestedPartial = nestedAfter = false;
            bool caught = false;
            try
            {
                harmony.Patch(getter, prefix:new HarmonyMethod(typeof(SharedAutomaticCombatEngineChecks), nameof(ProbeGetter))
                    { priority = Priority.Last });
                try { Verb ignored = body.CurrentEffectiveVerb; }
                catch (InvalidOperationException error) when (error.Message == FaultMessage) { caught = true; }
            }
            finally
            {
                ClearProbe();
                harmony.Unpatch(getter, HarmonyPatchType.Prefix, harmony.Id);
            }
            check(caught && nestedBefore,
                "autocombat/real automatic getter enters its policy before a later test prefix throws");
            check(nestedNpc && nestedPartial && nestedAfter,
                "autocombat/nested nonplayer and incomplete-pawn queries restore the outer automatic actor");
            check(ReferenceEquals(body.TryGetAttackVerb(null, true), manual),
                "autocombat/real Harmony finalizer restores explicit manual permission after a getter exception");
            check(body.CurrentEffectiveVerb?.IsMeleeAttack == true
                && ReferenceEquals(body.TryGetAttackVerb(null, true), manual),
                "autocombat/removing the temporary fault hook leaves normal automatic and explicit manual queries intact");
        }

        public static bool ProbeGetter(Pawn __instance, ref Verb __result)
        {
            if (ReferenceEquals(__instance, probePartial)) { __result = null; return false; }
            if (!ReferenceEquals(__instance, probeOuter) || probeOuter == null) return true;
            nestedBefore = probeOuter.TryGetAttackVerb(null, true)?.IsMeleeAttack == true;
            nestedNpc = ReferenceEquals(probeNpc.CurrentEffectiveVerb, probeNpcManual);
            nestedPartial = probePartial.CurrentEffectiveVerb == null;
            nestedAfter = probeOuter.TryGetAttackVerb(null, true)?.IsMeleeAttack == true;
            throw new InvalidOperationException(FaultMessage);
        }

        private static void EarlierPrefixCleanup(Pawn body, Verb manual, Pawn npc, Verb npcManual, Action<bool,string> check)
        {
            var harmony = new Harmony("wildshift.tests.automaticcombat.earlier-prefix");
            var getter = AccessTools.PropertyGetter(typeof(Pawn), "CurrentEffectiveVerb");
            var attack = AccessTools.Method(typeof(Pawn), "TryStartAttack", new[] { typeof(LocalTargetInfo) });
            var eligibility = AccessTools.DeclaredMethod(typeof(TransformedAutomaticCombat), "IsControlledForm", new[] { typeof(Pawn) });
            Require(getter != null && attack != null && eligibility != null,
                "autocombat/earlier-prefix probe resolves the real getter, attack and eligibility methods", check);
            probeOuter = body; probeNpc = npc; probeNpcManual = npcManual;
            try
            {
                // int.MaxValue guarantees these external hooks precede the
                // production Priority.First prefix. The outer hook runs later.
                harmony.Patch(getter,
                    prefix:new HarmonyMethod(typeof(SharedAutomaticCombatEngineChecks), nameof(EarlyGetter)) { priority = int.MaxValue });
                harmony.Patch(getter,
                    prefix:new HarmonyMethod(typeof(SharedAutomaticCombatEngineChecks), nameof(ProbeEarlyOuter)) { priority = Priority.Last });
                harmony.Patch(attack,
                    prefix:new HarmonyMethod(typeof(SharedAutomaticCombatEngineChecks), nameof(EarlyAttack)) { priority = int.MaxValue });
                // Patch the actual eligibility entry, not a tiny native getter
                // which Mono may already have inlined before this test runs.
                harmony.Patch(eligibility,
                    prefix:new HarmonyMethod(typeof(SharedAutomaticCombatEngineChecks), nameof(EarlyEligibility)) { priority = int.MaxValue });
                for (int entry = 0; entry < 2; entry++)
                    for (int mode = 0; mode < 3; mode++)
                    {
                        earlyAttack = entry == 1; earlyMode = mode;
                        earlyCalls = eligibilityCalls = 0;
                        earlyBefore = earlyAfter = earlyExpected = false;
                        Verb result = body.CurrentEffectiveVerb;
                        string label = (earlyAttack ? "attack/" : "getter/") + (mode == 0 ? "early-throw" : mode == 1 ? "early-skip" : "eligibility-throw");
                        check(earlyCalls == 1 && earlyExpected && (mode != 2 || eligibilityCalls == 1),
                            "autocombat/real external prefix takes the intended nested NPC branch: " + label);
                        check(earlyBefore && earlyAfter && result?.IsMeleeAttack == true,
                            "autocombat/nested skipped or throwing entry preserves the outer automatic policy: " + label);
                        check(ReferenceEquals(body.TryGetAttackVerb(null, true), manual),
                            "autocombat/outer completion restores explicit manual permission: " + label);
                    }
            }
            finally
            {
                ClearProbe();
                harmony.Unpatch(getter, HarmonyPatchType.Prefix, harmony.Id);
                harmony.Unpatch(attack, HarmonyPatchType.Prefix, harmony.Id);
                harmony.Unpatch(eligibility, HarmonyPatchType.Prefix, harmony.Id);
            }
            check(body.CurrentEffectiveVerb?.IsMeleeAttack == true && ReferenceEquals(body.TryGetAttackVerb(null, true), manual),
                "autocombat/removing all earlier-prefix probes preserves automatic and manual selection");
        }

        public static bool EarlyGetter(Pawn __instance, ref Verb __result)
        {
            if (!ReferenceEquals(__instance, probeNpc) || probeNpc == null || earlyAttack) return true;
            earlyCalls++;
            if (earlyMode == 0) throw new InvalidOperationException(FaultMessage);
            if (earlyMode == 1) { __result = probeNpcManual; return false; }
            return true;
        }

        public static bool EarlyAttack(Pawn __instance, ref bool __result)
        {
            if (!ReferenceEquals(__instance, probeNpc) || probeNpc == null || !earlyAttack) return true;
            earlyCalls++;
            if (earlyMode == 0) throw new InvalidOperationException(FaultMessage);
            if (earlyMode == 1) { __result = false; return false; }
            return true;
        }

        public static void EarlyEligibility(Pawn __0)
        {
            if (earlyMode != 2 || !ReferenceEquals(__0, probeNpc) || probeNpc == null) return;
            eligibilityCalls++;
            throw new InvalidOperationException(FaultMessage);
        }

        public static void ProbeEarlyOuter(Pawn __instance)
        {
            if (!ReferenceEquals(__instance, probeOuter) || probeOuter == null) return;
            earlyBefore = probeOuter.TryGetAttackVerb(null, true)?.IsMeleeAttack == true;
            try
            {
                if (earlyAttack) earlyExpected = !probeNpc.TryStartAttack(probeOuter) && earlyMode == 1;
                else earlyExpected = ReferenceEquals(probeNpc.CurrentEffectiveVerb, probeNpcManual) && earlyMode == 1;
            }
            catch (InvalidOperationException error) when (error.Message == FaultMessage)
            {
                earlyExpected = earlyMode != 1;
            }
            earlyAfter = probeOuter.TryGetAttackVerb(null, true)?.IsMeleeAttack == true;
        }

        private static void ClearProbe()
        {
            probeOuter = probeNpc = probePartial = null; probeNpcManual = null;
            earlyAttack = earlyBefore = earlyAfter = earlyExpected = false;
            earlyMode = earlyCalls = eligibilityCalls = 0;
        }

        private static Apparel Pack(Pawn pawn)
        {
            Apparel pack = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PackJump"));
            pawn.apparel.Wear(pack, false);
            return pack;
        }

        private static Pawn Human(Map map, string name)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn:true, canGeneratePawnRelations:false, mustBeCapableOfViolence:true,
                fixedGender:Gender.Male, fixedBiologicalAge:25f, forcedXenotype:XenotypeDefOf.Baseliner));
            pawn.Name = new NameSingle(name); pawn.health.RemoveAllHediffs();
            foreach (Trait trait in pawn.story.traits.allTraits.ToList()) pawn.story.traits.RemoveTrait(trait);
            foreach (Gene gene in pawn.genes.GenesListForReading.ToList()) pawn.genes.RemoveGene(gene);
            foreach (Apparel apparel in pawn.apparel.WornApparel.ToList()) pawn.apparel.Remove(apparel);
            pawn.equipment.DestroyAllEquipment();
            GenSpawn.Spawn(pawn, Open(map), map); pawn.drafter.Drafted = true;
            return pawn;
        }

        private static IntVec3 Open(Map map)
        { return CellFinder.StandableCellNear(map.Center, map, 12f); }

        private static void Cleanup(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed) return;
            Pawn form = SharedFormIdentity.ActiveFormFor(pawn);
            if (form != null) TransformUtility.RevertToHuman(form, false);
            if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
        }

        private static void Require(bool value, string label, Action<bool,string> check)
        { check(value,label); if (!value) throw new InvalidOperationException(label); }
    }
}

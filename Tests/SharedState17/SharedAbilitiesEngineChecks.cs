using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY: real generated pawns, native gene grants, commands, casting jobs,
    // projectiles, blood loss, tending and game ticks. No gameplay method is mocked.
    public static class SharedAbilitiesEngineChecks
    {
        public static void Run(Map map, Action<bool, string> check)
        {
            if (map == null || check == null) throw new ArgumentNullException();
            Pawn human = Human(map, "SharedAbilitiesOriginal", map.Center + new IntVec3(-18, 0, 15));
            foreach (string name in new[] { "Hemogenic", "HemogenDrain", "FireSpew", "FoamSpray", "LongjumpLegs", "Bloodfeeder", "Coagulate", "AnimalWarcall", "PiercingSpine", "AcidSpray", "XenogermReimplanter" })
            {
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(name);
                if (def != null && human.genes.GetGene(def) == null) human.genes.AddGene(def, false);
            }
            string[] supported = { "FireSpew", "FoamSpray", "Longjump", "Bloodfeed", "Coagulate", "AnimalWarcall", "PiercingSpine", "AcidSpray" };
            var sourceAbilities = new Dictionary<string, Ability>();
            foreach (string name in supported)
            {
                Ability source = human.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed(name));
                Require(source != null && SharedFormAbilities.IsSupported(source), "abilities/native gene grants supported " + name, check);
                sourceAbilities.Add(name, source);
            }
            Gene_Hemogen hemogen = human.genes.GetFirstGeneOfType<Gene_Hemogen>();
            Require(hemogen != null, "abilities/native hemogenic gene owns the one resource", check);
            hemogen.Value = .7f;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
            TransformUtility.AddOrGetShapeshifter(human, kind);
            Require(TransformUtility.TransformToAnimal(human, kind), "abilities/production transformation succeeds", check);
            Pawn animal = map.mapPawns.AllPawnsSpawned.Single(p => TransformUtility.TryGetTransformedComp(p)?.StoredPawn == human);
            animal.drafter.Drafted = true;
            check(animal.genes == null || animal.genes != human.genes,
                "abilities/active real animal does not share the human gene tracker");
            foreach (var entry in sourceAbilities)
            {
                Ability source = entry.Value;
                check(animal.abilities.GetAbility(source.def) == source && source.pawn == animal && source.verb.CasterPawn == animal,
                    "abilities/native lookup and verb use same source and active caster for " + entry.Key);
                check(!animal.abilities.abilities.Contains(source) && human.abilities.abilities.Contains(source),
                    "abilities/only human deep-owns " + entry.Key);
            }
            Ability reimplant = human.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("ReimplantXenogerm"));
            check(reimplant != null && animal.abilities.AllAbilitiesForReading.Contains(reimplant)
                && reimplant.pawn==animal && !animal.abilities.abilities.Contains(reimplant),
                "abilities/native gene reimplantation borrows the original ability while genetics remain canonical");

            Foam(map, animal, sourceAbilities["FoamSpray"], check);
            Jump(map, animal, sourceAbilities["Longjump"], hemogen, check);
            Pawn patient = Human(map, "SharedAbilitiesPatient", animal.Position + new IntVec3(1, 0, 0));
            patient.drafter.Drafted = true;
            Bloodfeed(animal, human, patient, sourceAbilities["Bloodfeed"], hemogen, check);
            Coagulate(animal, human, patient, sourceAbilities["Coagulate"], hemogen, check);
            Warcall(map, animal, sourceAbilities["AnimalWarcall"], check);

            Ability fire = sourceAbilities["FireSpew"], foam = sourceAbilities["FoamSpray"], warcall = sourceAbilities["AnimalWarcall"];
            fire.StartCooldown(12000);
            int fireCooldown = fire.CooldownTicksRemaining;
            int foamCharges = foam.RemainingCharges, warcallCharges = warcall.RemainingCharges;
            float blood = hemogen.Value;
            Require(TransformUtility.RevertToHuman(animal, false) == human, "abilities/return restores the exact original human", check);
            check(sourceAbilities.Values.All(a => a.pawn == human && a.verb.CasterPawn == human),
                "abilities/return restores every source ability and verb to human caster");
            check(fire.CooldownTicksRemaining == fireCooldown && foam.RemainingCharges == foamCharges
                && warcall.RemainingCharges == warcallCharges && Math.Abs(hemogen.Value - blood) < .00001f,
                "abilities/return does not reset cooldown, charges or hemogen");
            Require(TransformUtility.TransformToAnimal(human, kind), "abilities/reuses the same animal after return", check);
            check(map.mapPawns.AllPawnsSpawned.Contains(animal) && animal.abilities.GetAbility(fire.def) == fire
                && fire.pawn == animal && fire.verb.CasterPawn == animal,
                "abilities/repeated transformation rebinds exact original Ability to cached animal");
            check(fire.CooldownTicksRemaining == fireCooldown && foam.RemainingCharges == foamCharges
                && warcall.RemainingCharges == warcallCharges && Math.Abs(hemogen.Value - blood) < .00001f,
                "abilities/repeated transformation cannot refill ability charges or resource");
            Require(TransformUtility.RevertToHuman(animal, false) == human, "abilities/probe leaves a restored original and cached body", check);
            patient.drafter.Drafted = false;
        }

        private static void Foam(Map map, Pawn animal, Ability foam, Action<bool, string> check)
        {
            Command_Ability command = animal.abilities.GetGizmos().OfType<Command_Ability>().Single(c => c.Ability == foam);
            Require(command.Pawn == animal && !command.Disabled, "abilities/native foam command belongs to active animal and is enabled", check);
            IntVec3 target = Open(map, animal.Position + new IntVec3(3, 0, 0));
            Require(foam.verb.ValidateTarget(target, false), "abilities/native foam range and line of sight are evaluated from animal", check);
            int charges = foam.RemainingCharges;
            foam.QueueCastingJob(target, LocalTargetInfo.Invalid);
            Require(animal.CurJob != null && animal.CurJob.ability == foam && animal.CurJob.verbToUse == foam.verb,
                "abilities/native foam job is assigned to actual animal with canonical ability", check);
            AdvanceUntil(delegate { return foam.RemainingCharges < charges; }, 500, "native foam cast", check);
            for (int i = 0; i < 100; i++) Find.TickManager.DoSingleTick();
            check(foam.RemainingCharges == charges - 1,
                "abilities/real foam cast consumes exactly one canonical charge");
            ThingDef filth = DefDatabase<ThingDef>.GetNamed("Filth_FireFoam");
            check(GenRadial.RadialCellsAround(target, 4f, true).Any(c => c.InBounds(map) && c.GetThingList(map).Any(t => t.def == filth)),
                "abilities/real animal foam job creates native firefoam filth at targeted cells");
        }

        private static void Jump(Map map, Pawn animal, Ability jump, Gene_Hemogen hemogen, Action<bool, string> check)
        {
            animal.jobs.StopAll();
            IntVec3 target = Open(map, animal.Position + new IntVec3(3, 0, 0));
            Require(jump.verb.ValidateTarget(target, false), "abilities/native longjump validates active animal destination", check);
            float start = hemogen.Value;
            jump.QueueCastingJob(target, LocalTargetInfo.Invalid);
            AdvanceUntil(delegate { return animal.Spawned && animal.Position == target && hemogen.Value < start - .049f; },
                600, "native longjump landing and source cost", check);
            check(jump.pawn == animal && jump.verb.CasterPawn == animal && SharedFormIdentity.HumanFor(animal) == hemogen.pawn,
                "abilities/native flyer roundtrip retains actual animal, source human and canonical verb");
            check(hemogen.Value < start - .049f && hemogen.Value > start - .08f,
                "abilities/real longjump spends original hemogen plus ordinary passive drain");
        }

        private static void Bloodfeed(Pawn animal, Pawn human, Pawn patient, Ability bite, Gene_Hemogen hemogen, Action<bool, string> check)
        {
            animal.jobs.StopAll(); hemogen.Value = .4f;
            animal.needs.food.CurLevel = .1f;
            float humanFood = human.needs.food.CurLevel;
            Require(bite.verb.ValidateTarget(patient, false), "abilities/native bloodfeed target validation accepts the patient", check);
            bite.QueueCastingJob(patient, LocalTargetInfo.Invalid);
            AdvanceUntil(delegate { return patient.health.hediffSet.HasHediff(HediffDefOf.BloodLoss) && hemogen.Value > .5f; },
                600, "native bloodfeed resource and target blood loss", check);
            check(patient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss).Severity > .1f,
                "abilities/actual bloodfeed applies native blood loss to target");
            check(animal.needs.food.CurLevel > .15f && ReferenceEquals(animal.needs.food,human.needs.food)
                && human.needs.food.CurLevel > humanFood,
                "abilities/bloodfeed feeds the active animal through one canonical food need and original hemogen");
        }

        private static void Coagulate(Pawn animal, Pawn human, Pawn patient, Ability coagulate, Gene_Hemogen hemogen, Action<bool, string> check)
        {
            animal.jobs.StopAll(); hemogen.Value = .8f;
            BodyPartRecord part = patient.health.hediffSet.GetNotMissingParts().First(p => p.depth == BodyPartDepth.Outside && p.def == BodyPartDefOf.Torso);
            Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, part);
            wound.Severity = 2f; patient.health.AddHediff(wound, part);
            Require(wound.TendableNow(), "abilities/native patient cut is tendable before coagulate", check);
            AbilityComp required = coagulate.comps.First(c => c is CompAbility_RequiresCapacity);
            string reason;
            Require(human.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && !required.GizmoDisabled(out reason),
                "abilities/shared coagulate manipulation check uses intact original human", check);
            Require(coagulate.verb.ValidateTarget(patient, false), "abilities/native coagulate validates real target wound", check);
            float start = hemogen.Value;
            coagulate.QueueCastingJob(patient, LocalTargetInfo.Invalid);
            AdvanceUntil(delegate { return wound.TryGetComp<HediffComp_TendDuration>().IsTended; },
                600, "native coagulate tends patient", check);
            check(hemogen.Value < start - .199f && hemogen.Value > start - .23f,
                "abilities/real coagulate consumes original hemogen once");
        }

        private static void Warcall(Map map, Pawn animal, Ability warcall, Action<bool, string> check)
        {
            animal.jobs.StopAll();
            Pawn ally = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Hare"), Faction.OfPlayer);
            GenSpawn.Spawn(ally, Open(map, animal.Position + new IntVec3(2, 0, 0)), map);
            int charges = warcall.RemainingCharges;
            Require(warcall.verb.ValidateTarget(ally, false), "abilities/native animal warcall validates allied animal", check);
            warcall.QueueCastingJob(ally, LocalTargetInfo.Invalid);
            AdvanceUntil(delegate { return ally.InMentalState && warcall.RemainingCharges < charges; },
                600, "native animal warcall", check);
            check(warcall.RemainingCharges == charges - 1 && warcall.CooldownTicksRemaining > 0
                && ally.MentalState.def.defName == "BerserkWarcall",
                "abilities/real warcall applies native state and retains canonical per-charge cooldown");
            ally.mindState.mentalStateHandler.CurState.RecoverFromState();
        }

        private static void AdvanceUntil(Func<bool> complete, int limit, string label, Action<bool, string> check)
        {
            for (int i = 0; i < limit && !complete(); i++) Find.TickManager.DoSingleTick();
            Require(complete(), "abilities/" + label, check);
        }
        private static Pawn Human(Map map, string name, IntVec3 requested)
        {
            Pawn human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                fixedGender: Gender.Male, forcedXenotype: XenotypeDefOf.Baseliner));
            human.Name = new NameSingle(name); human.health.RemoveAllHediffs();
            GenSpawn.Spawn(human, Open(map, requested), map);
            return human;
        }
        private static IntVec3 Open(Map map, IntVec3 requested)
        {
            return requested.InBounds(map) && requested.Standable(map)
                ? requested : CellFinder.RandomClosewalkCellNear(map.Center, map, 8);
        }
        private static void Require(bool passed, string label, Action<bool, string> check)
        {
            check(passed, label);
            if (!passed) throw new InvalidOperationException("Shared-ability engine prerequisite: " + label);
        }
    }
}

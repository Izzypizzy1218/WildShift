using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Native Muffalo, enemy searcher, drafting, mental state and
    // anesthesia exercise the actual patched methods without replacing them.
    // The critical query is deliberately made before the first combat job or
    // elapsed tick, matching a newly drafted transformed herd animal.
    public static class SharedRoamerThreatEngineChecks
    {
        public static void Run(Map map, Action<bool,string> check)
        {
            Pawn human=null, enemy=null, ordinary=null;
            try
            {
                Faction hostile=Find.FactionManager.AllFactionsListForReading.First(f=>
                    !f.IsPlayer&&f.def.humanlikeFaction&&f.HostileTo(Faction.OfPlayer));
                enemy=Human(map,hostile,"RoamerThreatEnemy");
                Require(enemy.GetLord()==null,"roamer-threat/native enemy searcher has no Lord overriding roamer targeting",check);
                PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Muffalo");
                ordinary=PawnGenerator.GeneratePawn(kind,Faction.OfPlayer);
                ordinary.health.RemoveAllHediffs();
                GenSpawn.Spawn(ordinary,Open(map),map);
                // An ordinary animal normally has no draft controller. A real
                // native controller represents compatibility with animal-draft
                // mods while preserving the absence of a stored human.
                if(ordinary.drafter==null)ordinary.drafter=new Pawn_DraftController(ordinary);
                NoJob(ordinary);
                Require(ordinary.Roamer&&SharedFormIdentity.HumanFor(ordinary)==null,
                    "roamer-threat/control is real ordinary native roaming livestock",check);
                check(ordinary.ThreatDisabledBecauseNonAggressiveRoamer(enemy),
                    "roamer-threat/ordinary undrafted livestock keeps native nonaggressive protection");
                ordinary.drafter.Drafted=true;NoJob(ordinary);
                check(ordinary.Drafted&&ordinary.ThreatDisabledBecauseNonAggressiveRoamer(enemy)
                    &&ordinary.ThreatDisabled(enemy),
                    "roamer-threat/ordinary drafted livestock remains outside the person-only compatibility rule");

                human=Human(map,Faction.OfPlayer,"RoamerThreatOriginal");
                TransformUtility.AddOrGetShapeshifter(human,kind);
                Require(TransformUtility.TransformToAnimal(human,kind),
                    "roamer-threat/production transformation creates a native Muffalo form",check);
                Pawn body=SharedFormIdentity.ActiveFormFor(human);
                Require(body!=null&&body.Roamer&&body.drafter!=null&&body.mindState.Active&&!body.Downed,
                    "roamer-threat/live roaming form owns the exact original and can be drafted",check);
                int tick=Find.TickManager.TicksGame;
                body.drafter.Drafted=false;NoJob(body);
                check(body.ThreatDisabledBecauseNonAggressiveRoamer(enemy)&&body.ThreatDisabled(enemy),
                    "roamer-threat/undrafted transformed livestock retains its native nonaggressive protection");
                body.drafter.Drafted=true;NoJob(body);
                check(body.CurJob==null&&!body.ThreatDisabledBecauseNonAggressiveRoamer(enemy)
                    &&!body.ThreatDisabled(enemy),
                    "roamer-threat/fresh drafting makes the person targetable before any combat job or game tick");
                check(map.attackTargetsCache.GetPotentialTargetsFor(enemy).Contains(body),
                    "roamer-threat/native hostile target cache already contains the spawned person without re-registration");
                body.drafter.Drafted=false;NoJob(body);
                check(body.ThreatDisabledBecauseNonAggressiveRoamer(enemy),
                    "roamer-threat/same-tick undrafting restores native roamer protection");
                body.drafter.Drafted=true;NoJob(body);
                check(!body.ThreatDisabledBecauseNonAggressiveRoamer(enemy)&&Find.TickManager.TicksGame==tick,
                    "roamer-threat/same-tick redrafting removes only the roamer exemption immediately");

                body.SetFaction(hostile);body.drafter.Drafted=true;NoJob(body);
                check(body.Faction==hostile&&!body.ThreatDisabledBecauseNonAggressiveRoamer(enemy),
                    "roamer-threat/nonplayer form retains the native nonplayer roamer result");
                body.SetFaction(Faction.OfPlayer);body.drafter.Drafted=true;NoJob(body);
                Require(body.mindState.mentalStateHandler.TryStartMentalState(
                    DefDatabase<MentalStateDef>.GetNamed("Wander_Sad"),forced:true,forceWake:true,causedByMood:true),
                    "roamer-threat/native nonaggressive mental state starts on the preserved person",check);
                body.drafter.Drafted=true;NoJob(body);
                check(body.MentalStateDef!=null&&body.ThreatDisabledBecauseNonAggressiveRoamer(enemy),
                    "roamer-threat/a mental-state form keeps the native roamer exemption even when marked drafted");
                body.MentalState.RecoverFromState();body.drafter.Drafted=true;NoJob(body);
                check(!body.ThreatDisabledBecauseNonAggressiveRoamer(enemy),
                    "roamer-threat/recovery restores the controlled drafted targeting policy immediately");

                Hediff anesthetic=body.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Anesthetic"));
                SharedFormHealth.Refresh(body);
                Require(body.Downed,"roamer-threat/native anesthesia downs the real transformed body",check);
                body.drafter.Drafted=true;NoJob(body);
                check(!body.ThreatDisabledBecauseNonAggressiveRoamer(enemy)&&body.ThreatDisabled(enemy),
                    "roamer-threat/removing only the roamer exemption does not bypass the outer native downed threat gate");
                anesthetic.pawn.health.RemoveHediff(anesthetic);SharedFormHealth.Refresh(body);
                Require(!body.Downed,"roamer-threat/removing native anesthesia restores the active body",check);
                body.drafter.Drafted=true;NoJob(body);
                check(!body.ThreatDisabled(enemy)&&Find.TickManager.TicksGame==tick,
                    "roamer-threat/recovered drafted form is targetable without delayed refresh or elapsed ticks");
                Require(TransformUtility.RevertToHuman(body,false)==human,
                    "roamer-threat/return restores the exact original after native threat probes",check);
            }
            finally
            {
                Cleanup(human);Cleanup(ordinary);Cleanup(enemy);
            }
        }

        private static void NoJob(Pawn pawn)
        {
            pawn.jobs.StopAll();
            pawn.mindState.lastEngageTargetTick=-99999;
        }
        private static Pawn Human(Map map,Faction faction,string name)
        {
            Pawn pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,faction,
                forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,
                fixedGender:Gender.Male,fixedBiologicalAge:25f,forcedXenotype:XenotypeDefOf.Baseliner));
            pawn.Name=new NameSingle(name);pawn.health.RemoveAllHediffs();
            foreach(Trait trait in pawn.story.traits.allTraits.ToList())pawn.story.traits.RemoveTrait(trait);
            foreach(Gene gene in pawn.genes.GenesListForReading.ToList())pawn.genes.RemoveGene(gene);
            pawn.equipment.DestroyAllEquipment();
            foreach(Apparel apparel in pawn.apparel.WornApparel.ToList())pawn.apparel.Remove(apparel);
            GenSpawn.Spawn(pawn,Open(map),map);return pawn;
        }
        private static IntVec3 Open(Map map) { return CellFinder.StandableCellNear(map.Center,map,12f); }
        private static void Cleanup(Pawn pawn)
        {
            if(pawn==null||pawn.Destroyed)return;
            Pawn form=SharedFormIdentity.ActiveFormFor(pawn);
            if(form!=null)TransformUtility.RevertToHuman(form,false);
            if(!pawn.Destroyed)pawn.Destroy(DestroyMode.Vanish);
        }
        private static void Require(bool value,string label,Action<bool,string> check)
        { check(value,label);if(!value)throw new InvalidOperationException(label); }
    }
}

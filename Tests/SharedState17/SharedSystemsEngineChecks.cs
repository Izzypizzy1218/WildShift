using System;
using System.Linq;
using RimWorld;
using RimWorld.Utility;
using Verse;
using Verse.AI;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY: production transforms, actual native psycast effects/resources,
    // native mechanitor relations/drafting and persisted original-owned state.
    public static class SharedSystemsEngineChecks
    {
        private static bool jumpDiagnosticsPatched;
        private static Pawn jumpDiagnosticPawn;
        private static string lastJumpJobEnd;
        public static void Run(Map map,Action<bool,string> check)
        {
            Pawn human=Human(map,"SharedSystemsOriginal",map.Center+new IntVec3(15,0,-15));
            Pawn target=Human(map,"SharedSystemsTarget",human.Position+new IntVec3(2,0,0));
            human.skills.GetSkill(SkillDefOf.Melee).Level=20;
            Apparel jumpPack=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PackJump"));
            human.apparel.Wear(jumpPack,false);
            // Skip25 + invisibility45 cannot fit a normal30-heat human either.
            // Give the original actual native psychic enhancements rather than
            // bypassing entropy limits or testing an invalid baseline caster.
            human.story.traits.GainTrait(new Trait(DefDatabase<TraitDef>.GetNamed("PsychicSensitivity"),2));
            human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("PsychicAbility_Extreme"),false);
            BodyPartRecord brain=human.health.hediffSet.GetBrain();
            human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("PsychicSensitizer"),brain);
            var psylink=(Hediff_Psylink)HediffMaker.MakeHediff(HediffDefOf.PsychicAmplifier,human,brain);
            psylink.suppressPostAddLetter=true;
            human.health.AddHediff(psylink,brain);
            psylink.level=6;
            human.abilities.GainAbility(DefDatabase<AbilityDef>.GetNamed("Skip"));
            human.abilities.GainAbility(DefDatabase<AbilityDef>.GetNamed("Invisibility"));
            var skip=(Psycast)human.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("Skip"));
            var invisibility=(Psycast)human.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("Invisibility"));
            Require(SharedFormAbilities.IsSupported(skip)&&SharedFormAbilities.IsSupported(invisibility),"systems/native skip and invisibility qualify",check);
            human.psychicEntropy.RechargePsyfocus();human.psychicEntropy.RemoveAllEntropy();
            Require(human.psychicEntropy.MaxEntropy>=70f,"systems/native enhanced human has heat capacity for both test psycasts",check);
            human.health.AddHediff(HediffDefOf.MechlinkImplant,brain);
            Require(human.mechanitor!=null,"systems/native mechlink creates canonical mechanitor tracker",check);
            Pawn mech=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Mech_Lifter"),Faction.OfPlayer);
            GenSpawn.Spawn(mech,Open(map,human.Position+new IntVec3(1,0,0)),map);
            human.relations.AddDirectRelation(PawnRelationDefOf.Overseer,mech);
            human.mechanitor.AssignPawnControlGroup(mech);
            human.mechanitor.Notify_BandwidthChanged();
            Require(mech.GetOverseer()==human&&human.mechanitor.ControlledPawns.Contains(mech),"systems/native overseer relation and control group established",check);
            mech.drafter.Drafted=true;
            Require(mech.Drafted,"systems/native mech is drafted before controller transforms",check);

            InspirationDef inspirationDef=DefDatabase<InspirationDef>.GetNamed("Frenzy_Go");
            Require(human.mindState.inspirationHandler.TryStartInspiration(inspirationDef,"shared systems fixture",false),"systems/native go frenzy starts before transformation",check);
            Inspiration inspiration=human.Inspiration;
            PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Warg");
            Pawn ordinary=PawnGenerator.GeneratePawn(kind,Faction.OfPlayer);
            ordinary.health.RemoveAllHediffs();
            GenSpawn.Spawn(ordinary,Open(map,human.Position+new IntVec3(-2,0,0)),map);
            float ordinarySpeed=ordinary.GetStatValue(StatDefOf.MoveSpeed);
            TransformUtility.AddOrGetShapeshifter(human,kind);
            Require(TransformUtility.TransformToAnimal(human,kind),"systems/production transformation succeeds",check);
            Pawn body=map.mapPawns.AllPawnsSpawned.Single(p=>TransformUtility.TryGetTransformedComp(p)?.StoredPawn==human);
            body.drafter.Drafted=true;
            Require(ReferenceEquals(body.psychicEntropy,human.psychicEntropy)&&body.psychicEntropy.Pawn==body,"systems/animal borrows exact source psychic tracker",check);
            check(body.GetPsylinkLevel()==6&&ReferenceEquals(body.GetMainPsylinkSource(),psylink),"systems/native active-body psylink lookup resolves original implant");
            check(body.psychicEntropy.MaxEntropy>=70f,"systems/native source gene and trait psychic enhancements retain enough animal heat capacity");
            check(body.abilities.GetAbility(skip.def)==skip&&skip.pawn==body&&skip.verb.CasterPawn==body,"systems/native psychic ability and verb use original object with active caster");
            check(ReferenceEquals(body.mindState.inspirationHandler,human.mindState.inspirationHandler)&&body.Inspiration==inspiration,"systems/original inspiration handler and state retained once");
            check(Near(body.GetStatValue(StatDefOf.MoveSpeed),ordinarySpeed*1.4f),"systems/native go-frenzy x1.4 applies to animal speed");
            int age=inspiration.Age;
            body.mindState.inspirationHandler.InspirationHandlerTickInterval(150);
            human.mindState.inspirationHandler.InspirationHandlerTickInterval(150);
            check(inspiration.Age==age+150,"systems/native inspiration duration advances once through two interfaces");
            check(ReferenceEquals(body.mechanitor,human.mechanitor)&&body.mechanitor.Pawn==human&&mech.GetOverseer()==human,"systems/active controller retains original tracker and permanent overseer relation");
            check(mech.Drafted&&body.mechanitor.CanControlMechs,"systems/transformation retains mech draft and native controller permission");
            IntVec3 close=Open(map,body.Position+new IntVec3(3,0,0));
            IntVec3 distant=body.Position+new IntVec3(30,0,0);
            check(body.mechanitor.CanCommandTo(close)&&!body.mechanitor.CanCommandTo(distant),"systems/native command range follows active animal position");

            IntVec3 destination=Open(map,target.Position+new IntVec3(3,0,0));
            if(destination==target.Position)destination=Open(map,target.Position+new IntVec3(-3,0,0));
            float focus=human.psychicEntropy.CurrentPsyfocus;
            float cost=skip.FinalPsyfocusCost(target);
            AcceptanceReport skipPermission=skip.CanCast;
            string skipReason;bool disabled=skip.GizmoDisabled(out skipReason);
            check(skipPermission,"systems/skip diagnostic CanCast="+skipPermission.Accepted+" disabled="+disabled
                +" reason="+skipReason+" psylink="+body.GetPsylinkLevel()+" sourceLink="+human.GetPsylinkLevel()
                +" focus="+body.psychicEntropy.CurrentPsyfocus+" cost="+cost+" entropy="+body.psychicEntropy.EntropyValue
                +" maxEntropy="+body.psychicEntropy.MaxEntropy+" entropyCost="+skip.def.EntropyGain
                +" targetValid="+skip.verb.ValidateTarget(target,false)+" canApply="+skip.CanApplyPsycastTo(target));
            Require(skip.CanCast&&skip.Activate(target,destination),"systems/native animal skip activation succeeds",check);
            check(target.Position==destination,"systems/native skip teleports the actual target to selected destination");
            check(Near(human.psychicEntropy.CurrentPsyfocus,focus-cost)&&Near(body.psychicEntropy.CurrentPsyfocus,human.psychicEntropy.CurrentPsyfocus),"systems/native skip spends canonical focus exactly once");
            focus=human.psychicEntropy.CurrentPsyfocus;cost=invisibility.FinalPsyfocusCost(body);
            Require(invisibility.CanCast&&invisibility.Activate(body,LocalTargetInfo.Invalid),"systems/native invisibility activation succeeds from animal",check);
            check(Near(human.psychicEntropy.CurrentPsyfocus,focus-cost),"systems/native invisibility spends original focus exactly once");
            check(human.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("PsychicInvisibility"))
                ||body.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("PsychicInvisibility")),"systems/native invisibility applies its real health effect");
            Meditation(human,body,check);
            JumpPack(human,body,jumpPack,check);

            Precept_Role role=null;
            if(human.Ideo!=null)
            {
                Ideo ideo=human.Ideo;
                role=(Precept_Role)PreceptMaker.MakePrecept(DefDatabase<PreceptDef>.GetNamed("IdeoRole_MeleeSpecialist"));
                ideo.AddPrecept(role,true);
                role.Assign(human,false);
                human.abilities.Notify_TemporaryAbilitiesChanged();
                check(ideo.GetRole(human)==role,"systems/native melee-specialist role remains assigned to original person");
                StatDef dodge=DefDatabase<StatDef>.GetNamed("MeleeDodgeChance");
                var offsets=new System.Collections.Generic.List<StatModifier>();
                var factors=new System.Collections.Generic.List<StatModifier>();
                SharedFormSystems.AppendModifiers(body,offsets,factors);
                check(offsets.Any(m=>m.stat==dodge&&Near(m.value,7f)),"systems/actual melee-specialist role contributes its original dodge modifier");
                int version=SharedFormSystems.ModifierVersion(body);
                role.Unassign(human,false);
                check(SharedFormSystems.ModifierVersion(body)!=version,"systems/role unassignment invalidates projected stat generation in the same tick");
                role.Assign(human,false);
            }

            skip.StartCooldown(1800);
            float remainingFocus=human.psychicEntropy.CurrentPsyfocus;
            int cooldown=skip.CooldownTicksRemaining;
            Require(TransformUtility.RevertToHuman(body,false)==human,"systems/return restores original controller",check);
            check(skip.pawn==human&&human.psychicEntropy.Pawn==human&&Near(human.psychicEntropy.CurrentPsyfocus,remainingFocus)
                &&skip.CooldownTicksRemaining==cooldown,"systems/return preserves original psychic focus and ability cooldown");
            check(mech.GetOverseer()==human&&mech.Drafted&&human.mechanitor.CanControlMechs,"systems/return retains native mech control and drafting");
            Require(TransformUtility.TransformToAnimal(human,kind),"systems/repeated transformation reuses canonical systems",check);
            check(map.mapPawns.AllPawnsSpawned.Contains(body)&&body.abilities.GetAbility(skip.def)==skip
                &&Near(body.psychicEntropy.CurrentPsyfocus,remainingFocus)&&skip.CooldownTicksRemaining==cooldown,"systems/repeated shift cannot refill psychic resources or reset canonical cooldown");
            check(ReferenceEquals(body.mindState.mentalBreaker,human.mindState.mentalBreaker)
                &&ReferenceEquals(body.mindState.mentalStateHandler,human.mindState.mentalStateHandler)
                &&body.mindState.mentalBreaker.CanDoRandomMentalBreaks,"systems/adult person's canonical mental controller remains eligible in animal form");
            MentalStateDef sad=DefDatabase<MentalStateDef>.GetNamed("Wander_Sad");
            Require(body.mindState.mentalStateHandler.TryStartMentalState(sad,forced:true,forceWake:true,causedByMood:true),"systems/native sad wander starts on active animal",check);
            MentalState mental=body.MentalState;
            Require(TransformUtility.RevertToHuman(body,false)==human,"systems/return during mental state restores original person",check);
            check(human.MentalState==mental&&mental.pawn==human,"systems/reversion cannot discard the original person's active mental state");
            mental.RecoverFromState();
            int recovery=(int)HarmonyLib.AccessTools.Field(typeof(MentalBreaker),"ticksUntilCanDoMentalBreak").GetValue(human.mindState.mentalBreaker);
            Require(TransformUtility.TransformToAnimal(human,kind),"systems/shift after native mental-state recovery succeeds",check);
            check((int)HarmonyLib.AccessTools.Field(typeof(MentalBreaker),"ticksUntilCanDoMentalBreak").GetValue(body.mindState.mentalBreaker)==recovery,
                "systems/mental-break recovery cooldown remains canonical across another shift");
            mech.drafter.Drafted=true;
            SharedSystemsRecord record=Current.Game.GetComponent<SharedSystemsRecord>();
            record.human=human;record.body=body;record.mech=mech;record.ability=skip;record.ready=true;
            check(record.human!=null&&record.body!=null,"systems/active psychic and mechanitor state registered for actual cross-process save");
        }

        public static void AfterLoad(Map map,Action<bool,string> check)
        {
            SharedSystemsRecord record=Current.Game.GetComponent<SharedSystemsRecord>();
            Require(record!=null&&record.ready&&record.human!=null&&record.body!=null&&record.mech!=null&&record.ability!=null,"systems/reloaded save resolves all original system references",check);
            Pawn human=record.human,body=record.body;
            check(TransformUtility.TryGetTransformedComp(body)?.StoredPawn==human
                &&ReferenceEquals(body.psychicEntropy,human.psychicEntropy)&&body.psychicEntropy.Pawn==body,"systems/reload restores one canonical psychic tracker with active-body owner");
            check(Math.Abs(human.psychicEntropy.CurrentPsyfocus-record.focus)<.003f,"systems/reload preserves actual saved psychic focus without refill");
            check(record.ability.Id==record.abilityId&&body.abilities.GetAbility(record.ability.def)==record.ability&&record.ability.pawn==body,"systems/reload preserves exact saved original psycast identity and caster");
            check(record.ability.CooldownTicksRemaining<=record.cooldown&&record.ability.CooldownTicksRemaining>=record.cooldown-90,"systems/reload preserves psychic cooldown with only actual native elapsed ticks");
            check(body.Inspiration!=null&&body.Inspiration==human.Inspiration&&body.Inspiration.Age>=record.inspirationAge
                &&body.Inspiration.Age<=record.inspirationAge+90,"systems/reload preserves one original inspiration and duration");
            check(record.mech.GetOverseer()==human&&ReferenceEquals(body.mechanitor,human.mechanitor)&&body.mechanitor.CanControlMechs,"systems/reload preserves mechanitor identity and active-body control permission");
            check(ReferenceEquals(body.mindState.mentalBreaker,human.mindState.mentalBreaker)
                &&ReferenceEquals(body.mindState.mentalStateHandler,human.mindState.mentalStateHandler),"systems/reload restores single canonical mental controllers");
            int recovery=(int)HarmonyLib.AccessTools.Field(typeof(MentalBreaker),"ticksUntilCanDoMentalBreak").GetValue(body.mindState.mentalBreaker);
            check(recovery<=record.mentalCooldown&&recovery>=record.mentalCooldown-90,"systems/reload preserves original mental-break recovery cooldown");
        }

        private static bool Near(float a,float b){return Math.Abs(a-b)<.0001f;}
        private static void JumpPack(Pawn human,Pawn body,Apparel pack,Action<bool,string> check)
        {
            CompApparelReloadable reloadable=pack.GetComp<CompApparelReloadable>();
            Require(reloadable!=null&&reloadable.RemainingCharges>0,"systems/native original jump pack starts with usable charges",check);
            Verb_Jump jump=reloadable.AllVerbs.OfType<Verb_Jump>().Single();
            body.drafter.Drafted=true;
            Command_VerbTarget command=body.GetGizmos().OfType<Command_VerbTarget>().FirstOrDefault(c=>c.verb==jump);
            Require(command!=null&&!command.Disabled&&reloadable.Wearer==body&&jump.CasterPawn==body,
                "systems/original jump pack exposes an enabled native command owned by active animal",check);
            int charges=reloadable.RemainingCharges;
            IntVec3 target=IntVec3.Invalid;
            IntVec3 orderedTarget=IntVec3.Invalid;
            if(!jumpDiagnosticsPatched)
            {
                new HarmonyLib.Harmony("wildshift.tests.jump-job-diagnostics").Patch(
                    HarmonyLib.AccessTools.Method(typeof(Pawn_JobTracker),"EndCurrentJob"),
                    prefix:new HarmonyLib.HarmonyMethod(typeof(SharedSystemsEngineChecks),nameof(JumpJobEnding)));
                jumpDiagnosticsPatched=true;
            }
            jumpDiagnosticPawn=body;lastJumpJobEnd="none";
            UnityEngine.Event oldEvent=UnityEngine.Event.current;
            try
            {
                // Native reloadable target validation reads Event.current.shift:
                // it is a UI entry point, not an engine-tick callback. Supply a
                // real non-shift input event for this headless command fixture.
                SetInputEvent(new UnityEngine.Event{modifiers=UnityEngine.EventModifiers.None});
                target=GenRadial.RadialCellsAround(body.Position,5f,false).FirstOrDefault(
                    c=>c.InBounds(body.Map)&&c.Standable(body.Map)&&jump.ValidateTarget(c,false));
                Require(target.IsValid&&target!=body.Position&&jump.ValidateTarget(target,false),"systems/native borrowed jump pack validates a real destination",check);
                jump.OrderForceTarget(target);
                if(body.CurJobDef==JobDefOf.CastJump)orderedTarget=body.CurJob.targetA.Cell;
                check(true,"systems/jump native order diagnostic "+JumpDiagnostic(body,jump,reloadable,target,orderedTarget));
                Require(orderedTarget.IsValid,"systems/native jump order supplies its actual landing destination",check);
            }
            finally{SetInputEvent(oldEvent);}
            // Native OrderJump resolves crowded cells with BestOrderedGotoDestNear.
            // Verify the actual native job destination rather than the requested
            // input cell, which need not be the same usable landing location.
            for(int i=0;i<500&&(!body.Spawned||body.Position!=orderedTarget||reloadable.RemainingCharges==charges);i++)Find.TickManager.DoSingleTick();
            Require(body.Spawned&&body.Position==orderedTarget,"systems/native borrowed jump pack job lands the actual animal "+JumpDiagnostic(body,jump,reloadable,target,orderedTarget),check);
            jumpDiagnosticPawn=null;
            check(reloadable.RemainingCharges==charges-1&&pack.holdingOwner==human.apparel.GetDirectlyHeldThings()
                &&TransformUtility.TryGetTransformedComp(body)?.StoredPawn==human,
                "systems/animal jump spends one original pack charge without changing source item or person ownership");
            check(ReloadableUtility.OwnerOf(reloadable)==body,"systems/native reload ownership resolves active actor while physical source owner stays unchanged");
            Thing fuel=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Chemfuel"));
            fuel.stackCount=20;
            GenSpawn.Spawn(fuel,Open(body.Map,body.Position+new IntVec3(1,0,0)),body.Map);
            Job reload=JobMaker.MakeJob(JobDefOf.Reload,pack);
            reload.targetQueueB=new System.Collections.Generic.List<LocalTargetInfo>{fuel};
            reload.count=20;reload.playerForced=true;
            body.jobs.TryTakeOrderedJob(reload,JobTag.Misc);
            for(int i=0;i<500&&reloadable.RemainingCharges<charges;i++)Find.TickManager.DoSingleTick();
            check(reloadable.RemainingCharges==charges&&fuel.Destroyed&&pack.holdingOwner==human.apparel.GetDirectlyHeldThings(),
                "systems/native active-animal reload consumes real map fuel and refills the same original jump pack");
        }

        public static void JumpJobEnding(Pawn ___pawn,JobCondition condition)
        {
            if(___pawn==jumpDiagnosticPawn)lastJumpJobEnd=condition+":"+(___pawn.CurJob?.def?.defName??"null")+"@"+Find.TickManager.TicksGame;
        }

        private static void SetInputEvent(UnityEngine.Event value)
        {
            // Unity 1.6 Event.current=null dereferences s_MasterEvent even when
            // the batch player has never entered IMGUI and has no master event.
            // The reloadable UI validator only reads the managed current.shift.
            // Set and restore that managed slot directly, leaving Unity's native
            // event pointer unchanged throughout this headless command scope.
            // Never pass a null event pointer into the native Unity player.
            // The 1.7 stub has no private native-event slot and accepts null.
            var current=HarmonyLib.AccessTools.Field(typeof(UnityEngine.Event),"s_Current");
            if(current!=null)current.SetValue(null,value);
            else UnityEngine.Event.current=value;
        }

        private static string JumpDiagnostic(Pawn body,Verb_Jump jump,CompApparelReloadable reloadable,IntVec3 requested,IntVec3 ordered)
        {
            var queue=new System.Collections.Generic.List<string>();
            if(body.jobs?.jobQueue!=null)
                for(int i=0;i<body.jobs.jobQueue.Count;i++)
                {
                    Job job=body.jobs.jobQueue[i].job;
                    queue.Add((job?.def?.defName??"null")+":"+job?.targetA+":"+job?.verbToUse?.CasterPawn?.ThingID);
                }
            return "requested="+requested+" ordered="+ordered+" position="+body.Position+" spawned="+body.Spawned
                +" holder="+body.ParentHolder?.GetType().FullName+" job="+body.CurJobDef?.defName
                +" driver="+body.jobs?.curDriver?.GetType().FullName+" currentTarget="+body.CurJob?.targetA
                +" caster="+jump.CasterPawn?.ThingID+" wearer="+reloadable.Wearer?.ThingID+" charges="+reloadable.RemainingCharges
                +" downed="+body.Downed+" drafted="+body.Drafted+" mental="+body.MentalStateDef?.defName
                +" stance="+body.stances?.curStance?.GetType().Name+" verbState="+jump.state
                +" lastEnd="+lastJumpJobEnd+" queue=["+string.Join(",",queue)+"]";
        }
        private static void Meditation(Pawn human,Pawn body,Action<bool,string> check)
        {
            string naturalCategory=null;
            foreach(BackstoryCategoryAndSlot rule in MeditationFocusDefOf.Natural.requiredBackstoriesAny)
                if(rule.slot==BackstorySlot.Childhood){naturalCategory=rule.categoryName;break;}
            BackstoryDef tribal=naturalCategory==null?null:DefDatabase<BackstoryDef>.AllDefsListForReading.FirstOrDefault(
                b=>b.slot==BackstorySlot.Childhood&&b.spawnCategories!=null&&b.spawnCategories.Contains(naturalCategory)
                    &&(b.workDisables&WorkTags.Violent)==WorkTags.None);
            Require(tribal!=null,"systems/official natural-focus childhood prerequisite exists",check);
            human.story.Childhood=tribal;
            MeditationFocusTypeAvailabilityCache.ClearFor(human);
            MeditationFocusTypeAvailabilityCache.ClearFor(body);
            check(body.story.Childhood!=tribal&&MeditationFocusDefOf.Natural.CanPawnUse(human)
                &&MeditationFocusDefOf.Natural.CanPawnUse(body),"systems/animal natural-focus permission follows original tribal backstory without copying body history");
            check(DefDatabase<MeditationFocusDef>.AllDefsListForReading.All(d=>d.CanPawnUse(body)==d.CanPawnUse(human)),
                "systems/all actual meditation-focus permissions match canonical backstory traits titles and implants");
            human.psychicEntropy.OffsetPsyfocusDirectly(.2f-human.psychicEntropy.CurrentPsyfocus);
            if(body.needs.food!=null)body.needs.food.CurLevelPercentage=1f;
            if(body.needs.rest!=null)body.needs.rest.CurLevelPercentage=1f;
            Require(SharedFormMeditation.Eligible(body)&&SharedFormMeditation.Order(body),"systems/manual animal meditation orders the real native job",check);
            for(int i=0;i<180&&!body.psychicEntropy.IsCurrentlyMeditating;i++)Find.TickManager.DoSingleTick();
            Require(body.CurJobDef==JobDefOf.Meditate&&body.psychicEntropy.IsCurrentlyMeditating,"systems/actual animal job reaches native meditation ticks",check);
            JobDriver_Meditate driver=body.jobs.curDriver as JobDriver_Meditate;
            Require(driver!=null,"systems/meditation uses the unmodified native job driver",check);
            float gain=MeditationUtility.PsyfocusGainPerTick(body,driver.Focus.Thing);
            float focus=human.psychicEntropy.CurrentPsyfocus;
            const int elapsed=120;
            for(int i=0;i<elapsed;i++)Find.TickManager.DoSingleTick();
            float increase=human.psychicEntropy.CurrentPsyfocus-focus;
            check(increase>0f&&ReferenceEquals(body.psychicEntropy,human.psychicEntropy),"systems/native animal meditation raises the one original psyfocus pool");
            check(Math.Abs(increase-gain*elapsed)<Math.Max(.00003f,gain*elapsed*.05f),
                "systems/native meditation psyfocus gain occurs once per tick with canonical genetic bonus");
            check(body.GetStatValue(StatDefOf.MeditationFocusGain)>=human.GetStatValue(StatDefOf.MeditationFocusGain)-.0001f,
                "systems/canonical psychic gene meditation-focus gain remains present on animal");
            body.jobs.StopAll();
            body.drafter.Drafted=true;
        }
        private static Pawn Human(Map map,string name,IntVec3 cell)
        {
            Pawn pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
                forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,
                fixedBiologicalAge:25f,fixedChronologicalAge:25f,forcedXenotype:XenotypeDefOf.Baseliner));
            pawn.Name=new NameSingle(name);pawn.health.RemoveAllHediffs();
            foreach(Trait trait in pawn.story.traits.allTraits.ToList())pawn.story.traits.RemoveTrait(trait);
            foreach(Gene gene in pawn.genes.GenesListForReading.ToList())pawn.genes.RemoveGene(gene);
            GenSpawn.Spawn(pawn,Open(map,cell),map);return pawn;
        }
        private static IntVec3 Open(Map map,IntVec3 requested)
        {return requested.InBounds(map)&&requested.Standable(map)?requested:CellFinder.RandomClosewalkCellNear(map.Center,map,8);}
        private static void Require(bool passed,string label,Action<bool,string> check)
        {check(passed,label);if(!passed)throw new InvalidOperationException(label);}
    }

    public sealed class SharedSystemsRecord : GameComponent
    {
        public Pawn human,body,mech;
        public Ability ability;
        public bool ready;
        public float focus;
        public int cooldown,inspirationAge,abilityId,mentalCooldown;
        public SharedSystemsRecord(Game game){}
        public override void ExposeData()
        {
            if(Scribe.mode==LoadSaveMode.Saving&&ready)
            {focus=human.psychicEntropy.CurrentPsyfocus;cooldown=ability.CooldownTicksRemaining;inspirationAge=human.Inspiration?.Age??0;abilityId=ability.Id;
                mentalCooldown=(int)HarmonyLib.AccessTools.Field(typeof(MentalBreaker),"ticksUntilCanDoMentalBreak").GetValue(human.mindState.mentalBreaker);}
            Scribe_References.Look(ref human,"sharedSystemsHuman");Scribe_References.Look(ref body,"sharedSystemsBody");
            Scribe_References.Look(ref mech,"sharedSystemsMech");Scribe_References.Look(ref ability,"sharedSystemsAbility");
            Scribe_Values.Look(ref ready,"sharedSystemsReady");Scribe_Values.Look(ref focus,"sharedSystemsFocus");
            Scribe_Values.Look(ref cooldown,"sharedSystemsCooldown");Scribe_Values.Look(ref inspirationAge,"sharedSystemsInspirationAge");
            Scribe_Values.Look(ref abilityId,"sharedSystemsAbilityId");
            Scribe_Values.Look(ref mentalCooldown,"sharedSystemsMentalCooldown");
        }
    }
}

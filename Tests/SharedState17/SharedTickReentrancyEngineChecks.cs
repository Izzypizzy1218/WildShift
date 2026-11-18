using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY: supported opt-in callbacks use actual production transforms.
    public sealed class ReturnDuringTickGene : Gene
    {
        public static Gene Watched;
        public static Pawn ExpectedBody, Returned;
        public static int Calls;
        public override void Tick()
        {
            if (!ReferenceEquals(this, Watched) || !ReferenceEquals(pawn, ExpectedBody)) return;
            Calls++;
            Returned = TransformUtility.RevertToHuman(pawn, false);
        }
    }
    public sealed class TrailingTickGene : Gene
    {
        public static Gene Watched;
        public static Pawn ExpectedBody;
        public static int Calls, InactiveBodyCalls;
        public override void Tick()
        {
            if (!ReferenceEquals(this, Watched) || !ReferenceEquals(pawn, ExpectedBody)) return;
            Calls++;
            if (SharedFormIdentity.HumanFor(pawn) == null) InactiveBodyCalls++;
        }
    }
    public static class SharedTickReentrancyEngineChecks
    {
        public static void Run(Map map, Action<bool,string> check)
        {
            Pawn human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
                forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,
                forcedXenotype:XenotypeDefOf.Baseliner));
            human.health.RemoveAllHediffs();
            foreach(Gene gene in human.genes.GenesListForReading.ToList())human.genes.RemoveGene(gene);
            human.Name = new NameSingle("CallbackReentryOriginal");
            string name = human.Name.ToStringFull;
            GenSpawn.Spawn(human,CellFinder.StandableCellNear(map.Center,map,12f),map);
            Pawn body = null;
            try
            {
                Gene returning = human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("WildShift_TestReturnDuringTick"),false);
                Gene trailing = human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("WildShift_TestTrailingTick"),false);
                Require(returning.Active && trailing.Active
                    && SharedFormBiology.CanRunCallback(returning,false) && SharedFormBiology.CanRunCallback(trailing,false),
                    "callback-reentry/both native Gene objects have the public reviewed-callback opt-in",check);
                Require(human.genes.GenesListForReading.IndexOf(returning)<human.genes.GenesListForReading.IndexOf(trailing),
                    "callback-reentry/native gene order runs return before trailing callback",check);
                PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Warg");
                TransformUtility.AddOrGetShapeshifter(human,kind);
                Require(TransformUtility.TransformToAnimal(human,kind),"callback-reentry/real transformation succeeds",check);
                body=SharedFormIdentity.ActiveFormFor(human);
                Require(body!=null && body.Spawned,"callback-reentry/actual active body holds original",check);
                body.drafter.Drafted=true;
                body.pather.StopDead();
                IntVec3 position=body.Position;
                ReturnDuringTickGene.Watched=returning;ReturnDuringTickGene.ExpectedBody=body;
                ReturnDuringTickGene.Calls=TrailingTickGene.Calls=TrailingTickGene.InactiveBodyCalls=0;
                ReturnDuringTickGene.Returned=null;TrailingTickGene.Watched=trailing;TrailingTickGene.ExpectedBody=body;
                var nativeTick=AccessTools.Method(typeof(Pawn_GeneTracker),"GeneTrackerTick",Type.EmptyTypes);
                Require(nativeTick!=null,"callback-reentry/actual native gene tracker tick entry point exists",check);
                nativeTick.Invoke(body.genes,null);
                // A native spawn callback may already have consumed this tick.
                // In that case use the next real native tick, never mutate the
                // production clock or supply a mocked identity/tick result.
                if(ReturnDuringTickGene.Calls==0)Find.TickManager.DoSingleTick();
                check(ReturnDuringTickGene.Calls==1 && ReturnDuringTickGene.Returned==human && human.Spawned
                    && human.Map==map && human.Position==position && human.Name.ToStringFull==name
                    && SharedFormIdentity.HumanFor(body)==null && AnimalFormStorage.IsStored(body),
                    "callback-reentry/actual opt-in Gene.Tick returns the same original and caches the exact animal");
                check(TrailingTickGene.Calls==0 && TrailingTickGene.InactiveBodyCalls==0,
                    "callback-reentry/no later callback executes against a body released earlier in this tick"
                    +" [calls="+TrailingTickGene.Calls+", inactive="+TrailingTickGene.InactiveBodyCalls+"]");
                check(returning.pawn==human && trailing.pawn==human,
                    "callback-reentry/both canonical gene owners restore even when a callback returns the form");
            }
            finally
            {
                ReturnDuringTickGene.Watched=null;ReturnDuringTickGene.ExpectedBody=null;ReturnDuringTickGene.Returned=null;
                TrailingTickGene.Watched=null;TrailingTickGene.ExpectedBody=null;
                if(body!=null && SharedFormIdentity.HumanFor(body)==human)TransformUtility.RevertToHuman(body,false);
                if(!human.Destroyed)human.Destroy(DestroyMode.Vanish);
            }
        }
        private static void Require(bool ok,string label,Action<bool,string> check)
        {check(ok,label);if(!ok)throw new InvalidOperationException(label);}
    }
}

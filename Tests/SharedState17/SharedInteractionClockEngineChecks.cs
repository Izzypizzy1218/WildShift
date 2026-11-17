using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. A reviewed opt-in callback observes the production forwarding
    // path; it does not replace native ticking or return a gameplay result.
    public sealed class InteractionClockGene : Gene
    {
        public static Gene Watched;
        public static Pawn ExpectedBody;
        public static int Calls, Delta;
        public static bool OwnersCorrect = true;
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!ReferenceEquals(this,Watched)) return;
            Calls++; Delta += delta; OwnersCorrect &= pawn == ExpectedBody;
        }
    }

    public static class SharedInteractionClockEngineChecks
    {
        private static Pawn watched;
        private static Pawn_InteractionsTracker tracker;
        private static int nativeIntervals, nativeDelta, socialIntervals;
        public static void ObservePawn(Pawn __instance,int delta)
        { if (__instance==watched) { nativeIntervals++; nativeDelta += delta; } }
        public static void ObserveSocial(Pawn_InteractionsTracker __instance)
        { if (__instance==tracker) socialIntervals++; }

        public static void Run(Map map,Action<bool,string> check)
        {
            Pawn human = Human(map,"SharedInteractionClock");
            Pawn companion = Human(map,"SharedInteractionCompanion");
            Pawn body = null; Caravan caravan = null;
            var observer = new Harmony("wildshift.tests.interaction-clock-observer");
            var pawnInterval = AccessTools.DeclaredMethod(typeof(Pawn),"TickInterval",new[] { typeof(int) });
            var socialInterval = AccessTools.Method(typeof(Pawn_InteractionsTracker),"InteractionsTrackerTickInterval");
            observer.Patch(pawnInterval,prefix:new HarmonyMethod(typeof(SharedInteractionClockEngineChecks),nameof(ObservePawn)));
            observer.Patch(socialInterval,prefix:new HarmonyMethod(typeof(SharedInteractionClockEngineChecks),nameof(ObserveSocial)));
            try
            {
                Gene gene = human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("WildShift_TestInteractionClockGene"),false);
                Pawn_InteractionsTracker originalTracker = human.interactions;
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed("Warg");
                TransformUtility.AddOrGetShapeshifter(human,kind);
                Require(TransformUtility.TransformToAnimal(human,kind),"interaction-clock/real transform succeeds",check);
                body = SharedFormIdentity.ActiveFormFor(human);
                Require(body!=null && body.Spawned && originalTracker!=null && human.interactions==null
                    && body.interactions!=null && body.interactions!=originalTracker
                    && TrackerOwner(body.interactions)==body,
                    "interaction-clock/native transform removes the despawned human tracker and creates a map-body tracker"
                    +" [body="+(body?.GetUniqueLoadID()??"null")+", spawned="+(body?.Spawned.ToString()??"null")
                    +", originalTracker="+TrackerState(originalTracker)+", humanTracker="+TrackerState(human.interactions)
                    +", bodyTracker="+TrackerState(body?.interactions)+"]",check);
                // Labeled legacy input: retain the native-created source
                // tracker that a loaded/third-party pawn may already expose.
                // Production binding owns the actual forwarding/owner change;
                // no tick method or gameplay outcome is replaced by the test.
                human.interactions = originalTracker;
                SharedFormRuntime.Initialize(body);
                Require(body.interactions==originalTracker && human.interactions==originalTracker
                    && TrackerOwner(originalTracker)==body,
                    "interaction-clock/legacy native source tracker is borrowed by real production binding on the map",check);
                watched = body; tracker = originalTracker;
                InteractionClockGene.Watched = gene; InteractionClockGene.ExpectedBody = body;
                body.drafter.Drafted = true;
                Measure(human,body,gene,true,check);

                body.drafter.Drafted = false;
                caravan = CaravanExitMapUtility.ExitMapAndCreateCaravan(new[] { companion,body },Faction.OfPlayer,
                    map.Tile,map.Tile,PlanetTile.Invalid,false);
                Require(caravan!=null && caravan.Spawned && caravan.ContainsPawn(body) && !body.Spawned
                    && body.Map==null && WorldPawnsUtility.IsWorldPawn(body),
                    "interaction-clock/native departure creates a real off-map world/caravan form",check);
                Require(body.interactions==null && human.interactions==originalTracker,
                    "interaction-clock/native caravan departure removes the dynamic body tracker and preserves the source input",check);
                SharedFormRuntime.Initialize(body);
                Require(body.interactions==originalTracker && human.interactions==originalTracker
                    && TrackerOwner(originalTracker)==body && TrackerOwner(originalTracker).Map==null,
                    "interaction-clock/production binding restores the legacy source tracker on the real off-map form",check);
                // Deterministic ready inputs for the historical failing path.
                // The corrected native map-only clock never consumes them.
                AccessTools.Field(typeof(Pawn_InteractionsTracker),"wantsRandomInteract").SetValue(tracker,true);
                AccessTools.Field(typeof(Pawn_InteractionsTracker),"lastInteractionTime").SetValue(tracker,-9999999);
                Measure(human,body,gene,false,check);
            }
            finally
            {
                watched = null; tracker = null; InteractionClockGene.Watched = null; InteractionClockGene.ExpectedBody = null;
                observer.Unpatch(pawnInterval,HarmonyPatchType.All,observer.Id);
                observer.Unpatch(socialInterval,HarmonyPatchType.All,observer.Id);
                if (body!=null && SharedFormIdentity.HumanFor(body)==human) TransformUtility.RevertToHuman(body,false);
                if (caravan!=null && caravan.Spawned)
                {
                    foreach (Pawn pawn in caravan.PawnsListForReading.ToList()) caravan.RemovePawn(pawn);
                    if (caravan.Spawned) caravan.Destroy();
                }
                if (!human.Destroyed) human.Destroy(DestroyMode.Vanish);
                if (!companion.Destroyed) companion.Destroy(DestroyMode.Vanish);
            }
        }

        private static void Measure(Pawn human,Pawn body,Gene gene,bool onMap,Action<bool,string> check)
        {
            string place = onMap ? "map" : "off-map caravan/world";
            nativeIntervals = nativeDelta = socialIntervals = 0;
            InteractionClockGene.Calls = InteractionClockGene.Delta = 0; InteractionClockGene.OwnersCorrect = true;
            human.needs.food.CurLevelPercentage = .8f;
            float food = human.needs.food.CurLevel;
            long age = human.ageTracker.AgeBiologicalTicks;
            int errors = Log.Messages.Count(message=>message.type==LogMessageType.Error);
            for (int i=0;i<300;i++) Find.TickManager.DoSingleTick();
            check(nativeIntervals>0 && socialIntervals==(onMap?nativeIntervals:0),
                "interaction-clock/"+place+" native interval count="+nativeIntervals+", social interval count="+socialIntervals
                +(onMap?" (exactly once per native interval)":" (map-only social clock stays stopped)"));
            check(InteractionClockGene.Calls==nativeIntervals && InteractionClockGene.Delta==nativeDelta
                && InteractionClockGene.OwnersCorrect && gene.pawn==human,
                "interaction-clock/"+place+" forwards reviewed gene callbacks once with temporary body ownership and restores canonical owner");
            long aged = human.ageTracker.AgeBiologicalTicks-age;
            check(aged==nativeDelta && aged>0,
                "interaction-clock/"+place+" canonical age continues once (age="+aged+", interval delta="+nativeDelta+")");
            check(human.needs.food==body.needs.food && human.needs.food.CurLevel<food,
                "interaction-clock/"+place+" canonical shared food need continues to decay");
            check(Log.Messages.Count(message=>message.type==LogMessageType.Error)==errors,
                "interaction-clock/"+place+" native ticking produces no runtime errors");
        }

        private static Pawn Human(Map map,string name)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
                forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,
                fixedGender:Gender.Male,fixedBiologicalAge:25f,forcedXenotype:XenotypeDefOf.Baseliner));
            pawn.Name = new NameSingle(name); pawn.health.RemoveAllHediffs();
            foreach (Gene gene in pawn.genes.GenesListForReading.ToList()) pawn.genes.RemoveGene(gene);
            GenSpawn.Spawn(pawn,CellFinder.StandableCellNear(map.Center,map,12f),map);
            return pawn;
        }
        private static void Require(bool value,string label,Action<bool,string> check)
        { check(value,label); if(!value)throw new InvalidOperationException(label); }
        private static string TrackerState(Pawn_InteractionsTracker value)
        {
            if(value==null)return "null";
            Pawn owner=TrackerOwner(value);
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value)+"@"+(owner?.GetUniqueLoadID()??"null");
        }
        private static Pawn TrackerOwner(Pawn_InteractionsTracker value)
        { return value==null?null:AccessTools.Field(typeof(Pawn_InteractionsTracker),"pawn").GetValue(value) as Pawn; }
    }
}

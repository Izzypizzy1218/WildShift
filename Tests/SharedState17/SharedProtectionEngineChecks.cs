using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY: native designation, job, execution and transaction paths.
    // No gameplay method is replaced; temporary policy changes are restored.
    public static class SharedProtectionEngineChecks
    {
        public static void Run(Map map, Action<bool,string> check)
        {
            bool slaughter=WildShiftMod.Settings.allowTransformedSlaughter;
            bool sale=WildShiftMod.Settings.allowTransformedSale;
            float revenge=WildShiftMod.Settings.soldRevengeChance;
            float death=WildShiftMod.Settings.deathChance;
            var people=new List<Pawn>();
            var animals=new List<Pawn>();
            try
            {
                var defaults=new WildShiftSettings();
                check(defaults.allowTransformedSlaughter&&defaults.allowTransformedSale,
                    "protection/new settings default to allowing both existing actions");
                defaults.allowTransformedSlaughter=defaults.allowTransformedSale=false;
                defaults.Reset();
                check(defaults.allowTransformedSlaughter&&defaults.allowTransformedSale,
                    "protection/reset restores both independently saved allow options");
                Pawn human=Human(map,"ProtectionOriginal"),worker=Human(map,"ProtectionWorker");
                people.Add(human);people.Add(worker);
                Pawn body=Shift(map,human);
                Pawn ordinary=Animal(map);animals.Add(ordinary);
                Set(true,true);
                check(!TransformedAnimalProtection.SlaughterBlocked(body)&&!TransformedAnimalProtection.SaleBlocked(body),
                    "protection/default policy preserves transformed animal slaughter and sale eligibility");
                Set(false,true);
                check(TransformedAnimalProtection.SlaughterBlocked(body)&&!TransformedAnimalProtection.SaleBlocked(body),
                    "protection/slaughter toggle does not disable sales");
                Set(true,false);
                check(!TransformedAnimalProtection.SlaughterBlocked(body)&&TransformedAnimalProtection.SaleBlocked(body),
                    "protection/sale toggle does not disable slaughter");
                Set(false,false);
                check(!TransformedAnimalProtection.SlaughterBlocked(ordinary)&&!TransformedAnimalProtection.SaleBlocked(ordinary),
                    "protection/ordinary livestock remains outside both protections");
                ordinary.health.AddHediff(WildShiftDefOf.WildShift_FieldShapeshifterMarker);
                check(!TransformedAnimalProtection.SlaughterBlocked(ordinary)&&!TransformedAnimalProtection.SaleBlocked(ordinary),
                    "protection/latent unawakened animals without a stored original remain ordinary animals");
                Faction other=Find.FactionManager.AllFactionsListForReading.First(f=>!f.IsPlayer&&f.def.humanlikeFaction);
                body.SetFaction(other);
                check(!TransformedAnimalProtection.SlaughterBlocked(body)&&!TransformedAnimalProtection.SaleBlocked(body),
                    "protection/enemy-owned transformed bodies do not gain player protection");
                body.SetFaction(Faction.OfPlayer);
                human.SetFaction(other);
                check(!TransformedAnimalProtection.SlaughterBlocked(body)&&!TransformedAnimalProtection.SaleBlocked(body),
                    "protection/a player-owned body containing an enemy original is not classified as a player person");
                human.SetFaction(Faction.OfPlayer);
                Hediff anesthetic=body.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Anesthetic"));
                SharedFormHealth.Refresh(body);
                Require(body.Downed,"protection/native anesthetic creates a downed transformed test subject",check);
                check(TransformedAnimalProtection.SlaughterBlocked(body)&&TransformedAnimalProtection.SaleBlocked(body),
                    "protection/downed player persons remain protected despite loss of direct control");
                anesthetic.pawn.health.RemoveHediff(anesthetic);SharedFormHealth.Refresh(body);
                Require(!body.Downed,"protection/test subject recovers after removing native anesthetic",check);
                Require(body.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("Wander_Sad"),
                    forced:true,forceWake:true,causedByMood:true),"protection/native mental state starts on transformed person",check);
                check(TransformedAnimalProtection.SlaughterBlocked(body)&&TransformedAnimalProtection.SaleBlocked(body),
                    "protection/mental-state player persons remain protected");
                body.MentalState.RecoverFromState();
                Slaughter(map,human,body,worker,ordinary,check);
                Sales(map,human,body,worker,ordinary,other,check);
                // A successful allowed sale still performs the original revenge roll.
                Set(true,true);WildShiftMod.Settings.soldRevengeChance=1f;
                int before=GameComponent_SoldShapeshifterRevenge.Current.PendingCount;
                var ship=new TradeShip(TraderKind(body),other);
                ship.GiveSoldThingToTrader(body,1,worker);
                check(GameComponent_SoldShapeshifterRevenge.Current.PendingCount==before+1
                    &&TransformUtility.TryGetShapeshifterComp(human).saleRevengeRolled
                    &&ship.GetDirectlyHeldThings().Contains(body),
                    "protection/allowed native sale preserves original person ownership and existing revenge scheduling");
                GameComponent_SoldShapeshifterRevenge.Current.CancelFor(body);
                ship.GetDirectlyHeldThings().Remove(body);body.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(body,CellFinder.StandableCellNear(map.Center,map,10f),map);
                Require(TransformUtility.RevertToHuman(body,false)==human,
                    "protection/allowed-sale test can return its exact original after controlled recovery",check);
                Pawn doomed=Human(map,"ProtectionAllowedSlaughter");people.Add(doomed);
                Pawn doomedBody=Shift(map,doomed);
                WildShiftMod.Settings.deathChance=0f;
                Job nativeJob=JobMaker.MakeJob(JobDefOf.Slaughter,doomedBody);nativeJob.ignoreDesignations=true;
                worker.drafter.Drafted=false;worker.jobs.TryTakeOrderedJob(nativeJob,JobTag.Misc);
                Require(worker.CurJobDef==JobDefOf.Slaughter,"protection/allowed final-cut probe runs in a real native slaughter job",check);
                ExecutionUtility.DoExecutionByCut(worker,doomedBody,0,false);
                check(doomed.Dead,"protection/allowing slaughter retains guaranteed original-human death even when random death chance is zero");
                worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            finally
            {
                WildShiftMod.Settings.allowTransformedSlaughter=slaughter;
                WildShiftMod.Settings.allowTransformedSale=sale;
                WildShiftMod.Settings.soldRevengeChance=revenge;
                WildShiftMod.Settings.deathChance=death;
                TransformedAnimalProtection.SettingsChanged();
                foreach(Pawn p in people)Cleanup(p);
                foreach(Pawn p in animals)if(!p.Destroyed)p.Destroy(DestroyMode.Vanish);
            }
        }

        private static void Slaughter(Map map,Pawn human,Pawn body,Pawn worker,Pawn ordinary,Action<bool,string> check)
        {
            Set(false,false);
            var designator=new Designator_Slaughter();
            check(!designator.CanDesignateThing(body).Accepted&&designator.CanDesignateThing(ordinary).Accepted,
                "protection/native slaughter designator rejects the person and accepts ordinary livestock");
            var column=new PawnColumnWorker_Slaughter();
            var checkbox=AccessTools.Method(typeof(PawnColumnWorker_Slaughter),"HasCheckbox");
            check(!(bool)checkbox.Invoke(column,new object[]{body})&&(bool)checkbox.Invoke(column,new object[]{ordinary}),
                "protection/native animal-table slaughter checkbox hides only protected persons");
            map.designationManager.AddDesignation(new Designation(body,DesignationDefOf.Slaughter));
            check(map.designationManager.DesignationOn(body,DesignationDefOf.Slaughter)==null,
                "protection/direct native designation insertion cannot bypass disabled slaughter");
            check(!body.ShouldBeSlaughtered()&&!new WorkGiver_Slaughter().HasJobOnThing(worker,body,true)
                &&!AutoSlaughterManager.CanEverAutoSlaughter(body),
                "protection/native work and automatic slaughter eligibility reject the protected person");
            // Reserve every other same-faction animal through the real native
            // manager so FindAnimal has one deterministic reachable candidate.
            // No ticks or gameplay replacements run while reservations exist.
            Job reservation=JobMaker.MakeJob(JobDefOf.Wait);
            try
            {
                foreach(Pawn animal in map.mapPawns.AllPawnsSpawned.Where(p=>p.IsAnimal&&p.Faction==worker.Faction&&p!=body).ToList())
                    Require(map.reservationManager.Reserve(body,reservation,animal,errorOnFailed:false,ignoreOtherReservations:true),
                        "protection/native reservation isolates mental-slaughter candidate "+animal.ThingID,check);
                Set(true,false);
                Require(SlaughtererMentalStateUtility.FindAnimal(worker)==body,
                    "protection/native mental-slaughter finder reaches the single allowed transformed person",check);
                Set(false,false);
                check(SlaughtererMentalStateUtility.FindAnimal(worker)==null,
                    "protection/native mental-slaughter finder excludes the protected person before creating a job");
                map.reservationManager.Release(ordinary,body,reservation);
                check(SlaughtererMentalStateUtility.FindAnimal(worker)==ordinary,
                    "protection/native mental-slaughter finder retains ordinary livestock as a valid target");
            }
            finally{map.reservationManager.ReleaseClaimedBy(body,reservation);Set(false,false);}
            List<AutoSlaughterConfig> previous=map.autoSlaughterManager.configs;
            try
            {
                map.autoSlaughterManager.configs=new List<AutoSlaughterConfig>{new AutoSlaughterConfig
                    {animal=body.def,maxTotal=0,allowSlaughterBonded=true,allowSlaughterPregnant=true}};
                Set(true,false);
                check(map.autoSlaughterManager.AnimalsToSlaughter.Contains(body),
                    "protection/native automatic-slaughter cache contains an allowed test person");
                map.designationManager.AddDesignation(new Designation(body,DesignationDefOf.Slaughter));
                Set(false,false);
                check(!map.autoSlaughterManager.AnimalsToSlaughter.Contains(body)
                    &&map.designationManager.DesignationOn(body,DesignationDefOf.Slaughter)==null,
                    "protection/settings change clears cached automatic targets and old manual designations");
            }
            finally{map.autoSlaughterManager.configs=previous;map.autoSlaughterManager.Notify_ConfigChanged();}
            Set(true,false);
            Job queued=JobMaker.MakeJob(JobDefOf.Slaughter,body);queued.ignoreDesignations=true;
            worker.drafter.Drafted=false;worker.jobs.TryTakeOrderedJob(queued,JobTag.Misc);
            Require(worker.CurJobDef==JobDefOf.Slaughter,"protection/native ignore-designations slaughter job starts while allowed",check);
            // Deliberately bypass settings resynchronization to exercise the
            // last cut guard, then let the same queued native job abort itself.
            WildShiftMod.Settings.allowTransformedSlaughter=false;
            ExecutionUtility.DoExecutionByCut(worker,body,0,false);
            check(!body.Dead&&!human.Dead&&SharedFormIdentity.HumanFor(body)==human,
                "protection/final native slaughter cut cannot kill either body after the policy changes");
            for(int i=0;i<30;i++)Find.TickManager.DoSingleTick();
            check(!body.Dead&&!human.Dead&&worker.CurJobDef!=JobDefOf.Slaughter,
                "protection/already-started ignore-designations slaughter job aborts when disabled");
            worker.jobs.StopAll();worker.drafter.Drafted=true;
            TransformedAnimalProtection.SettingsChanged();
        }

        private static void Sales(Map map,Pawn human,Pawn body,Pawn worker,Pawn ordinary,Faction faction,Action<bool,string> check)
        {
            ITrader savedTrader=TradeSession.trader;Pawn savedNegotiator=TradeSession.playerNegotiator;
            TradeDeal savedDeal=TradeSession.deal;bool savedGift=TradeSession.giftMode;
            var trader=new RecordingTrader(faction,TraderKind(body));
            Thing money=ThingMaker.MakeThing(ThingDefOf.Silver);money.stackCount=100;
            Thing stock=ThingMaker.MakeThing(ThingDefOf.Silver);stock.stackCount=1000;
            trader.colony.Add(body);trader.colony.Add(money);trader.stock.Add(stock);
            try
            {
                GenSpawn.Spawn(money,CellFinder.StandableCellNear(map.Center,map,10f),map);
                Set(true,true);TradeSession.SetupWith(trader,worker,false);
                TradeDeal deal=TradeSession.deal;
                Tradeable row=deal.AllTradeables.First(t=>t.thingsColony.Contains(body));
                row.ForceToDestination(1);
                deal.UpdateCurrencyCount();
                Require(deal.CurrencyTradeable!=null&&deal.CurrencyTradeable.thingsColony.Contains(money)
                    &&deal.CurrencyTradeable.thingsTrader.Contains(stock)&&deal.CurrencyTradeable.CountToTransfer!=0,
                    "protection/native stale-deal fixture contains both real silver sides and a nonzero payment",check);
                check(TradeUtility.PlayerSellableNow(body,trader)&&row.ActionToDo==TradeAction.PlayerSells,
                    "protection/allowed native trade setup offers the transformed person for sale");
                int count=row.CountToTransfer;int beforePending=GameComponent_SoldShapeshifterRevenge.Current.PendingCount;
                Set(true,false);
                check(!TradeUtility.PlayerSellableNow(body,trader)&&TradeUtility.PlayerSellableNow(ordinary,trader),
                    "protection/native player-sellable filter excludes only the protected transformed person");
                bool traded=true;
                bool accepted=deal.TryExecute(out traded);
                check(!accepted&&!traded&&trader.callbacks==0&&money.stackCount==100&&stock.stackCount==1000
                    &&row.CountToTransfer==count&&body.Spawned&&body.Faction==Faction.OfPlayer
                    &&GameComponent_SoldShapeshifterRevenge.Current.PendingCount==beforePending,
                    "protection/stale native sale rejects atomically before silver, items, ownership or revenge changes");
                int goodwill=Faction.OfPlayer.GoodwillWith(faction);
                TradeSession.giftMode=true;row.ForceToDestination(1);
                accepted=deal.TryExecute(out traded);
                check(!accepted&&!traded&&trader.callbacks==0&&Faction.OfPlayer.GoodwillWith(faction)==goodwill,
                    "protection/native gift-mode stale selection cannot bypass sale protection or change goodwill");
                TradeSession.giftMode=false;
                // Match native first-N pawn selection and forward stack-count
                // selection, rather than rejecting an unsold row member.
                row.thingsColony.Clear();row.thingsColony.Add(ordinary);row.thingsColony.Add(body);row.ForceToDestination(1);
                check(TransformedAnimalSaleProtection.FirstBlockedSale(deal)==null,
                    "protection/unsold protected pawn behind a selected ordinary pawn does not veto its sale");
                row.ForceToDestination(2);
                check(TransformedAnimalSaleProtection.FirstBlockedSale(deal)==body,
                    "protection/native first-N selection detects the protected pawn when actually selected");
                deal.AllTradeables.Clear();var mixed=new Tradeable();
                mixed.thingsColony.Add(money);mixed.thingsColony.Add(body);deal.AllTradeables.Add(mixed);
                mixed.ForceToDestination(100);
                check(TransformedAnimalSaleProtection.FirstBlockedSale(deal)==null,
                    "protection/generic native stack selection consumes preceding stack counts correctly");
                mixed.ForceToDestination(101);
                check(TransformedAnimalSaleProtection.FirstBlockedSale(deal)==body,
                    "protection/generic native stack selection blocks the following selected person");
                deal.AllTradeables.Clear();var buying=new Tradeable_Pawn();buying.thingsTrader.Add(body);
                buying.ForceToSource(1);deal.AllTradeables.Add(buying);
                check(buying.ActionToDo==TradeAction.PlayerBuys&&TransformedAnimalSaleProtection.FirstBlockedSale(deal)==null,
                    "protection/purchase direction remains available when selling is disabled");
                new Pawn_TraderTracker(null).GiveSoldThingToTrader(body,1,worker);
                new TradeShip().GiveSoldThingToTrader(body,1,worker);
                new Settlement_TraderTracker(null).GiveSoldThingToTrader(body,1,worker);
                new Caravan_TraderTracker(null).GiveSoldThingToTrader(body,1,worker);
                check(body.Spawned&&body.Faction==Faction.OfPlayer&&SharedFormIdentity.HumanFor(body)==human
                    &&GameComponent_SoldShapeshifterRevenge.Current.PendingCount==beforePending,
                    "protection/all four direct native trader handoffs stop before native owner/stock mutation");
                Set(true,true);
                check(TradeUtility.PlayerSellableNow(body,trader)&&TransformedAnimalSaleProtection.FirstBlockedSale(deal)==null,
                    "protection/re-enabling sales immediately restores native eligibility");
            }
            finally
            {
                TradeSession.trader=savedTrader;TradeSession.playerNegotiator=savedNegotiator;
                TradeSession.deal=savedDeal;TradeSession.giftMode=savedGift;
                if(!money.Destroyed)money.Destroy(DestroyMode.Vanish);if(!stock.Destroyed)stock.Destroy(DestroyMode.Vanish);
            }
        }

        private sealed class RecordingTrader : ITrader
        {
            public readonly List<Thing> colony=new List<Thing>(),stock=new List<Thing>();
            public int callbacks;private readonly Faction faction;private readonly TraderKindDef kind;
            public RecordingTrader(Faction faction,TraderKindDef kind){this.faction=faction;this.kind=kind;}
            public TraderKindDef TraderKind=>kind;public IEnumerable<Thing> Goods=>stock;
            public int RandomPriceFactorSeed=>13579;public string TraderName=>"Protection test trader";
            public bool CanTradeNow=>true;public float TradePriceImprovementOffsetForPlayer=>0f;
            public Faction Faction=>faction;public TradeCurrency TradeCurrency=>TradeCurrency.Silver;
            public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn negotiator){return colony;}
            public void GiveSoldThingToTrader(Thing thing,int count,Pawn negotiator){callbacks++;}
            public void GiveSoldThingToPlayer(Thing thing,int count,Pawn negotiator){callbacks++;}
        }
        private static TraderKindDef TraderKind(Pawn body)
        {return DefDatabase<TraderKindDef>.AllDefsListForReading.First(t=>t.WillTrade(body.def)&&t.tradeCurrency==TradeCurrency.Silver);}
        private static void Set(bool slaughter,bool sale)
        {WildShiftMod.Settings.allowTransformedSlaughter=slaughter;WildShiftMod.Settings.allowTransformedSale=sale;TransformedAnimalProtection.SettingsChanged();}
        private static Pawn Human(Map map,string name)
        {
            Pawn p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
                forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,
                fixedGender:Gender.Male,fixedBiologicalAge:25f,forcedXenotype:XenotypeDefOf.Baseliner,
                validatorPostGear: pawn => pawn.skills?.GetSkill(SkillDefOf.Social)?.TotallyDisabled == false));
            p.Name=new NameSingle(name);p.health.RemoveAllHediffs();
            foreach(Trait t in p.story.traits.allTraits.ToList())p.story.traits.RemoveTrait(t);
            foreach(Gene g in p.genes.GenesListForReading.ToList())p.genes.RemoveGene(g);
            foreach(Apparel a in p.apparel.WornApparel.ToList())p.apparel.Remove(a);
            p.equipment.DestroyAllEquipment();
            GenSpawn.Spawn(p,CellFinder.StandableCellNear(map.Center,map,10f),map);
            p.drafter.Drafted=true;return p;
        }
        private static Pawn Shift(Map map,Pawn human)
        {
            PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Warg");
            TransformUtility.AddOrGetShapeshifter(human,kind);
            if(!TransformUtility.TransformToAnimal(human,kind))throw new InvalidOperationException("Protection native transformation failed");
            return map.mapPawns.AllPawnsSpawned.Single(p=>TransformUtility.TryGetTransformedComp(p)?.StoredPawn==human);
        }
        private static Pawn Animal(Map map)
        {
            Pawn p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Warg"),Faction.OfPlayer);
            p.health.RemoveAllHediffs();GenSpawn.Spawn(p,CellFinder.StandableCellNear(map.Center,map,10f),map);return p;
        }
        private static void Cleanup(Pawn human)
        {
            if(human==null||human.Destroyed)return;
            Pawn body=SharedFormIdentity.ActiveFormFor(human);
            if(body!=null)TransformUtility.RevertToHuman(body,false);
            if(human.Corpse!=null&&!human.Corpse.Destroyed)human.Corpse.Destroy(DestroyMode.Vanish);
            if(!human.Destroyed)human.Destroy(DestroyMode.Vanish);
        }
        private static void Require(bool value,string label,Action<bool,string> check)
        {check(value,label);if(!value)throw new InvalidOperationException(label);}
    }
}

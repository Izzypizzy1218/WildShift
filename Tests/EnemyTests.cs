namespace Verse.AI
{
    public class Job { public bool playerForced,killIncappedTarget; public LocalTargetInfo target; }
    public static class JobMaker { public static Job MakeJob(object def,LocalTargetInfo target) { return new Job { target=target }; } }
    public class Pawn_Thinker { public Pawn pawn; }
    public abstract class ThinkNode_Conditional
    {
        protected abstract bool Satisfied(Pawn pawn);
        public bool Test(Pawn pawn) { return Satisfied(pawn); }
    }
}
namespace WildShift
{
    public static class WildShiftDefOf
    {
        public static object WildShift_FieldShapeshifterMarker = new object();
        public static ThinkTreeDef WildShift_TransformedDrafted = new ThinkTreeDef();
        public static ThinkTreeDef WildShift_TransformedEnemy = new ThinkTreeDef();
        public static ThinkTreeDef WildShift_TransformedEnemyConstant = new ThinkTreeDef();
    }
}
namespace Verse.AI.Group
{
    public enum PawnLostCondition { Incapped, Killed, MadePrisoner }
    public class LordToil
    {
        public Lord lord; public bool AssignsDuties=true, fail;
        public object duty=new object(); public int updates;
        public virtual void UpdateAllDuties()
        {
            updates++; if(fail) throw new Exception("Injected toil failure");
            foreach(Pawn p in lord.ownedPawns) p.mindState.duty=duty;
        }
    }
    public class Lord
    {
        public List<Pawn> ownedPawns = new List<Pawn>();
        public Map Map; public Faction faction; public LordToil CurLordToil;
        public int lost, gained = 1; public bool destroyed, failLost;
        public void Notify_PawnLost(Pawn p, PawnLostCondition cond)
        {
            if(!WildShift.Patch_EnemyPendingRaidLoss.Prefix(p,cond)) return;
            if(failLost) throw new Exception("injected foreign Lord callback failure");
            ownedPawns.Remove(p); p.lord=null; lost++; if(ownedPawns.Count==0) destroyed=true;
        }
    }
}
namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static Lord EnemyLord(Pawn pawn, Map map)
        {
            var lord=new Lord { Map=map, faction=pawn.Faction };
            lord.CurLordToil=new LordToil_AssaultColony { lord=lord };
            lord.ownedPawns.Add(pawn); pawn.lord=lord; pawn.mindState.duty=new object();
            return lord;
        }
        private static Pawn EnemyForm(out Pawn human, Map map)
        {
            Pawn animal=Form(out human,map);
            animal.Faction=human.Faction=new Faction { hostile=true };
            human.RaceProps=new RaceProperties { Animal=false,Humanlike=true };
            human.shifter=new HediffComp_Shapeshifter { enemyRaidParticipant=true,enemyShiftUsed=true };
            animal.comp.enemyCombatForm=true;
            return animal;
        }
        private static void RunEnemyTests()
        {
            Reset(); Map map=new Map(); Pawn human;
            Pawn animal=EnemyForm(out human,map); Lord lord=EnemyLord(animal,map); object duty=animal.mindState.duty;
            Check(EnemyShapeshifterUtility.IsCombatForm(animal),"hostile marked form recognized");
            var thinker=new Pawn_Thinker { pawn=animal }; var originalTree=new ThinkTreeDef(); var tree=originalTree;
            Patch_TransformedAnimalDraftThinkTree.Postfix(thinker,ref tree);
            Check(tree==WildShiftDefOf.WildShift_TransformedEnemy,"enemy main tree follows Lord duties");
            tree=originalTree; Patch_TransformedEnemyConstantThinkTree.Postfix(thinker,ref tree);
            Check(tree==WildShiftDefOf.WildShift_TransformedEnemyConstant,"enemy constant tree does not independently flee");
            Patch_EnemyDownedReturn.Prefix(animal); animal.Downed=true; animal.mindState.duty=null;
            Check(animal.comp.enemyReturnPending,"downing marks pending before Lord notifications");
            lord.Notify_PawnLost(animal,PawnLostCondition.Incapped);
            Check(lord.ownedPawns.Contains(animal) && !lord.destroyed && lord.lost==0,"last raider downing does not end raid");
            Check(!Patch_EnemyPendingRaidDowned.Prefix(animal),"pending downed notification deferred");
            map.GetComponent<MapComponent_EnemyShapeshifters>().ProcessPending();
            Check(human.Spawned && animal.Destroyed && !human.Dead,"downed enemy returns alive");
            Check(lord.ownedPawns.Count==1 && lord.ownedPawns[0]==human && human.lord==lord && animal.lord==null,"Lord membership follows human");
            Check(lord.lost==0 && lord.gained==1 && !lord.destroyed && human.mindState.duty!=null,"raid counters preserved and native downing-cleared duty restored");
            Check(!EnemyShapeshifterUtility.CanShift(human),"returned human cannot shift again");
            Check(!animal.comp.enemyReturnPending,"pending cleared after successful return");
            Check(human.mindState.duty==lord.CurLordToil.duty && lord.CurLordToil.updates==1,"current assault toil assigns duty once after downed return");
            EnemyShapeshifterUtility.RestoreMissingDuty(human);
            Check(lord.CurLordToil.updates==1,"valid duty is not repeatedly reassigned");
            var ready=new ThinkNode_ConditionalEnemyDutyReady();
            Check(ready.Test(human) && !ready.Test(animal) && !ready.Test(null),"local duty gate requires real duty and Lord");

            Reset(); map=new Map(); animal=EnemyForm(out human,map); lord=EnemyLord(animal,map);
            Patch_EnemyDownedReturn.Prefix(animal); animal.Downed=true; animal.mindState.duty=null;
            Check(!ready.Test(animal),"pending downed form does not execute duty nodes with null orders");
            lord.CurLordToil=new LordToil { lord=lord }; object retreatDuty=lord.CurLordToil.duty;
            map.GetComponent<MapComponent_EnemyShapeshifters>().ProcessPending();
            Check(human.mindState.duty==retreatDuty && human.lord==lord,"return during retreat takes current orders instead of forcing assault");

            Reset(); map=new Map(); animal=EnemyForm(out human,map); lord=EnemyLord(animal,map);
            Patch_EnemyDownedReturn.Prefix(animal); animal.Downed=true; animal.mindState.duty=null;
            lord.CurLordToil.fail=true;
            map.GetComponent<MapComponent_EnemyShapeshifters>().ProcessPending();
            Check(human.Spawned && animal.Destroyed && !animal.comp.HasStoredPawn,"third-party toil failure cannot interrupt committed body cleanup");

            Reset(); map=new Map(); animal=EnemyForm(out human,map); lord=EnemyLord(animal,map);
            Patch_EnemyDownedReturn.Prefix(animal); animal.Downed=true; animal.mindState.duty=null; GenSpawn.FailBefore=p=>p==human;
            map.GetComponent<MapComponent_EnemyShapeshifters>().ProcessPending();
            Check(animal.Spawned && !animal.Destroyed && animal.comp.StoredPawn==human,"failed downed return preserves bodies");
            Check(!animal.comp.enemyReturnPending && lord.lost==1 && lord.destroyed,"failed return releases raid-loss suppression");
            Reset(); map=new Map(); animal=EnemyForm(out human,map); lord=EnemyLord(animal,map);
            Patch_EnemyDownedReturn.Prefix(animal); animal.Downed=true; animal.mindState.duty=null;
            WildShiftMod.Settings.deathChance=1f;
            Patch_TransformedDeath.Prefix(animal,new DamageInfo(DamageDefOf.Blunt,999));
            map.GetComponent<MapComponent_EnemyShapeshifters>().ProcessPending();
            Check(human.Dead && lord.lost==1 && lord.destroyed,"true lethal damage retains death setting during pending return");
            WildShiftMod.Settings.deathChance=0f;

            Reset(); map=new Map(); animal=EnemyForm(out human,map); lord=EnemyLord(animal,map);
            var health=new Pawn_HealthTracker { overrideDeathOnDownedChance=.75f }; float? state, nested;
            Patch_EnemyDowningChance.Prefix(health,animal,out state);
            Check(health.overrideDeathOnDownedChance==0f && state==.75f,"enemy random death-on-downing disabled temporarily");
            Patch_EnemyDowningChance.Prefix(health,animal,out nested);
            Patch_EnemyDowningChance.Finalizer(health,nested);
            Check(health.overrideDeathOnDownedChance==0f,"nested health check restores outer override");
            Patch_EnemyDowningChance.Finalizer(health,state);
            Check(health.overrideDeathOnDownedChance==.75f,"finalizer restores native override including exception paths");
            animal.Faction=Faction.OfPlayer;
            tree=originalTree; Patch_TransformedEnemyConstantThinkTree.Postfix(new Pawn_Thinker { pawn=animal },ref tree);
            Check(tree==originalTree,"player constant AI untouched");
            animal.drafter.Drafted=true; Patch_TransformedAnimalDraftThinkTree.Postfix(new Pawn_Thinker { pawn=animal },ref tree);
            Check(tree==WildShiftDefOf.WildShift_TransformedDrafted,"player drafted main AI preserved");
            Patch_EnemyDownedReturn.Prefix(animal);
            Patch_EnemyDowningChance.Prefix(health,animal,out state);
            Check(!animal.comp.enemyReturnPending && !state.HasValue,"captured/recruited and player forms keep original downing");
            animal.Faction=human.Faction; animal.IsPrisoner=true;
            Check(!EnemyShapeshifterUtility.IsCombatForm(animal),"prisoner no longer enemy form AI");
            animal.IsPrisoner=false; animal.HostFaction=Faction.OfPlayer;
            Check(!EnemyShapeshifterUtility.IsCombatForm(animal),"hosted pawn excluded"); animal.HostFaction=null;
            Find.FactionManager.OfPlayer=null;
            Check(!EnemyShapeshifterUtility.IsCombatForm(animal),"missing player faction safe during initialization");
            Find.FactionManager.OfPlayer=Faction.OfPlayer;

            Reset(); map=new Map(); human=new Pawn { Faction=new Faction { hostile=true },RaceProps=new RaceProperties { Animal=false,Humanlike=true } };
            GenSpawn.Spawn(human,new IntVec3(),map); lord=EnemyLord(human,map);
            var list=new List<Pawn> { human }; var raid=new IncidentWorker_RaidEnemy();
            WildShiftMod.Settings.enemyShapeshifterChance=0f;
            Patch_EnemyRaidAssignment.Postfix(raid,true,list,false);
            Check(human.shifter==null,"0 percent adds no new raider shifters");
            WildShiftMod.Settings.enemyShapeshifterChance=1f;
            Patch_EnemyRaidAssignment.Postfix(raid,false,list,false);
            Patch_EnemyRaidAssignment.Postfix(raid,true,list,true);
            Patch_EnemyRaidAssignment.Postfix(new IncidentWorker_Raid(),true,list,false);
            Check(human.shifter==null,"failed/debug/friendly raids untouched");
            human.questTags=new List<string> { "scripted" };
            Patch_EnemyRaidAssignment.Postfix(raid,true,list,false);
            Check(human.shifter==null,"scripted quest actors excluded"); human.questTags=null;
            Patch_EnemyRaidAssignment.Postfix(raid,true,list,false);
            Check(human.shifter!=null && EnemyShapeshifterUtility.CanShift(human),"100 percent marks eligible assault raider");
            human.Spawned=false; Patch_EnemyAssaultStart.Postfix((LordToil_AssaultColony)lord.CurLordToil);
            int beforeArrival=TransformUtility.shiftCalls;
            map.GetComponent<MapComponent_EnemyShapeshifters>().ProcessPending();
            Check(TransformUtility.shiftCalls==beforeArrival,"unspawned drop pod pawn not transformed in flight");
            human.Spawned=true; Patch_EnemyArrival.Postfix(human);
            map.GetComponent<MapComponent_EnemyShapeshifters>().ProcessPending();
            Check(TransformUtility.shiftCalls==beforeArrival+1 && human.shifter.enemyShiftUsed,"drop pod spawn queues initial shift without polling");
            human.shifter.enemyShiftUsed=false;
            human.shifter.assignedKind.forceDeathOnDowned=true;
            Check(!EnemyShapeshifterUtility.CanShift(human),"forced-death animal kinds excluded from combat transformation"); human.shifter.assignedKind.forceDeathOnDowned=false;
            lord.CurLordToil=new LordToil { lord=lord };
            Check(!EnemyShapeshifterUtility.CanShift(human),"sappers/siege wait for normal assault duty"); lord.CurLordToil=new LordToil_AssaultColony { lord=lord };
            var queue=map.GetComponent<MapComponent_EnemyShapeshifters>(); int calls=TransformUtility.shiftCalls;
            queue.QueueShift(human); queue.QueueShift(human); queue.ProcessPending();
            queue.QueueShift(human); queue.ProcessPending();
            Check(TransformUtility.shiftCalls==calls+1 && human.shifter.enemyShiftUsed,"duplicate duty callbacks and failure cannot retry endlessly");
            PawnKindDef assigned=human.shifter.assignedKind;
            Patch_EnemyRaidAssignment.Postfix(raid,true,list,false);
            Check(!human.shifter.enemyShiftUsed && human.shifter.assignedKind==assigned,"new raid resets one-use without rerolling form");
            human.shifter.enemyShiftUsed=true;
            map.mapPawns.pawns.Add(human); queue.FinalizeInit(); queue.ProcessPending();
            Check(human.shifter.enemyShiftUsed && TransformUtility.shiftCalls==calls+1,"load reconstruction respects saved one-use");
            int reads=map.mapPawns.reads;
            for(int i=0;i<10000;i++) queue.ProcessPending();
            Check(map.mapPawns.reads==reads,"10000 idle safe points perform no map pawn scans");

            Reset(); map=new Map(); animal=EnemyForm(out human,map); lord=EnemyLord(animal,map);
            animal.Downed=true; animal.mindState.duty=null; animal.comp.enemyReturnPending=true; map.mapPawns.pawns.Add(animal);
            queue=map.GetComponent<MapComponent_EnemyShapeshifters>(); queue.FinalizeInit(); queue.ProcessPending();
            Check(human.Spawned && human.lord==lord && animal.Destroyed && human.mindState.duty!=null,"load reconstruction finishes saved pending return with restored duty");
            human.mindState.duty=null; map.mapPawns.pawns.Clear(); map.mapPawns.pawns.Add(human);
            queue.FinalizeInit();
            Check(human.mindState.duty==lord.CurLordToil.duty && human.shifter.enemyShiftUsed,"loading v1.2.0 returned human repairs duty without refreshing transformation");
            int updates=lord.CurLordToil.updates; queue.FinalizeInit();
            Check(lord.CurLordToil.updates==updates,"load repair leaves healthy raid members unchanged");
            human.mindState.duty=null; human.Faction=Faction.OfPlayer; queue.FinalizeInit();
            Check(human.mindState.duty==null && lord.CurLordToil.updates==updates,"load repair does not assign enemy orders to recruited humans");
            Reset(); map=new Map(); animal=EnemyForm(out human,map); lord=EnemyLord(animal,map);
            animal.mindState.duty=null; map.mapPawns.pawns.Add(animal);
            map.GetComponent<MapComponent_EnemyShapeshifters>().FinalizeInit();
            Check(animal.mindState.duty==lord.CurLordToil.duty,"load repair also handles existing enemy animal without duty");
            animal.mindState.duty=null; lord.CurLordToil.AssignsDuties=false; updates=lord.CurLordToil.updates;
            EnemyShapeshifterUtility.RestoreMissingDuty(animal);
            Check(lord.CurLordToil.updates==updates,"toils which do not assign duties are untouched");
            Reset(); map=new Map(); human=new Pawn { Faction=new Faction { hostile=true }, shifter=new HediffComp_Shapeshifter { enemyRaidParticipant=true } };
            GenSpawn.Spawn(human,new IntVec3(),map); lord=EnemyLord(human,map); duty=human.mindState.duty;
            animal=new Pawn { Faction=human.Faction, comp=new HediffComp_Transformed() };
            GenSpawn.Spawn(animal,new IntVec3(),map); animal.comp.TryStore(human);
            EnemyShapeshifterUtility.OnTransformed(human,animal,animal.comp);
            Check(animal.lord==lord && human.lord==null && lord.ownedPawns[0]==animal && animal.mindState.duty==duty,"successful transformation transfers Lord after storage");
            Check(human.shifter.enemyShiftUsed && animal.comp.enemyCombatForm && lord.gained==1,"successful transformation records enemy one-use without growing raid");
            WildShiftMod.Settings.enemyShapeshifterChance=.05f;
        }
    }
}

namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static void RunReviewTests()
        {
            Reset(); Pawn originalHuman; Pawn form=Form(out originalHuman);
            var caravanOwner=new ThingOwner(); caravanOwner.TryAddOrTransfer(form,false);
            Find.WorldPawns.PassToWorld(form,PawnDiscardDecideMode.KeepForever);
            Check(TransformUtility.RevertToHuman(form)==originalHuman,"world-registered holder return succeeds");
            Check(caravanOwner.Contains(originalHuman) && WorldPawnsUtility.IsWorldPawn(originalHuman)
                && form.Destroyed && !WorldPawnsUtility.IsWorldPawn(form),"caravan replacement keeps both owner and world registration");
            Reset(); form=Form(out originalHuman); caravanOwner=new ThingOwner(); caravanOwner.TryAddOrTransfer(form,false);
            Find.WorldPawns.PassToWorld(form,PawnDiscardDecideMode.KeepForever); Find.WorldPawns.Fail=true;
            Check(TransformUtility.RevertToHuman(form)==null,"caravan registration failure does not commit replacement");
            Check(caravanOwner.Contains(form) && WorldPawnsUtility.IsWorldPawn(form) && !form.Destroyed
                && form.comp.StoredPawn==originalHuman && !WorldPawnsUtility.IsWorldPawn(originalHuman),"failed registration restores animal member and stored human");
            Find.WorldPawns.Fail=false;
            Check(TransformUtility.RevertToHuman(form)==originalHuman && WorldPawnsUtility.IsWorldPawn(originalHuman),"caravan reversion can retry after registry recovers");
            Reset(); form=Form(out originalHuman); caravanOwner=new ThingOwner(); caravanOwner.TryAddOrTransfer(form,false);
            Find.WorldPawns.PassToWorld(form,PawnDiscardDecideMode.KeepForever); Find.WorldPawns.FailAfter=true;
            Check(TransformUtility.RevertToHuman(form)==originalHuman && WorldPawnsUtility.IsWorldPawn(originalHuman)
                && caravanOwner.Contains(originalHuman),"post-world-registration exception keeps committed replacement");
            Reset(); form=Form(out originalHuman); caravanOwner=new ThingOwner(); caravanOwner.TryAddOrTransfer(form,false);
            Check(TransformUtility.RevertToHuman(form)==originalHuman && !WorldPawnsUtility.IsWorldPawn(originalHuman),"ordinary on-map holder does not gain unnecessary world registration");
            Reset(); var map=new Map(); var animal=new Pawn(); GenSpawn.Spawn(animal,new IntVec3(),map);
            var human=new Pawn(); PawnGenerator.Generate=()=>human; GenSpawn.FailBefore=p=>p==human;
            TamingTestBridge.Run(animal);
            Check(animal.Spawned && !animal.Destroyed && human.Destroyed,"failed tame reveal placement preserves original animal");
            Reset(); animal=new Pawn(); GenSpawn.Spawn(animal,new IntVec3(),map);
            human=new Pawn(); PawnGenerator.Generate=()=>human; GenSpawn.FailAfter=p=>p==human;
            TamingTestBridge.Run(animal);
            Check(human.Spawned && !human.Destroyed && animal.Destroyed,"post-registration exception commits tamed human once");
            Reset(); animal=new Pawn(); GenSpawn.Spawn(animal,new IntVec3(),map);
            PawnGenerator.Generate=()=>{throw new Exception("injected generator failure");};
            TamingTestBridge.Run(animal);
            Check(animal.Spawned && !animal.Destroyed,"generation exception leaves tamed animal intact");
            PawnGenerator.Generate=()=>null; TamingTestBridge.Run(animal);
            Check(animal.Spawned && !animal.Destroyed,"null generation leaves tamed animal intact");
            human=new Pawn(); PawnGenerator.Generate=()=>human; TransformUtility.failAssignment=true;
            TamingTestBridge.Run(animal);
            Check(animal.Spawned && !animal.Destroyed && human.Destroyed,"failed assignment cleans candidate only");
            Reset(); animal=new Pawn(); GenSpawn.Spawn(animal,new IntVec3(),map); int generated=0;
            animal.Faction=null; PawnGenerator.Generate=()=>{generated++;return new Pawn();}; TamingTestBridge.Run(animal);
            Check(generated==0 && animal.Spawned,"deferred reveal rechecks current faction");

            Reset(); animal=new Pawn(); GenSpawn.Spawn(animal,new IntVec3(),map); animal.drafter.Drafted=true;
            var target=new Thing { Spawned=true,Map=map }; int orders=TransformUtility.orders;
            Check(TransformUtility.TryOrderMeleeAttack(animal,target),"valid current-map melee order accepted");
            Check(animal.jobs.lastJob.playerForced && animal.jobs.lastJob.killIncappedTarget,"native direct-attack job flags retained");
            target.Map=new Map(); Check(!TransformUtility.TryOrderMeleeAttack(animal,target),"other-map target rejected");
            target.Map=map; target.Destroyed=true; Check(!TransformUtility.TryOrderMeleeAttack(animal,target),"destroyed target rejected");
            target.Destroyed=false; target.Spawned=false; Check(!TransformUtility.TryOrderMeleeAttack(animal,target),"despawned target rejected");
            target.Spawned=true; animal.Downed=true; Check(!TransformUtility.TryOrderMeleeAttack(animal,target),"downed attacker rejected");
            animal.Downed=false; animal.Faction=new Faction { hostile=true }; Check(!TransformUtility.TryOrderMeleeAttack(animal,target),"nonplayer attacker cannot receive direct player order");
            animal.Faction=Faction.OfPlayer; Check(!TransformUtility.TryOrderMeleeAttack(animal,animal),"self target rejected");
            Check(TransformUtility.orders==orders+1,"invalid attacks never reach job/pathing submission");

            var wild=new Pawn { Spawned=true,Faction=null };
            var tame=new Pawn { Spawned=true,Faction=Faction.OfPlayer };
            var dead=new Pawn { Spawned=true,Faction=new Faction { hostile=true },Dead=true };
            int reads=TransformUtility.transformedLookups;
            for(int i=0;i<10000;i++)
            {
                EnemyShapeshifterUtility.IsCombatForm(wild);
                EnemyShapeshifterUtility.IsCombatForm(tame);
                EnemyShapeshifterUtility.IsCombatForm(dead);
            }
            Check(TransformUtility.transformedLookups==reads,"30000 ordinary/dead animal checks do not look up transformed hediffs");
        }
    }
}

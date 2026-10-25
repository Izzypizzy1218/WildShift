namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static void RunReviewTests()
        {
            RunReturnDraftTests();

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

        private static void RunReturnDraftTests()
        {
            var map=new Map(); Pawn human;
            Reset(); var animal=Form(out human,map); animal.drafter.Drafted=true;
            Check(TransformUtility.RevertToHuman(animal)==human && human.Drafted,"drafted form returns to a drafted human");

            Reset(); animal=Form(out human,map);
            Check(TransformUtility.RevertToHuman(animal)==human && !human.Drafted,"ordinary undrafted return stays undrafted");

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true; human.Downed=true;
            Check(TransformUtility.RevertToHuman(animal)==human && !human.Drafted,"downed stored human is not drafted on return");

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true; human.InMentalState=true;
            Check(TransformUtility.RevertToHuman(animal)==human && !human.Drafted,"mentally broken stored human is not drafted on return");

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true;
            animal.Faction=new Faction { hostile=true };
            Check(TransformUtility.RevertToHuman(animal)==human && !human.Drafted,"enemy return does not gain a player draft order");

            Reset(); animal=Form(out human); animal.drafter.Drafted=true; human.drafter=null;
            var owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false);
            Check(TransformUtility.RevertToHuman(animal)==human && owner.Contains(human)
                && !human.Spawned && human.drafter==null,"holder return does not create an off-map draft controller");

            Reset(); animal=Form(out human); animal.drafter.Drafted=true; human.drafter=null;
            Find.WorldPawns.PassToWorld(animal,PawnDiscardDecideMode.KeepForever);
            Check(TransformUtility.RevertToHuman(animal)==human && WorldPawnsUtility.IsWorldPawn(human)
                && !human.Spawned && human.drafter==null,"world return does not create an off-map draft controller");

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true;
            GenSpawn.FailBefore=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.Drafted
                && animal.comp.StoredPawn==human && !human.Drafted,"failed placement keeps the active form drafted and the original stored");
            GenSpawn.FailBefore=null;
            Check(TransformUtility.RevertToHuman(animal)==human && human.Drafted,"return retry restores draft after placement recovers");

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true;
            GenSpawn.FailAfter=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==human && human.Drafted,
                "committed spawn restores draft even when a spawn callback throws");

            Reset(); animal=Form(out human); animal.drafter.Drafted=true; human.drafter=null;
            owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false); owner.Reject=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.Drafted
                && animal.comp.StoredPawn==human && human.drafter==null,"holder rejection preserves active form draft and stored original");

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true;
            float deathChance=WildShiftMod.Settings.deathChance; WildShiftMod.Settings.deathChance=0;
            Check(!Patch_TransformedDeath.Prefix(animal,null) && human.Spawned && !human.Dead
                && human.Drafted,"animal death keeps the surviving human's draft order");
            WildShiftMod.Settings.deathChance=deathChance;

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true;
            Check(!Patch_TransformedDeath.Prefix(animal,new DamageInfo(DamageDefOf.ExecutionCut,100))
                && human.Dead && animal.Destroyed,"draft restoration does not bypass slaughter death");
        }
    }
}

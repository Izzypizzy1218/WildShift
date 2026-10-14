// Serialization below traverses production ExposeData on fresh entry objects.
// Pawn serialization is modeled, not the real game's cross-reference resolver.
namespace Verse
{
    public interface IExposable { void ExposeData(); }
    public enum LoadSaveMode { Inactive, Saving, LoadingVars }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class RecoverySave
    {
        public static Dictionary<string,object> Node = new Dictionary<string,object>();
        public static int DeepPawns, ReferencedPawns;
    }
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value,string key,T fallback=default(T))
        { if(Scribe.mode==LoadSaveMode.Saving) RecoverySave.Node[key]=value; else { object saved; value=RecoverySave.Node.TryGetValue(key,out saved)?(T)saved:fallback; } }
    }
    public static class Scribe_References
    {
        public static void Look(ref Pawn value,string key)
        { if(Scribe.mode==LoadSaveMode.Saving) { RecoverySave.ReferencedPawns++; RecoverySave.Node[key]=value; } else value=(Pawn)RecoverySave.Node[key]; }
    }
    public static class Scribe_Deep
    {
        public static void Look(ref Pawn value,string key)
        {
            if(Scribe.mode==LoadSaveMode.Saving) { RecoverySave.DeepPawns++; RecoverySave.Node[key]=value; }
            else { var old=(Pawn)RecoverySave.Node[key]; value=new Pawn { thingIDNumber=old.thingIDNumber, Faction=old.Faction, shifter=old.shifter }; }
        }
    }
    public static partial class Scribe_Collections
    {
        public static void Look<T>(ref List<T> values,string key,LookMode mode) where T : IExposable,new()
        {
            var parent=RecoverySave.Node;
            if(Scribe.mode==LoadSaveMode.Saving)
            {
                var entries=new List<Dictionary<string,object>>(); parent[key]=entries;
                foreach(var value in values) { RecoverySave.Node=new Dictionary<string,object>(); entries.Add(RecoverySave.Node); value.ExposeData(); }
            }
            else
            {
                values=new List<T>(); object saved;
                if(parent.TryGetValue(key,out saved))
                    foreach(var entry in (List<Dictionary<string,object>>)saved) { RecoverySave.Node=entry; var value=new T(); value.ExposeData(); values.Add(value); }
            }
            RecoverySave.Node=parent;
        }
    }
}
namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static GameComponent_AwakeningRecovery ReloadRecovery()
        {
            RecoverySave.Node=new Dictionary<string,object>(); RecoverySave.DeepPawns=0; RecoverySave.ReferencedPawns=0;
            Scribe.mode=LoadSaveMode.Saving; Scribe_Collections.Loading=false;
            Current.Game.GetComponent<GameComponent_AwakeningRecovery>().ExposeData();
            Current.Game=new Game(); Scribe.mode=LoadSaveMode.LoadingVars; Scribe_Collections.Loading=true;
            var tracker=Current.Game.GetComponent<GameComponent_AwakeningRecovery>(); tracker.ExposeData();
            Scribe.mode=LoadSaveMode.Inactive; Scribe_Collections.Loading=false;
            return tracker;
        }

        private static void RunRecoveryRegressionTests()
        {
            Reset(); var neutralSource=OwnedLatentAnimal(); var neutralHuman=new Pawn {Faction=null}; PawnGenerator.Generate=()=>neutralHuman;
            OwnedAnimalAwakening.TryAwaken(neutralSource);
            Check(neutralHuman.Faction==Faction.OfPlayer && neutralHuman.Spawned && neutralSource.Destroyed,"independently generated awakening race joins colony before placement");
            Reset(); neutralSource=OwnedLatentAnimal(); neutralHuman=new Pawn(); PawnGenerator.Generate=()=>neutralHuman; AnimalFormGender.mismatch=true;
            OwnedAnimalAwakening.TryAwaken(neutralSource);
            Check(!neutralSource.Destroyed && neutralHuman.Destroyed,"foreign race replacement rolls back candidate and preserves animal");
            foreach(bool detachBeforeFailure in new[] {false,true})
            {
                Reset(); Pawn human; var animal=Form(out human); var owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false);
                Find.WorldPawns.PassToWorld(animal,PawnDiscardDecideMode.KeepForever);
                if(detachBeforeFailure) owner.ThrowBefore=p=>p==human; else owner.Reject=p=>p==human;
                Check(TransformUtility.RevertToHuman(animal)==null,"failed caravan insertion never falls back to loose world return");
                Check(owner.Contains(animal) && !animal.Destroyed && animal.comp.StoredPawn==human && !WorldPawnsUtility.IsWorldPawn(human),"caravan failure preserves member and stored human");
                owner.ThrowBefore=null; owner.Reject=null;
                Check(TransformUtility.RevertToHuman(animal)==human && owner.Contains(human) && WorldPawnsUtility.IsWorldPawn(human),"caravan retries in original owner after rejection recovers");
            }

            Reset(); var source=OwnedLatentAnimal(); var humans=new List<Pawn>();
            PawnGenerator.Generate=()=>{var p=new Pawn(); humans.Add(p); return p;};
            Pawn.DestroyFailBefore=p=>p==source;
            OwnedAnimalAwakening.TryAwaken(source); OwnedAnimalAwakening.TryAwaken(source);
            Check(humans.Count==1 && humans[0].Spawned && source.Spawned && !source.Destroyed,"original cleanup failure plus repeat click keeps exactly one human");
            Check(OwnedAnimalAwakening.GetCommand(source).defaultLabel=="WildShift_CommandFinishAwakeningLabel","completed source offers cleanup-only command");
            var restored=ReloadRecovery();
            Check(restored.IsCompleted(source),"completed awakening identity survives serialization");
            OwnedAnimalAwakening.TryAwaken(source);
            Check(humans.Count==1,"reloaded completed source cannot generate another human");
            humans[0].Dead=true;
            Check(restored.IsCompleted(source),"human death does not reset consumed animal identity");
            source.inventory=new TestInventory(); var cargo=new Thing(); source.inventory.innerContainer.Add(cargo);
            Pawn.DestroyFailBefore=null; OwnedAnimalAwakening.TryAwaken(source);
            Check(source.Destroyed && humans.Count==1 && TestDrops.Ground.Contains(cargo),"cleanup retry drops newly added cargo without generating another human");
            source.Destroyed=false; source.Spawned=true; OwnedAnimalAwakening.TryAwaken(source);
            Check(source.Destroyed && humans.Count==1,"obsolete source resurrection cannot grant a new identity");

            Reset(); source=OwnedLatentAnimal(); int generated=0;
            PawnGenerator.Generate=()=>{generated++; return new Pawn();}; Pawn.DestroyNoop=p=>p==source;
            OwnedAnimalAwakening.TryAwaken(source); OwnedAnimalAwakening.TryAwaken(source);
            Check(generated==1 && !source.Destroyed,"silent destruction rejection also blocks duplication");
            Pawn.DestroyNoop=null; Pawn.DestroyFailAfter=p=>p==source; OwnedAnimalAwakening.TryAwaken(source);
            Check(source.Destroyed && generated==1,"post-destruction exception keeps committed cleanup");

            Reset(); source=OwnedLatentAnimal(); source.inventory=new TestInventory { Fail=true }; source.inventory.innerContainer.Add(new Thing());
            humans=new List<Pawn>(); PawnGenerator.Generate=()=>{var p=new Pawn(); humans.Add(p); return p;};
            Pawn.DestroyFailBefore=p=>p!=source;
            OwnedAnimalAwakening.TryAwaken(source); OwnedAnimalAwakening.TryAwaken(source);
            Check(humans.Count==1 && humans[0].Spawned && !source.Destroyed,"failed drop and candidate cleanup block further generation");
            ReloadRecovery();
            Check(RecoverySave.ReferencedPawns==1 && RecoverySave.DeepPawns==0,"spawned rollback pawn serialized by reference, never duplicated deeply");
            source.inventory.Fail=false; OwnedAnimalAwakening.TryAwaken(source);
            Check(humans.Count==1 && !source.Destroyed,"rollback lock survives reload while destruction still fails");
            Pawn.DestroyFailBefore=null; OwnedAnimalAwakening.TryAwaken(source);
            Check(humans.Count==2 && humans[0].Destroyed && humans[1].Spawned && source.Destroyed,"rollback recovery destroys old candidate before allowing one successful retry");

            Reset(); source=OwnedLatentAnimal(); generated=0;
            PawnGenerator.Generate=()=>{generated++; return new Pawn();}; GenSpawn.FailBefore=p=>p!=source; Pawn.DestroyFailBefore=p=>p!=source;
            OwnedAnimalAwakening.TryAwaken(source); ReloadRecovery();
            Check(RecoverySave.DeepPawns==1 && RecoverySave.ReferencedPawns==0,"unspawned unregistered rollback pawn is deeply retained for save");
            OwnedAnimalAwakening.TryAwaken(source);
            Check(generated==1 && !source.Destroyed,"detached rollback survives reload without a new generation");
            GenSpawn.FailBefore=null; Pawn.DestroyFailBefore=null; OwnedAnimalAwakening.TryAwaken(source);
            Check(generated==2 && source.Destroyed,"detached candidate cleanup can recover and retry");

            Reset(); source=OwnedLatentAnimal(); var externalCandidate=new Pawn();
            Find.WorldPawns.PassToWorld(externalCandidate,PawnDiscardDecideMode.KeepForever);
            Pawn.DestroyFailBefore=p=>p==externalCandidate;
            Current.Game.GetComponent<GameComponent_AwakeningRecovery>().RollBack(source,externalCandidate);
            ReloadRecovery();
            Check(RecoverySave.ReferencedPawns==1 && RecoverySave.DeepPawns==0,"world-registered failed candidate is saved by reference");
            externalCandidate.Destroyed=true; ReloadRecovery();
            Check(RecoverySave.ReferencedPawns==0 && Current.Game.GetComponent<GameComponent_AwakeningRecovery>().TryClearRollback(source),"externally destroyed rollback candidates are cleared before saving dead references");

            Reset(); source=OwnedLatentAnimal(); externalCandidate=new Pawn(); var externalOwner=new ThingOwner(); externalOwner.TryAddOrTransfer(externalCandidate,false);
            Pawn.DestroyFailBefore=p=>p==externalCandidate;
            Current.Game.GetComponent<GameComponent_AwakeningRecovery>().RollBack(source,externalCandidate); ReloadRecovery();
            Check(RecoverySave.ReferencedPawns==1 && RecoverySave.DeepPawns==0,"externally held candidate is not saved twice");

            Reset(); source=OwnedLatentAnimal(); generated=0;
            PawnGenerator.Generate=()=>{generated++; TamingTestBridge.Run(source); OwnedAnimalAwakening.TryAwaken(source); return new Pawn();};
            TamingTestBridge.Run(source);
            Check(generated==1 && source.Destroyed,"normal taming and manual reentry share one awakening lock");
            Reset(); source=LatentAnimal(); generated=0;
            PawnGenerator.Generate=()=>{generated++; TamingTestBridge.Run(source); return new Pawn();};
            Check(MedicalRecruit(source) && generated==1,"medical revelation also uses the shared lock");

            Reset(); RecoverySave.Node=new Dictionary<string,object>(); Scribe_Collections.Values.Remove("wildShiftCompletedAwakenings");
            Scribe_Collections.Loading=true; Scribe.mode=LoadSaveMode.LoadingVars;
            var legacy=Current.Game.GetComponent<GameComponent_AwakeningRecovery>(); legacy.ExposeData();
            Scribe_Collections.Loading=false; Scribe.mode=LoadSaveMode.Inactive; source=OwnedLatentAnimal();
            Check(!legacy.IsCompleted(source) && legacy.TryClearRollback(source),"legacy save without recovery fields starts empty");
        }
    }
}

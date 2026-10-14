// Actual production damage tracking and both taming hooks, with engine boundaries
// stubbed. Serialization exercises ExposeData using a copied value set, not Scribe.
namespace Verse
{
    public class GameComponent { public virtual void ExposeData() {} }
    public class Game
    {
        public int componentReads;
        private Dictionary<Type,GameComponent> components = new Dictionary<Type,GameComponent>();
        public T GetComponent<T>() where T : GameComponent
        { componentReads++; GameComponent component; if(!components.TryGetValue(typeof(T),out component)) components[typeof(T)]=component=(GameComponent)Activator.CreateInstance(typeof(T),this); return (T)component; }
    }
    public static class Current { public static Game Game = new Game(); }
    public enum LookMode { Value, Deep }
    public static partial class Scribe_Collections
    {
        public static bool Loading;
        public static Dictionary<string,HashSet<int>> Values=new Dictionary<string,HashSet<int>>();
        public static HashSet<int> Saved { get { HashSet<int> value; return Values.TryGetValue("wildShiftPlayerHarmedAnimals",out value) ? value : null; } set { Values["wildShiftPlayerHarmedAnimals"]=value; } }
        public static void Look(ref HashSet<int> values,string label,LookMode mode)
        { HashSet<int> stored; if(Loading) values=!Values.TryGetValue(label,out stored) || stored==null ? null : new HashSet<int>(stored); else Values[label]=new HashSet<int>(values); }
    }
    public static class LongEventHandler
    {
        public static bool Defer; public static List<Action> Pending=new List<Action>();
        public static void ExecuteWhenFinished(Action action) { if(Defer) Pending.Add(action); else action(); }
        public static void Flush() { foreach(var action in Pending.ToArray()) action(); Pending.Clear(); }
    }
}
namespace RimWorld { public class TendUtility {} }
namespace WildShift
{
    public static class AnimalPool { public static bool IsEligible(Pawn pawn) { return pawn!=null && pawn.RaceProps.Animal; } }
}
namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static Pawn LatentAnimal()
        {
            var animal=new Pawn { Faction=null }; animal.health.hediffSet.latent=true;
            GenSpawn.Spawn(animal,new IntVec3(),new Map()); return animal;
        }
        private static void Harm(Pawn animal,Thing source,float actual=5f)
        {
            Patch_LatentAnimalPlayerHarm.Prefix(animal,new DamageInfo(DamageDefOf.Blunt,5f) { Instigator=source },actual);
        }
        private static bool MedicalRecruit(Pawn animal, bool recruit=true)
        {
            bool state; Patch_MedicalBondAwakening.Prefix(new Pawn(),animal,out state);
            // This models DoTend's successful medical-bond recruitment boundary.
            if(recruit) animal.SetFaction(Faction.OfPlayer);
            Patch_MedicalBondAwakening.Postfix(animal,state); return animal.Destroyed;
        }
        private static void RunMedicalAwakeningTests()
        {
            Reset(); var animal=LatentAnimal(); Harm(animal,new Pawn { Faction=null });
            Check(MedicalRecruit(animal),"wild predator injury permits medical awakening");
            Reset(); animal=LatentAnimal(); Harm(animal,new Pawn { Faction=new Faction { hostile=true } });
            Check(MedicalRecruit(animal),"enemy injury permits medical awakening");
            Reset(); animal=LatentAnimal(); Harm(animal,new Pawn());
            Check(!MedicalRecruit(animal) && animal.Faction.IsPlayer,"player harm blocks awakening but preserves native medical recruitment");
            Reset(); animal=LatentAnimal(); Harm(animal,new Thing { Faction=Faction.OfPlayer });
            Check(!MedicalRecruit(animal),"player-owned turret or trap instigator blocks medical awakening");
            Reset(); animal=LatentAnimal(); Harm(animal,new Pawn(),0f);
            Check(MedicalRecruit(animal),"fully absorbed player attack does not record harm");
            Reset(); animal=LatentAnimal(); Harm(animal,null);
            Check(MedicalRecruit(animal),"unattributed environmental damage is not blamed on player");
            Reset(); animal=LatentAnimal(); Harm(animal,new Pawn()); Harm(animal,new Pawn { Faction=null });
            Check(!MedicalRecruit(animal),"later predator damage does not erase player harm");
            Reset(); animal=LatentAnimal(); animal.Dead=true; Harm(animal,new Pawn()); animal.Dead=false;
            Check(!MedicalRecruit(animal),"lethal player hit remains blocked after resurrection");
            Reset(); animal=LatentAnimal(); animal.Dead=true; Harm(animal,new Pawn { Faction=null }); animal.Dead=false;
            Check(MedicalRecruit(animal),"resurrection alone is not a medical awakening ban");
            Reset(); animal=LatentAnimal(); Harm(animal,new Pawn());
            var tracker=Current.Game.GetComponent<GameComponent_LatentAnimalHarm>();
            Scribe_Collections.Loading=false; tracker.ExposeData();
            int savedId=animal.thingIDNumber; Current.Game=new Game();
            tracker=Current.Game.GetComponent<GameComponent_LatentAnimalHarm>();
            Scribe_Collections.Loading=true; tracker.ExposeData(); Scribe_Collections.Loading=false;
            animal=LatentAnimal(); animal.thingIDNumber=savedId;
            Check(!MedicalRecruit(animal),"saved IDs block a loaded pawn with the same identity");
            Current.Game=new Game(); Scribe_Collections.Saved=null; Scribe_Collections.Loading=true;
            Current.Game.GetComponent<GameComponent_LatentAnimalHarm>().ExposeData(); Scribe_Collections.Loading=false;
            animal=LatentAnimal(); Check(MedicalRecruit(animal),"old saves without harm history remain loadable");
            Reset(); animal=LatentAnimal(); Harm(animal,new Pawn()); bool normal;
            Patch_Taming.Prefix(animal,out normal); animal.SetFaction(Faction.OfPlayer); Patch_Taming.Postfix(animal,normal);
            Check(animal.Destroyed,"normal taming remains allowed after player harm without any bond check");
            Reset(); animal=LatentAnimal(); Patch_Taming.Prefix(animal,out normal); Patch_Taming.Postfix(animal,normal);
            Check(!animal.Destroyed,"failed normal taming does not reveal");
            Reset(); animal=LatentAnimal(); Check(!MedicalRecruit(animal,false),"treatment without native recruitment does not reveal");
            Reset(); animal=LatentAnimal(); animal.health.hediffSet.latent=false;
            Check(!MedicalRecruit(animal),"unmarked animals never awaken through treatment");
            Reset(); animal=LatentAnimal(); animal.Faction=Faction.OfPlayer;
            Check(!MedicalRecruit(animal),"already owned animals do not awaken from repeated treatment");
            Reset(); animal=LatentAnimal(); bool medical;
            Patch_MedicalBondAwakening.Prefix(new Pawn { Faction=new Faction() },animal,out medical);
            Check(!medical,"nonplayer doctor does not initiate awakening");
            Reset(); animal=LatentAnimal(); LongEventHandler.Defer=true; MedicalRecruit(animal);
            Check(!animal.Destroyed,"queued revelation leaves patient intact until callback");
            Harm(animal,new Pawn()); LongEventHandler.Flush();
            Check(!animal.Destroyed,"deferred medical revelation rechecks newly recorded player harm");
            Reset(); animal=LatentAnimal(); var other=LatentAnimal(); Harm(animal,new Pawn());
            Check(MedicalRecruit(other),"one animal's harm does not block another animal");
        }
    }
}

namespace Verse
{
    public class Gizmo {}
    public class Command_Action : Gizmo
    {
        public string defaultLabel, defaultDesc, disabledReason; public object icon;
        public bool disabled; public Action action;
        public void Disable(string reason) { disabled=true; disabledReason=reason; }
    }
    public enum ThingPlaceMode { Near }
    public static class TestDrops
    {
        public static List<Thing> Ground=new List<Thing>();
        public static void Drop(List<Thing> items,bool fail,bool throws)
        { if(throws) throw new Exception("injected drop failure"); if(!fail) { Ground.AddRange(items); items.Clear(); } }
    }
    public class TestInventory
    {
        public TestItemList innerContainer=new TestItemList(); public bool Fail, Throws;
        public ThingOwner GetDirectlyHeldThings() { return innerContainer.Owner; }
        public void DropAllNearPawn(IntVec3 pos,bool forbid,bool unforbid) { TestDrops.Drop(innerContainer,Fail,Throws); }
    }
    public class TestEquipment
    {
        public TestItemList AllEquipmentListForReading=new TestItemList(); public bool Fail;
        public ThingOwner GetDirectlyHeldThings() { return AllEquipmentListForReading.Owner; }
        public void DropAllEquipment(IntVec3 pos,bool forbid) { TestDrops.Drop(AllEquipmentListForReading,Fail,false); }
    }
    public class TestApparel
    {
        public TestItemList WornApparel=new TestItemList(); public bool Fail;
        public ThingOwner GetDirectlyHeldThings() { return WornApparel.Owner; }
        public int WornApparelCount { get { return WornApparel.Count; } }
        public void DropAll(IntVec3 pos,bool forbid,bool dropLocked) { TestDrops.Drop(WornApparel,Fail,false); }
    }
    public class TestCarryTracker
    {
        private TestItemList things=new TestItemList(); public bool Fail;
        public ThingOwner GetDirectlyHeldThings() { return things.Owner; }
        public Thing CarriedThing { get { return things.Count>0 ? things[0] : null; } set { things.Clear(); if(value!=null) things.Add(value); } }
        public bool TryDropCarriedThing(IntVec3 pos,ThingPlaceMode mode,out Thing dropped)
        { dropped=null; if(Fail) return false; dropped=CarriedThing; if(dropped!=null) TestDrops.Ground.Add(dropped); CarriedThing=null; return true; }
    }
}
namespace RimWorld { public enum TradeAction { PlayerBuys, PlayerSells } }
namespace WildShift { public static class WildShiftTextures { public static object Transform=new object(); } }
namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static Pawn OwnedLatentAnimal()
        {
            var animal=LatentAnimal(); animal.SetFaction(Faction.OfPlayer); return animal;
        }
        private static void RunOwnedAnimalAwakeningTests()
        {
            Reset(); var animal=OwnedLatentAnimal(); var human=new Pawn(); PawnGenerator.Generate=()=>human;
            var command=OwnedAnimalAwakening.GetCommand(animal);
            Check(command!=null && !command.disabled,"previously purchased marked animal receives enabled awakening command");
            command.action();
            Check(animal.Destroyed && human.Spawned && human.shifter!=null,"manual command safely reveals human with shapeshifter state");
            Reset(); animal=LatentAnimal(); Check(OwnedAnimalAwakening.GetCommand(animal)==null,"wild animal receives no ownership shortcut");
            animal.Faction=new Faction(); Check(OwnedAnimalAwakening.GetCommand(animal)==null,"trader-owned animal receives no command");
            animal.Faction=Faction.OfPlayer; animal.health.hediffSet.latent=false;
            Check(OwnedAnimalAwakening.GetCommand(animal)==null,"unmarked purchased animal cannot awaken");
            animal.health.hediffSet.latent=true; animal.Dead=true;
            Check(OwnedAnimalAwakening.GetCommand(animal)==null,"corpse receives no awakening command");
            animal.Dead=false; animal.Destroyed=true;
            Check(OwnedAnimalAwakening.GetCommand(animal)==null,"destroyed animal receives no command");
            Reset(); animal=OwnedLatentAnimal(); animal.Spawned=false; animal.Map=null;
            Check(OwnedAnimalAwakening.GetCommand(animal).disabled,"caravan or delivery-container animal must arrive on map");
            GenSpawn.Spawn(animal,new IntVec3(),new Map());
            Check(!OwnedAnimalAwakening.GetCommand(animal).disabled,"map arrival enables purchased animal awakening");
            animal.Downed=true; Check(OwnedAnimalAwakening.GetCommand(animal).disabled,"downed animal cannot manually awaken");
            animal.Downed=false; animal.InMentalState=true;
            Check(OwnedAnimalAwakening.GetCommand(animal).disabled,"mental state blocks manual command");
            Reset(); animal=OwnedLatentAnimal(); Harm(animal,new Pawn());
            Check(!OwnedAnimalAwakening.GetCommand(animal).disabled,"damage history alone does not block an owned or purchased animal");
            Current.Game.GetComponent<GameComponent_LatentAnimalHarm>().RecordMedicalRecruitment(animal);
            Check(OwnedAnimalAwakening.GetCommand(animal).disabled,"manual button cannot bypass player-harm medical restriction");
            OwnedAnimalAwakening.TryAwaken(animal); Check(!animal.Destroyed,"direct invocation also enforces harm block");
            Reset(); animal=OwnedLatentAnimal(); command=OwnedAnimalAwakening.GetCommand(animal); animal.Faction=null; command.action();
            Check(!animal.Destroyed,"stale command rechecks ownership");
            Reset(); animal=OwnedLatentAnimal(); command=OwnedAnimalAwakening.GetCommand(animal);
            LongEventHandler.Defer=true; command.action(); Harm(animal,new Pawn());
            Current.Game.GetComponent<GameComponent_LatentAnimalHarm>().RecordMedicalRecruitment(animal); LongEventHandler.Flush();
            Check(!animal.Destroyed,"queued manual command rechecks new harm");
            Reset(); animal=OwnedLatentAnimal(); animal.inventory=new TestInventory(); animal.inventory.innerContainer.Add(new Thing());
            animal.equipment=new TestEquipment(); animal.equipment.AllEquipmentListForReading.Add(new Thing());
            animal.apparel=new TestApparel(); animal.apparel.WornApparel.Add(new Thing());
            animal.carryTracker=new TestCarryTracker { CarriedThing=new Thing() };
            var belongings=new [] { animal.inventory.innerContainer[0],animal.equipment.AllEquipmentListForReading[0],animal.apparel.WornApparel[0],animal.carryTracker.CarriedThing };
            Check(!OwnedAnimalAwakening.GetCommand(animal).disabled,"cargo and all equipment allow immediate command use");
            OwnedAnimalAwakening.TryAwaken(animal);
            Check(animal.Destroyed && TestDrops.Ground.Count==4 && belongings.All(t=>TestDrops.Ground.Contains(t)),"all original items preserved on ground exactly once");
            Reset(); animal=OwnedLatentAnimal(); animal.inventory=new TestInventory { Fail=true }; animal.inventory.innerContainer.Add(new Thing());
            human=new Pawn(); PawnGenerator.Generate=()=>human; OwnedAnimalAwakening.TryAwaken(animal);
            Check(!animal.Destroyed && animal.Spawned && animal.inventory.innerContainer.Count==1 && human.Destroyed,"failed drop retains animal and item and removes generated human");
            animal.inventory.Fail=false; PawnGenerator.Generate=()=>new Pawn(); OwnedAnimalAwakening.TryAwaken(animal);
            Check(animal.Destroyed && TestDrops.Ground.Count==1,"failed drop can safely retry without item duplication");
            Reset(); animal=OwnedLatentAnimal(); animal.inventory=new TestInventory { Throws=true }; animal.inventory.innerContainer.Add(new Thing());
            OwnedAnimalAwakening.TryAwaken(animal); Check(!animal.Destroyed && animal.inventory.innerContainer.Count==1,"drop exception preserves animal and belongings");
            Reset(); animal=OwnedLatentAnimal(); animal.inventory=new TestInventory(); var cargo=new Thing(); animal.inventory.innerContainer.Add(cargo);
            animal.carryTracker=new TestCarryTracker { CarriedThing=new Thing(),Fail=true }; OwnedAnimalAwakening.TryAwaken(animal);
            Check(!animal.Destroyed && TestDrops.Ground.Contains(cargo) && animal.carryTracker.CarriedThing!=null,"partial drop preserves both dropped cargo and remaining carried item");
            Reset(); animal=OwnedLatentAnimal(); human=new Pawn(); PawnGenerator.Generate=()=>human; GenSpawn.FailBefore=p=>p==human;
            OwnedAnimalAwakening.TryAwaken(animal);
            Check(!animal.Destroyed && animal.Spawned && human.Destroyed,"manual placement failure preserves purchased animal");
            GenSpawn.FailBefore=null; PawnGenerator.Generate=()=>new Pawn(); OwnedAnimalAwakening.TryAwaken(animal);
            Check(animal.Destroyed,"manual awakening can retry after placement failure");
            Reset(); animal=OwnedLatentAnimal(); int generated=0; command=OwnedAnimalAwakening.GetCommand(animal);
            PawnGenerator.Generate=()=>{generated++; OwnedAnimalAwakening.TryAwaken(animal); return new Pawn();};
            LongEventHandler.Defer=true; command.action(); command.action(); LongEventHandler.Flush();
            Check(generated==1 && animal.Destroyed,"duplicate queued clicks and reentrant callbacks generate one human");
            Reset(); animal=OwnedLatentAnimal(); var vanilla=new Gizmo(); IEnumerable<Gizmo> gizmos=new [] { vanilla };
            Patch_OwnedAnimalAwakeningGizmos.Postfix(animal,ref gizmos); var result=gizmos.ToList();
            Check(result.Count==2 && result[0]==vanilla && result[1] is Command_Action,"postfix preserves existing gizmos and appends command");
            Reset(); animal=LatentAnimal(); Harm(animal,new Pawn()); MedicalRecruit(animal);
            var tracker=Current.Game.GetComponent<GameComponent_LatentAnimalHarm>();
            Check(tracker.WasMedicalAwakeningBlocked(animal) && OwnedAnimalAwakening.GetCommand(animal).disabled,"real medical acquisition records manual anti-abuse block");
            Patch_PurchasedAnimalAwakening.Postfix(animal,TradeAction.PlayerSells);
            Check(tracker.WasMedicalAwakeningBlocked(animal),"selling does not grant purchase exemption");
            Patch_PurchasedAnimalAwakening.Postfix(animal,TradeAction.PlayerBuys);
            Check(!tracker.WasMedicalAwakeningBlocked(animal) && tracker.WasHarmed(animal) && !OwnedAnimalAwakening.GetCommand(animal).disabled,"purchase clears only medical-origin block while retaining harm history");
            Harm(animal,new Pawn()); Check(!OwnedAnimalAwakening.GetCommand(animal).disabled,"later damage to purchased animal is not a medical acquisition");
            tracker.RecordMedicalRecruitment(animal); Scribe_Collections.Loading=false; tracker.ExposeData();
            Current.Game=new Game(); tracker=Current.Game.GetComponent<GameComponent_LatentAnimalHarm>();
            Scribe_Collections.Loading=true; tracker.ExposeData(); Scribe_Collections.Loading=false;
            Check(tracker.WasMedicalAwakeningBlocked(animal) && tracker.WasHarmed(animal),"medical-origin block and harm history serialize independently");
            tracker.RecordPurchase(animal); tracker.ExposeData(); Current.Game=new Game();
            tracker=Current.Game.GetComponent<GameComponent_LatentAnimalHarm>(); Scribe_Collections.Loading=true; tracker.ExposeData(); Scribe_Collections.Loading=false;
            Check(!tracker.WasMedicalAwakeningBlocked(animal) && tracker.WasHarmed(animal),"purchase exemption survives save/load without erasing damage");
            Scribe_Collections.Values.Remove("wildShiftBlockedMedicalRecruits"); Current.Game=new Game();
            tracker=Current.Game.GetComponent<GameComponent_LatentAnimalHarm>(); Scribe_Collections.Loading=true; tracker.ExposeData(); Scribe_Collections.Loading=false;
            Check(!tracker.WasMedicalAwakeningBlocked(animal) && !OwnedAnimalAwakening.GetCommand(animal).disabled,"legacy owned purchases are not presumed medical recruits from harm alone");
        }
    }
}

namespace Verse
{
    public class TestItemList : List<Thing>
    {
        public readonly ThingOwner Owner;
        public TestItemList() { Owner=new ThingOwner { items=this }; }
        public new void Add(Thing item) { base.Add(item); item.holdingOwner=Owner; }
        public static implicit operator ThingOwner(TestItemList items) { return items.Owner; }
    }
}
namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static void RunFormPossessionsTests()
        {
            Pawn human; var map=new Map();
            Reset(); var animal=Form(out human,map);
            animal.apparel=new TestApparel(); var armor=new Thing(); animal.apparel.WornApparel.Add(armor);
            human.apparel=new TestApparel(); var humanClothes=new Thing(); human.apparel.WornApparel.Add(humanClothes);
            animal.inventory=new TestInventory(); var cargo=new Thing(); animal.inventory.innerContainer.Add(cargo);
            animal.equipment=new TestEquipment(); var weapon=new Thing(); animal.equipment.AllEquipmentListForReading.Add(weapon);
            animal.carryTracker=new TestCarryTracker { CarriedThing=new Thing() }; var carried=animal.carryTracker.CarriedThing;
            Check(TransformUtility.RevertToHuman(animal)==human && animal.Destroyed,"equipped form returns successfully");
            Check(new[]{armor,cargo,weapon,carried}.All(x=>TestDrops.Ground.Contains(x)) && TestDrops.Ground.Count==4,"every animal possession lands once on ground");
            Check(human.apparel.WornApparel.Contains(humanClothes) && !TestDrops.Ground.Contains(humanClothes),"original human clothing stays worn");

            Reset(); animal=Form(out human,map); animal.apparel=new TestApparel { Fail=true }; armor=new Thing(); animal.apparel.WornApparel.Add(armor);
            human.inventory=new TestInventory();
            Check(TransformUtility.RevertToHuman(animal)==human && human.Spawned && animal.Destroyed,"blocked ground does not prevent return when inventory accepts armor");
            Check(human.inventory.innerContainer.Count(x=>x==armor)==1 && !TestDrops.Ground.Contains(armor),"failed apparel drop preserves exact armor in human inventory");

            Reset(); animal=Form(out human,map); human.inventory=new TestInventory();
            animal.apparel=new TestApparel { Fail=true }; armor=new Thing(); animal.apparel.WornApparel.Add(armor);
            animal.inventory=new TestInventory(); cargo=new Thing(); animal.inventory.innerContainer.Add(cargo);
            Check(TransformUtility.RevertToHuman(animal)==human && TestDrops.Ground.Contains(cargo) && human.inventory.innerContainer.Contains(armor),"partial ground placement transfers only the remaining armor");
            Check(TestDrops.Ground.Count==1 && human.inventory.innerContainer.Count==1,"partial placement keeps exactly one copy per destination");

            Reset(); animal=Form(out human,map); human.inventory=new TestInventory();
            animal.inventory=new TestInventory { Throws=true }; cargo=new Thing(); animal.inventory.innerContainer.Add(cargo);
            Check(TransformUtility.RevertToHuman(animal)==human && human.inventory.innerContainer.Contains(cargo),"ground-drop exception falls back to inventory and returns");

            Reset(); animal=Form(out human,map); human.inventory=new TestInventory();
            animal.apparel=new TestApparel { Fail=true }; armor=new Thing(); animal.apparel.WornApparel.Add(armor); human.inventory.innerContainer.Owner.RejectItems=true;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.comp.StoredPawn==human && animal.apparel.WornApparel.Contains(armor),"both ground and inventory failure preserve the source");
            human.inventory.innerContainer.Owner.RejectItems=false;
            Check(TransformUtility.RevertToHuman(animal)==human && human.inventory.innerContainer.Count(x=>x==armor)==1,"dual-failure retry restores human without armor duplication");

            Reset(); animal=Form(out human,map); human.inventory=new TestInventory();
            animal.apparel=new TestApparel { Fail=true }; armor=new Thing(); animal.apparel.WornApparel.Add(armor); GenSpawn.FailBefore=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.comp.StoredPawn==human && human.inventory.innerContainer.Contains(armor),"failed human placement retains fallback armor with stored human");
            GenSpawn.FailBefore=null;
            Check(TransformUtility.RevertToHuman(animal)==human && human.inventory.innerContainer.Count(x=>x==armor)==1,"placement retry retains inventory fallback without duplication");

            Reset(); animal=Form(out human,map); human.inventory=new TestInventory();
            animal.apparel=new TestApparel { Fail=true }; armor=new Thing(); animal.apparel.WornApparel.Add(armor); WildShiftMod.Settings.deathChance=0f;
            Check(!Patch_TransformedDeath.Prefix(animal,null) && human.Spawned && !human.Dead && animal.Destroyed && human.inventory.innerContainer.Contains(armor),"lethal return with blocked ground uses inventory without world recovery");

            Reset(); animal=Form(out human,map); animal.apparel=new TestApparel(); armor=new Thing(); animal.apparel.WornApparel.Add(armor); GenSpawn.FailBefore=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null && TestDrops.Ground.Contains(armor) && animal.comp.StoredPawn==human,"placement failure after unload keeps armor on map");
            GenSpawn.FailBefore=null;
            Check(TransformUtility.RevertToHuman(animal)==human && TestDrops.Ground.Count(x=>x==armor)==1,"spawn retry does not duplicate dropped armor");

            Reset(); animal=Form(out human); var owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false);
            human.inventory=new TestInventory(); animal.apparel=new TestApparel(); armor=new Thing(); animal.apparel.WornApparel.Add(armor);
            animal.equipment=new TestEquipment(); weapon=new Thing(); animal.equipment.AllEquipmentListForReading.Add(weapon);
            animal.inventory=new TestInventory(); cargo=new Thing(); animal.inventory.innerContainer.Add(cargo);
            animal.carryTracker=new TestCarryTracker { CarriedThing=new Thing() }; carried=animal.carryTracker.CarriedThing;
            Check(TransformUtility.RevertToHuman(animal)==human && owner.Contains(human) && human.inventory.innerContainer.Contains(armor) && TestDrops.Ground.Count==0,"caravan clothing moves to preserved human inventory");
            Check(new[]{armor,weapon,cargo,carried}.All(x=>human.inventory.innerContainer.Contains(x)) && human.inventory.innerContainer.Count==4,"caravan return preserves all four possession trackers");

            Reset(); animal=Form(out human); owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false); human.inventory=new TestInventory();
            animal.apparel=new TestApparel(); armor=new Thing(); animal.apparel.WornApparel.Add(armor); human.inventory.innerContainer.Owner.RejectItems=true;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.apparel.WornApparel.Contains(armor) && animal.comp.StoredPawn==human,"silent inventory rejection keeps animal clothing and identity");
            human.inventory.innerContainer.Owner.RejectItems=false; owner.Reject=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null && owner.Contains(animal) && animal.comp.StoredPawn==human && human.inventory.innerContainer.Contains(armor),"body swap rejection preserves already transferred gear with stored human");
            owner.Reject=null;
            Check(TransformUtility.RevertToHuman(animal)==human && human.inventory.innerContainer.Count==1,"holder retry preserves moved gear exactly once");

            Reset(); animal=Form(out human); owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false); human.inventory=new TestInventory();
            animal.apparel=new TestApparel(); armor=new Thing(); animal.apparel.WornApparel.Add(armor); human.inventory.innerContainer.Owner.ThrowItemBefore=true;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.apparel.WornApparel.Contains(armor) && armor.holdingOwner!=null && animal.comp.StoredPawn==human,"transfer exception after detach restores armor ownership");
            human.inventory.innerContainer.Owner.ThrowItemBefore=false; human.inventory.innerContainer.Owner.ThrowItemAfter=true;
            Check(TransformUtility.RevertToHuman(animal)==null && human.inventory.innerContainer.Contains(armor) && animal.comp.StoredPawn==human,"post-add exception retains armor in stored human inventory");
            human.inventory.innerContainer.Owner.ThrowItemAfter=false;
            Check(TransformUtility.RevertToHuman(animal)==human && human.inventory.innerContainer.Count==1,"retry after transfer callback does not duplicate armor");

            Reset(); animal=Form(out human,map); animal.apparel=new TestApparel { Fail=true }; armor=new Thing(); animal.apparel.WornApparel.Add(armor);
            animal.comp.ReleaseStoredPawn();
            Check(!FormTransferUtility.DestroyEmptyForm(animal) && !animal.Destroyed && animal.apparel.WornApparel.Contains(armor),"emergency cleanup never destroys remaining animal apparel");

            Reset(); animal=Form(out human,map); animal.apparel=new TestApparel { Fail=true }; armor=new Thing(); animal.apparel.WornApparel.Add(armor); WildShiftMod.Settings.deathChance=0f;
            Check(Patch_TransformedDeath.Prefix(animal,null) && !animal.comp.HasStoredPawn && !animal.Destroyed && WorldPawnsUtility.IsWorldPawn(human),"failed lethal unload allows native corpse after emergency human recovery");
            Check(animal.apparel.WornApparel.Contains(armor),"lethal recovery retains armor on source for native death");
        }
    }
}

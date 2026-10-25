namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static AnimalFormStorage EnableFormStorage(Pawn human, Pawn animal)
        {
            human.shifter = new HediffComp_Shapeshifter { assignedKind = animal.kindDef };
            human.shifter.FormStorage = new AnimalFormStorage(human);
            animal.comp.sourceKind = animal.kindDef;
            return human.shifter.FormStorage;
        }

        private static void RunAnimalFormStorageTests()
        {
            var map=new Map(); Pawn human;
            Reset(); var animal=Form(out human,map); var storage=EnableFormStorage(human,animal);
            var implant=new Hediff_Implant { def=new HediffDef { defName="CustomImplant",isBad=true,chronic=true } };
            var addedPart=new Hediff_AddedPart { def=new HediffDef { defName="BionicLeg" } };
            addedPart.Part=new BodyPartRecord();
            var prostheticChild=new Hediff_MissingPart { def=new HediffDef { defName="ReplacedNaturalFoot" },Part=new BodyPartRecord {parent=addedPart.Part} };
            var injury=new Hediff_Injury { def=new HediffDef { defName="Cut" } };
            var missing=new Hediff_MissingPart { def=new HediffDef { defName="MissingPart" } };
            var blood=new Hediff { def=HediffDefOf.BloodLoss };
            var heatstroke=new Hediff { def=new HediffDef { defName="Heatstroke",isBad=true } };
            var projection=new Hediff { def=new HediffDef { defName="WildShift_SharedFormDrugEffect" } };
            var innate=new Hediff { def=new HediffDef { defName="OtherModInnateEffect" } };
            animal.health.hediffSet.hediffs.AddRange(new[] { implant,addedPart,injury,missing,blood,heatstroke,projection,innate,prostheticChild });
            Check(!storage.TryStore(animal,animal.kindDef) && animal.comp.StoredPawn==human,
                "cannot put an occupied animal inside its own stored human");
            animal.drafter.Drafted=true;
            Check(TransformUtility.RevertToHuman(animal)==human,"cached map reversion succeeds");
            Check(human.Spawned && human.Drafted && !animal.Spawned && !animal.Destroyed
                && storage.StoredForm==animal && AnimalFormStorage.IsStored(animal),"exact animal is owned by the returned human without a second active body");
            Check(!animal.comp.HasStoredPawn && !WorldPawnsUtility.IsWorldPawn(animal),"cached body owns no human and has no duplicate world registration");
            Check(animal.health.hediffSet.hediffs.Contains(implant) && animal.health.hediffSet.hediffs.Contains(addedPart)
                && !animal.health.hediffSet.hediffs.Contains(projection),"original implants survive caching while shared drug projection is removed");
            Check(animal.health.hediffSet.hediffs.Contains(injury),"reversion does not heal the stored animal before the next transformation");
            Check(storage.TryTake(animal.kindDef)==animal && !storage.HasStoredForm && animal.holdingOwner==null,
                "checkout detaches the exact original before it can hold its human");
            Check(!animal.health.hediffSet.hediffs.Contains(injury) && !animal.health.hediffSet.hediffs.Contains(missing)
                && !animal.health.hediffSet.hediffs.Contains(blood) && !animal.health.hediffSet.hediffs.Contains(heatstroke),"next transformation clears injuries missing parts lethal blood loss and temperature damage");
            Check(animal.health.hediffSet.hediffs.Contains(implant) && animal.health.hediffSet.hediffs.Contains(addedPart)
                && animal.health.hediffSet.hediffs.Contains(innate) && animal.health.hediffSet.hediffs.Contains(prostheticChild),"checkout keeps original upgrades their replaced-child anatomy and unrelated innate health");
            Check(animal.comp.TryStore(human) && !storage.HasStoredForm,"reverse ownership remains acyclic after checkout");
            GenSpawn.Spawn(animal,new IntVec3(),map);
            Check(TransformUtility.RevertToHuman(animal)==human && storage.StoredForm==animal,
                "repeated returns reuse one body rather than growing a cache");
            animal.health.lethal=true;
            Check(storage.TryTake(animal.kindDef)==null && storage.StoredForm==animal && !animal.Destroyed,
                "unresolved fatal mod health cannot detach and destroy the cached upgrade body during spawn");
            animal.health.lethal=false;
            var otherKind=new PawnKindDef();
            Check(!storage.Matches(otherKind) && storage.TryTake(otherKind)==null && storage.StoredForm==animal,
                "different species cannot overwrite or discard an existing upgraded body");

            Reset(); animal=Form(out human,map); storage=EnableFormStorage(human,animal);
            GenSpawn.FailBefore=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.Spawned && animal.comp.StoredPawn==human
                && !storage.HasStoredForm,"failed human placement leaves the active body and ownership graph intact");
            GenSpawn.FailBefore=null;
            Check(TransformUtility.RevertToHuman(animal)==human && storage.StoredForm==animal,"cached reversion retries after map placement recovers");

            Reset(); animal=Form(out human,map); storage=EnableFormStorage(human,animal); animal.drafter.Drafted=true;
            var cacheOwner=storage.GetDirectlyHeldThings(); cacheOwner.Reject=p=>p==animal;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.Spawned && !human.Spawned
                && animal.comp.StoredPawn==human && !storage.HasStoredForm,"cache insertion failure rolls the placed human back into the original body");
            cacheOwner.Reject=null;
            Check(TransformUtility.RevertToHuman(animal)==human && storage.StoredForm==animal,"return can retry after cache insertion recovers");

            Reset(); animal=Form(out human); storage=EnableFormStorage(human,animal); human.drafter=null;
            var caravan=new ThingOwner(); caravan.TryAddOrTransfer(animal,false);
            Find.WorldPawns.PassToWorld(animal,PawnDiscardDecideMode.KeepForever);
            Check(TransformUtility.RevertToHuman(animal)==human && caravan.Contains(human)
                && WorldPawnsUtility.IsWorldPawn(human) && storage.StoredForm==animal && !WorldPawnsUtility.IsWorldPawn(animal),
                "caravan caches original form while retaining only its human in the owner and world registry");
            Check(!human.Drafted && animal.holdingOwner==storage.GetDirectlyHeldThings(),"off-map return stays undrafted and has one cache owner");

            Reset(); animal=Form(out human); storage=EnableFormStorage(human,animal);
            caravan=new ThingOwner(); caravan.TryAddOrTransfer(animal,false);
            Find.WorldPawns.PassToWorld(animal,PawnDiscardDecideMode.KeepForever);
            storage.GetDirectlyHeldThings().Reject=p=>p==animal;
            Check(TransformUtility.RevertToHuman(animal)==null && caravan.Contains(animal)
                && WorldPawnsUtility.IsWorldPawn(animal) && !WorldPawnsUtility.IsWorldPawn(human)
                && animal.comp.StoredPawn==human,"failed caravan caching restores original member and world registration");

            Reset(); animal=Form(out human); storage=EnableFormStorage(human,animal);
            Find.WorldPawns.PassToWorld(animal,PawnDiscardDecideMode.KeepForever);
            Check(TransformUtility.RevertToHuman(animal)==human && storage.StoredForm==animal
                && WorldPawnsUtility.IsWorldPawn(human) && !WorldPawnsUtility.IsWorldPawn(animal),"loose world return also caches one original body without dual registration");

            Reset(); animal=Form(out human,map); storage=EnableFormStorage(human,animal);
            Check(!Patch_TransformedDeath.Prefix(animal,null) && !human.Dead && storage.StoredForm==animal
                && !animal.Destroyed && !animal.Dead,"animal death survival caches its body and suppresses native killing of the preserved form");
            Check(!Patch_TransformedDeath.Prefix(animal,null) && storage.StoredForm==animal,
                "repeated native kill callback cannot kill an inactive preserved form");

            Reset(); animal=Form(out human,map); storage=EnableFormStorage(human,animal);
            Check(!Patch_TransformedDeath.Prefix(animal,new DamageInfo(DamageDefOf.ExecutionCut,100))
                && human.Dead && storage.StoredForm==animal,"slaughter still kills the human after storing the original animal body");

            Reset(); animal=Form(out human,map); storage=EnableFormStorage(human,animal);
            var existing=new Pawn { comp=new HediffComp_Transformed() };
            Check(storage.TryStore(existing,animal.kindDef),"conflicting cache fixture stores an independent empty form");
            Check(TransformUtility.RevertToHuman(animal)==null && animal.comp.StoredPawn==human
                && animal.Spawned && storage.StoredForm==existing,"occupied cache rejects return before either live body is moved or deleted");

            Reset(); animal=OwnedLatentAnimal(); human=new Pawn(); PawnGenerator.Generate=()=>human;
            var originalUpgrade=new Hediff_Implant { def=new HediffDef { defName="PurchasedOriginalUpgrade" } };
            animal.health.hediffSet.hediffs.Add(originalUpgrade);
            OwnedAnimalAwakening.TryAwaken(animal);
            Check(AnimalFormStorage.IsStored(animal) && human.shifter.FormStorage.StoredForm==animal
                && animal.health.hediffSet.hediffs.Contains(originalUpgrade) && human.Spawned,
                "purchased awakening preserves the actual original upgraded animal");
            storage=human.shifter.FormStorage;
            var originalKind=animal.kindDef;
            Check(storage.TryTake(originalKind)==animal && animal.comp.TryStore(human)
                && !Patch_Taming.IsLatentWildShapeshifter(animal) && OwnedAnimalAwakening.GetCommand(animal)==null,
                "reactivated original cannot awaken a second human even with a stale latent marker");

            Reset(); animal=OwnedLatentAnimal(); human=new Pawn(); human.shifter=new HediffComp_Shapeshifter(human);
            human.shifter.FormStorage.GetDirectlyHeldThings().Reject=p=>p==animal;
            PawnGenerator.Generate=()=>human; OwnedAnimalAwakening.TryAwaken(animal);
            Check(animal.Spawned && !animal.Destroyed && animal.comp==null && human.Destroyed
                && Patch_Taming.IsLatentWildShapeshifter(animal),"failed initial cache adoption preserves latent animal and rolls back candidate human");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    public static class SharedGearEngineChecks
    {
        public static void Run(Map map, Action<bool,string> check)
        {
            Anatomy(map,check);
            Gear(map,check);
            Armor(map,check);
            PersonalThoughts(map,check);
        }
        private static Pawn Human(Map map,string label)
        {
            Pawn p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
                forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,
                fixedGender:Gender.Male,fixedBiologicalAge:25f,forcedXenotype:XenotypeDefOf.Baseliner));
            p.Name=new NameSingle(label);p.health.RemoveAllHediffs();
            foreach(Trait t in p.story.traits.allTraits.ToList())p.story.traits.RemoveTrait(t);
            foreach(Gene g in p.genes.GenesListForReading.ToList())p.genes.RemoveGene(g);
            GenSpawn.Spawn(p,CellFinder.StandableCellNear(map.Center,map,12f),map);
            return p;
        }
        private static Pawn Shift(Map map,Pawn human)
        {
            PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Warg");
            TransformUtility.AddOrGetShapeshifter(human,kind);
            if(!TransformUtility.TransformToAnimal(human,kind))throw new InvalidOperationException("gear/shift failed");
            return map.mapPawns.AllPawnsSpawned.Single(p=>TransformUtility.TryGetTransformedComp(p)?.StoredPawn==human);
        }
        private static void Anatomy(Map map,Action<bool,string> check)
        {
            Pawn human=Human(map,"SharedHumanProstheses");
            var arms=human.RaceProps.body.AllParts.Where(p=>p.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbCore)).ToList();
            Require(arms.Count==2,"anatomy/human has two manipulation limbs",check);
            HediffDef archotech=DefDatabase<HediffDef>.GetNamed("ArchotechArm");
            foreach(BodyPartRecord part in arms)human.health.AddHediff(archotech,part);
            float enhanced=human.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
            Pawn body=Shift(map,human);
            float projected=body.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
            check(enhanced>1.4f&&Math.Abs(projected-enhanced)<.02f,"anatomy/real human archotech arm performance reaches animal manipulation");
            check(human.health.hediffSet.hediffs.Count(h=>h.def==archotech)==2
                && !body.health.hediffSet.hediffs.Any(h=>h is Hediff_AddedPart&&h.def==archotech),"anatomy/source prostheses remain on original body without copied physical limbs");
            Gene fingers=human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("ElongatedFingers"),false);
            human.health.hediffSet.DirtyCache();
            body.health.hediffSet.DirtyCache();
            float combinedHuman=human.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
            float combinedBody=body.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
            // Native elongated fingers is a 1.1 postFactor, so two 150%
            // archotech arms correctly combine to 165%, rather than 160%.
            check(fingers.Active&&Math.Abs(combinedHuman-1.65f)<.011f&&Math.Abs(combinedBody-combinedHuman)<.011f,
                "anatomy/native elongated-fingers factor and source archotech arms combine once to165percent");
            var counterfactual=new HediffSet(body);
            check(Math.Abs(PawnCapacityUtility.CalculateCapacityLevel(counterfactual,PawnCapacityDefOf.Manipulation)-1.1f)<.011f,
                "anatomy/counterfactual animal health set does not receive source installation scaling");
            HediffDef offsetDef=new HediffDef
            {
                defName="WildShift_TestAnatomyNegativeOffset",label="fixture negative manipulation offset",
                hediffClass=typeof(HediffWithComps),isBad=true,
                modExtensions=new List<DefModExtension>{new SharedFormBuffExtension()},
                stages=new List<HediffStage>{new HediffStage{capMods=new List<PawnCapacityModifier>
                    {new PawnCapacityModifier{capacity=PawnCapacityDefOf.Manipulation,offset=-.2f}}}}
            };
            Hediff offset=HediffMaker.MakeHediff(offsetDef,human);
            human.health.AddHediff(offset);
            try
            {
                SharedFormHealth.Refresh(body);
                float reducedHuman=human.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
                float reducedBody=body.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
                check(Math.Abs(reducedHuman-1.43f)<.011f&&Math.Abs(reducedBody-reducedHuman)<.011f,
                    "anatomy/negative capacity offset follows raw prosthesis anatomy before native gene factors:143percent");
            }
            finally
            {
                human.health.RemoveHediff(offset);
                SharedFormHealth.Refresh(body);
            }
            check(Math.Abs(body.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation)-combinedHuman)<.011f,
                "anatomy/removing the source offset restores the combined native capacity immediately");
            Require(TransformUtility.RevertToHuman(body,false)==human,"anatomy/return succeeds",check);
            check(human.health.hediffSet.hediffs.Count(h=>h.def==archotech)==2,"anatomy/return preserves both exact source installations");
            Cleanup(human);
        }
        private static void Gear(Map map,Action<bool,string> check)
        {
            Pawn human=Human(map,"SharedHumanGear");
            Apparel shield=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_ShieldBelt"));
            ThingWithComps rifle=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle"));
            human.apparel.Wear(shield);human.equipment.AddEquipment(rifle);
            CompBiocodable coding=rifle.GetComp<CompBiocodable>();
            Require(coding!=null,"gear/native rifle has biocoding component",check);
            coding.CodeFor(human);
            ThingOwner apparelOwner=shield.holdingOwner,weaponOwner=rifle.holdingOwner;
            Pawn body=Shift(map,human);
            check(body.apparel.WornApparel.Contains(shield)&&body.equipment.Primary==rifle,"gear/animal native apparel and primary-weapon views expose original items");
            using(SharedFormGear.NativeOnly())
                check(!body.apparel.WornApparel.Contains(shield),"gear/physical rendering view excludes borrowed human garments while retaining native ownership");
            check(shield.holdingOwner==apparelOwner&&rifle.holdingOwner==weaponOwner,"gear/non-owning views preserve original ThingOwner identity");
            check(shield.Wearer==body&&rifle.GetComp<CompEquippable>().PrimaryVerb.CasterPawn==body,"gear/shield wearer and weapon caster are active animal");
            check(CompBiocodable.IsBiocodedFor(rifle,body)&&coding.CodedPawn==human,
                "gear/biocoded weapon recognizes active form while retaining canonical human coding");
            CompShield shieldComp=shield.GetComp<CompShield>();
            Require(shieldComp!=null,"gear/native shield component exists",check);
            AccessTools.Field(typeof(CompShield),"energy").SetValue(shieldComp,1f);
            float before=shieldComp.Energy;
            var result=body.TakeDamage(new DamageInfo(DamageDefOf.Bullet,5f,0f,hitPart:body.RaceProps.body.corePart));
            check(shieldComp.Energy<before&&result.totalDamageDealt==0f,"gear/native shield on preserved item absorbs animal's projectile damage and spends source energy once");
            Require(TransformUtility.RevertToHuman(body,false)==human,"gear/return succeeds",check);
            check(human.apparel.WornApparel.Contains(shield)&&human.equipment.Primary==rifle
                &&shield.holdingOwner==apparelOwner&&rifle.holdingOwner==weaponOwner,"gear/return preserves original clothes and weapon instead of unloading borrowed views");
            check(shield.Wearer==human&&rifle.GetComp<CompEquippable>().PrimaryVerb.CasterPawn==human,"gear/return restores wearer and weapon caster to human");
            body=Shift(map,human);
            Apparel dropped;
            Require(body.apparel.TryDrop(shield,out dropped,body.Position,false),"gear/active-body drop routes original item through canonical tracker",check);
            check(dropped==shield&&shield.Spawned&&!human.apparel.WornApparel.Contains(shield),
                "gear/native drop preserves exact shield object and removes its original owner once");
            Require(TransformUtility.RevertToHuman(body,false)==human,"gear/return after source-gear drop succeeds",check);
            check(shield.Spawned&&human.equipment.Primary==rifle,"gear/return neither reclaims dropped human gear nor unloads original weapon");
            if(!shield.Destroyed)shield.Destroy(DestroyMode.Vanish);
            Cleanup(human);
        }
        private static void Cleanup(Pawn human)
        {
            Pawn body=SharedFormIdentity.ActiveFormFor(human);
            if(body!=null)TransformUtility.RevertToHuman(body,false);
            if(!human.Destroyed)human.Destroy(DestroyMode.Vanish);
        }
        private static void PersonalThoughts(Map map,Action<bool,string> check)
        {
            Pawn human=Human(map,"SharedThoughtIdentity"),partner=Human(map,"SharedThoughtPartner");
            human.relations.AddDirectRelation(PawnRelationDefOf.Spouse,partner);
            partner.relations.everSeenByPlayer=true;
            ThoughtDef thought=DefDatabase<ThoughtDef>.AllDefsListForReading.First(d=>d.workerClass==typeof(ThoughtWorker_WantToSleepWithSpouseOrLover));
            bool before=thought.Worker.CurrentState(human).Active;
            Pawn body=Shift(map,human);
            Pawn_RelationsTracker ownRelations=body.relations;
            Pawn_IdeoTracker ownIdeo=body.ideo;
            body.relations.AddDirectRelation(PawnRelationDefOf.Bond,partner);
            int errors=Log.Messages.Count(m=>m.type==LogMessageType.Error);
            body.needs.mood.thoughts.situational.Notify_SituationalThoughtsDirty();
            body.needs.mood.thoughts.situational.SituationalThoughtInterval();
            check(Log.Messages.Count(m=>m.type==LogMessageType.Error)==errors,
                "persona/direct native situational recalc outside total-mood wrapper retains precept creation context");
            ThoughtDef socialPrecept=DefDatabase<ThoughtDef>.AllDefsListForReading
                .First(d=>typeof(ThoughtWorker_Precept_Social).IsAssignableFrom(d.workerClass));
            string description=socialPrecept.Worker.PostProcessDescription(body,"persona native precept");
            string label=socialPrecept.Worker.PostProcessLabel(body,"persona native precept");
            check(description.Contains(human.Ideo.name)&&!string.IsNullOrEmpty(label),
                "persona/native precept description and label resolve original ideology outside thought-state queries");
            check(body.relations==ownRelations&&body.ideo==ownIdeo
                &&body.relations.DirectRelationExists(PawnRelationDefOf.Bond,partner),
                "persona/handler and text query scopes restore the animal's actual bond and ideology");
            float moodOffset=body.needs.mood.thoughts.TotalMoodOffset();
            check(body.styleObserver!=null&&body.surroundings!=null&&!float.IsNaN(moodOffset)&&!float.IsInfinity(moodOffset),
                "persona/native full mood calculation has active-body style observation and personal tracker context");
            check(thought.Worker.CurrentState(body).Active==before&&before,
                "persona/native spouse thought retains original relationship while evaluating active animal's bed and map");
            check(body.relations==ownRelations&&body.ideo==ownIdeo
                &&body.relations.DirectRelationExists(PawnRelationDefOf.Bond,partner)
                &&!body.relations.DirectRelationExists(PawnRelationDefOf.Spouse,partner),
                "persona/thought context restores body-native bond and ideology without copying personal relationships");
            using(new SharedFormThoughts.QueryScope(body,null))
                check(body.Ideo==human.Ideo&&body.Map==map&&body.relations==human.relations,
                    "persona/personal ideology and relations coexist with physical world context during thought calculation");
            Pawn partnerBody=Shift(map,partner);
            using(new SharedFormThoughts.QueryScope(body,partnerBody))
                check(PawnRelationDefOf.Spouse.Worker.InRelation(body,partnerBody),
                    "persona/social relation workers recognize both transformed spouses by canonical identity");
            Require(TransformUtility.RevertToHuman(body,false)==human,"persona/return succeeds",check);
            check(human.relations.DirectRelationExists(PawnRelationDefOf.Spouse,partner),"persona/original spouse identity survives thought queries and return");
            Cleanup(human);Cleanup(partner);
        }
        private static void Armor(Map map,Action<bool,string> check)
        {
            Pawn human=Human(map,"SharedArmorCoverage");
            Apparel armor=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmor"));
            human.apparel.Wear(armor);
            Pawn body=Shift(map,human);
            Pawn ordinary=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Warg"),Faction.OfPlayer);
            ordinary.health.RemoveAllHediffs();
            GenSpawn.Spawn(ordinary,CellFinder.StandableCellNear(map.Center,map,12f),map);
            float armored=0f,naked=0f;
            for(int i=0;i<16;i++)
            {
                Rand.PushState(9010+i);
                try{DamageDef d=DamageDefOf.Bullet;bool a,b;armored+=ArmorUtility.GetPostArmorDamage(body,10f,0f,body.RaceProps.body.corePart,ref d,out a,out b);}
                finally{Rand.PopState();}
                Rand.PushState(9010+i);
                try{DamageDef d=DamageDefOf.Bullet;bool a,b;naked+=ArmorUtility.GetPostArmorDamage(ordinary,10f,0f,ordinary.RaceProps.body.corePart,ref d,out a,out b);}
                finally{Rand.PopState();}
            }
            check(armored<naked,"gear/borrowed human torso armor actually reduces native animal-core damage despite different bodypart groups");
            // Modded humanlike animals may share the person's exact BodyDef.
            // Exercise coverage with that anatomy without mutating a DefDatabase entry.
            ThingDef originalDef=body.def;
            var clone=AccessTools.Method(typeof(object),"MemberwiseClone");
            ThingDef sharedBodyDef=(ThingDef)clone.Invoke(originalDef,null);
            sharedBodyDef.race=(RaceProperties)clone.Invoke(originalDef.race,null);
            sharedBodyDef.race.body=human.RaceProps.body;
            Pawn previous=null;
            try
            {
                body.def=sharedBodyDef;
                previous=SharedFormGear.BeginCoverage(body);
                check(armor.def.apparel.CoversBodyPart(human.RaceProps.body.corePart),
                    "gear/shared human-animal BodyDef coverage terminates and retains native torso protection");
            }
            finally {SharedFormGear.EndCoverage(previous);body.def=originalDef;}
            Require(TransformUtility.RevertToHuman(body,false)==human,"gear/armored return succeeds",check);
            check(human.apparel.WornApparel.Contains(armor)&&!armor.Destroyed,"gear/native armor damage stays on original item and it remains worn after return");
            if(!ordinary.Destroyed)ordinary.Destroy(DestroyMode.Vanish);
            Cleanup(human);
        }
        private static void Require(bool ok,string label,Action<bool,string> check)
        {check(ok,label);if(!ok)throw new InvalidOperationException(label);}
    }
}

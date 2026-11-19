using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY: real shared getters and native gear/stat calculations. A
    // native role supplies a non-null projected stage, making unnecessary
    // stage rebuilding observable without reflecting private cache fields.
    public static class SharedModifierCacheEngineChecks
    {
        public static void Run(Map map,Action<bool,string> check)
        {
            Pawn human=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
                forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,
                fixedGender:Gender.Male,fixedBiologicalAge:25f,forcedXenotype:XenotypeDefOf.Baseliner));
            human.Name=new NameSingle("SharedModifierCache");
            human.health.RemoveAllHediffs();
            foreach(Trait trait in human.story.traits.allTraits.ToList())human.story.traits.RemoveTrait(trait);
            foreach(Gene gene in human.genes.GenesListForReading.ToList())human.genes.RemoveGene(gene);
            GenSpawn.Spawn(human,CellFinder.StandableCellNear(map.Center,map,12f),map);
            Pawn body=null;
            Ideo ideo=human.Ideo;
            Precept_Role role=null;
            CompBladelinkWeapon link=null;
            WeaponTraitDef extra=new WeaponTraitDef
            {
                defName="WildShift_TestModifierCacheMoveSpeed",
                equippedStatOffsets=new List<StatModifier>{new StatModifier{stat=StatDefOf.MoveSpeed,value=.375f}}
            };
            try
            {
                Require(ideo!=null,"modifier-cache/native fixture has an ideology",check);
                role=(Precept_Role)PreceptMaker.MakePrecept(DefDatabase<PreceptDef>.GetNamed("IdeoRole_MeleeSpecialist"));
                ideo.AddPrecept(role,true);
                role.Assign(human,false);
                Apparel armor=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmor"));
                human.apparel.Wear(armor,false);
                ThingDef bladeDef=DefDatabase<ThingDef>.AllDefsListForReading.First(d=>
                    d.equipmentType==EquipmentType.Primary&&d.comps!=null
                    &&d.comps.Any(c=>c.compClass==typeof(CompBladelinkWeapon)));
                ThingWithComps blade=(ThingWithComps)ThingMaker.MakeThing(bladeDef);
                human.equipment.AddEquipment(blade);
                link=blade.GetComp<CompBladelinkWeapon>();
                PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Warg");
                TransformUtility.AddOrGetShapeshifter(human,kind);
                Require(TransformUtility.TransformToAnimal(human,kind),"modifier-cache/real transform succeeds",check);
                body=SharedFormIdentity.ActiveFormFor(human);
                Require(body!=null,"modifier-cache/active body is registered",check);
                Hediff carrier=TransformUtility.TryGetTransformedComp(body).parent;
                HediffStage nativeStage=carrier.CurStage;
                Require(nativeStage!=null&&nativeStage.statOffsets!=null,
                    "modifier-cache/native specialist role creates an observable shared stage",check);
                int tick=Find.TickManager.TicksGame;
                int nativeProjected;
                int nativeVersion=SharedFormSystems.ModifierVersion(body,out nativeProjected);
                armor.HitPoints=Math.Max(1,armor.HitPoints-1);
                int projected;
                check(SharedFormSystems.ModifierVersion(body,out projected)!=nativeVersion&&projected==nativeProjected
                    &&ReferenceEquals(nativeStage,carrier.CurStage),
                    "modifier-cache/native-visible armor hit invalidates native stat version while preserving projected stage allocation");
                CompQuality quality=armor.GetComp<CompQuality>();
                Require(quality!=null,"modifier-cache/native armor has quality",check);
                quality.SetQuality(QualityCategory.Awful,ArtGenerationContext.Outsider);
                float weakArmor=armor.GetStatValue(StatDefOf.ArmorRating_Sharp,cacheStaleAfterTicks:0);
                quality.SetQuality(QualityCategory.Legendary,ArtGenerationContext.Outsider);
                float strongArmor=armor.GetStatValue(StatDefOf.ArmorRating_Sharp,cacheStaleAfterTicks:0);
                check(strongArmor>weakArmor&&SharedFormSystems.ModifierVersion(body,out projected)!=nativeVersion&&projected==nativeProjected
                    &&ReferenceEquals(nativeStage,carrier.CurStage),
                    "modifier-cache/native armor quality still changes real protection without rebuilding unrelated shared modifiers");
                float speed=body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
                link.TraitsListForReading.Add(extra);
                float faster=body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000);
                check(faster>speed+.2f&&SharedFormSystems.ModifierVersion(body,out projected)!=nativeVersion&&projected==nativeProjected
                    &&ReferenceEquals(nativeStage,carrier.CurStage),
                    "modifier-cache/native-visible persona trait invalidates a long-lived animal stat cache immediately without rebuilding the shared stage");
                link.TraitsListForReading.Remove(extra);
                check(Math.Abs(body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000)-speed)<.001f,
                    "modifier-cache/native-visible persona modifier removal repairs the same long-lived stat cache in the same tick");
                link.TraitsListForReading.Add(extra);
                float uncachedSpeed=body.GetStatValue(StatDefOf.MoveSpeed);
                link.TraitsListForReading.Remove(extra);
                check(uncachedSpeed>speed+.2f&&Math.Abs(body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:10000)-speed)<.001f,
                    "modifier-cache/native ignore-cache writes cannot leave stale persona stats for the next same-tick cached read");
                nativeVersion=SharedFormSystems.ModifierVersion(body,out nativeProjected);

                // NativeOnly is also used by real physical-only consumers.
                // It hides borrowed gear from the body's getter while leaving
                // ownership and the canonical source items untouched.
                using(SharedFormGear.NativeOnly())
                {
                    int fallbackProjected;
                    int fallbackVersion=SharedFormSystems.ModifierVersion(body,out fallbackProjected);
                    HediffStage fallbackStage=carrier.CurStage;
                    float fallbackSpeed=fallbackStage.statOffsets.GetStatOffsetFromList(StatDefOf.MoveSpeed);
                    check(fallbackProjected!=nativeProjected&&!ReferenceEquals(fallbackStage,nativeStage),
                        "modifier-cache/hiding borrowed gear activates fallback projection in the same tick");
                    link.TraitsListForReading.Add(extra);
                    HediffStage changedStage=carrier.CurStage;
                    check(SharedFormSystems.ModifierVersion(body)!=fallbackVersion
                        &&!ReferenceEquals(changedStage,fallbackStage)
                        &&Math.Abs(changedStage.statOffsets.GetStatOffsetFromList(StatDefOf.MoveSpeed)-fallbackSpeed-.375f)<.001f,
                        "modifier-cache/real fallback persona modifier change invalidates and rebuilds immediately");
                    link.TraitsListForReading.Remove(extra);
                    check(SharedFormSystems.ModifierVersion(body)==fallbackVersion
                        &&Math.Abs(carrier.CurStage.statOffsets.GetStatOffsetFromList(StatDefOf.MoveSpeed)-fallbackSpeed)<.001f,
                        "modifier-cache/removing fallback modifier restores the exact original contribution in the same tick");
                }
                check(SharedFormSystems.ModifierVersion(body)==nativeVersion
                    &&Math.Abs(body.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:0)-speed)<.001f
                    &&Find.TickManager.TicksGame==tick,
                    "modifier-cache/restoring native item visibility removes fallback without elapsed ticks or duplicate stats");
            }
            finally
            {
                link?.TraitsListForReading.Remove(extra);
                if(body!=null&&SharedFormIdentity.HumanFor(body)==human)TransformUtility.RevertToHuman(body,false);
                if(role!=null)
                {
                    role.Unassign(human,false);
                    // This temporary fixture precept is removed below. Native
                    // unassignment creates a role-lost memory referencing it;
                    // a destroyed fixture pawn can remain in dead world pawns
                    // and otherwise save that now-unsaved reference.
                    human.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(DefDatabase<ThoughtDef>.GetNamed("IdeoRoleLost"));
                    ideo.RemovePrecept(role);
                }
                if(!human.Destroyed)human.Destroy(DestroyMode.Vanish);
            }
        }
        private static void Require(bool ok,string label,Action<bool,string> check)
        {check(ok,label);if(!ok)throw new InvalidOperationException(label);}
    }
}

using System;
using System.Linq;
using RimWorld;
using Verse;

namespace WildShiftCombatPerformance
{
    // TEST ONLY: actual native objects and effects, no result replacement.
    internal static class StressLoadout
    {
        public static void Apply(Pawn human,string loadout)
        {
            foreach(string name in new[]{"Apparel_Pants","Apparel_CollarShirt","Apparel_ArmorCataphract","Apparel_ArmorHelmetCataphract"})
            {
                ThingDef def=DefDatabase<ThingDef>.GetNamed(name);
                Apparel item=(Apparel)ThingMaker.MakeThing(def,def.MadeFromStuff?DefDatabase<ThingDef>.GetNamed("Hyperweave"):null);
                item.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent,ArtGenerationContext.Outsider);
                human.apparel.Wear(item,false);
                if(item.holdingOwner!=human.apparel.GetDirectlyHeldThings())throw new InvalidOperationException("Native stress armor could not be worn: "+name);
            }
            // A shield belt blocks native firearm use. Include it only in the
            // unarmed case; jump loadouts retain their own belt/charge test.
            if(loadout=="unarmed")human.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_ShieldBelt")),false);
            Install(human,"ArchotechArm",BodyPartTagDefOf.ManipulationLimbCore);
            Install(human,"ArchotechLeg",BodyPartTagDefOf.MovingLimbCore);
            Install(human,"ArchotechEye",BodyPartTagDefOf.SightSource);
            foreach(string name in new[]{"LuciferiumHigh","GoJuiceHigh","NeuralSupercharge","JuggernautSerum","Metalblood","MindNumbSerum"})
            {
                HediffDef def=DefDatabase<HediffDef>.GetNamed(name);
                Hediff source=human.health.AddHediff(def);
                if(!WildShift.SharedFormHealth.IsSupported(source))throw new InvalidOperationException("Unreviewed native stress effect: "+name);
            }
        }
        private static void Install(Pawn human,string name,BodyPartTagDef tag)
        {
            var parts=human.RaceProps.body.AllParts.Where(p=>p.def.tags.Contains(tag)).ToList();
            if(parts.Count==0)throw new InvalidOperationException("Stress anatomy has no "+tag.defName);
            foreach(BodyPartRecord part in parts)human.health.AddHediff(DefDatabase<HediffDef>.GetNamed(name),part);
        }
        public static object Snapshot(Pawn human){return new{Armor=human.apparel.WornApparel.Select(a=>new{Id=a.GetUniqueLoadID(),Def=a.def.defName,HP=a.HitPoints,OwnerIsOriginal=a.holdingOwner==human.apparel.GetDirectlyHeldThings()}).ToArray(),Effects=human.health.hediffSet.hediffs.Select(h=>new{Id=h.GetUniqueLoadID(),Def=h.def.defName,Part=h.Part?.def.defName,Severity=h.Severity,OriginalOwner=h.pawn==human}).ToArray(),Scope="Native excellent cataphract armor, archotech limbs/eyes, six supported native personal effects; shield only for unarmed, to preserve native firearm eligibility."};}
    }
}

namespace HarmonyLib {
    public static class AccessTools {
        public static Type TypeByName(string name) { return typeof(AlienRace.RaceRestrictionSettings); }
        public static System.Reflection.MethodInfo Method(Type type,string name,Type[] args) { return type.GetMethod(name,args); }
    }
}
namespace AlienRace {
    public static class RaceRestrictionSettings {
        public static bool CanUseXenotype(XenotypeDef x,ThingDef race) { return x.defName!="RatkinGenes" || race.defName=="Ratkin"; }
    }
}
namespace WildShift.Tests {
    public static class AwakeningRaceSelectionTests {
        private static int checks;
        private static void Check(bool result,string label) { checks++; if(!result) throw new Exception(label); }
        public static string Run() {
            var human=PawnKindDefOf.Colonist;
            var ratkin=new PawnKindDef {defName="RatkinColonist",race=new ThingDef {defName="Ratkin",race=new RaceProperties {Humanlike=true}},defaultFactionDef=new FactionDef {isPlayer=true}};
            var kiiro=new PawnKindDef {defName="KiiroColonist",race=new ThingDef {defName="Kiiro",race=new RaceProperties {Humanlike=true}},defaultFactionDef=new FactionDef {isPlayer=true}};
            var pig=new XenotypeDef {defName="Pigskin"}; var ratGenes=new XenotypeDef {defName="RatkinGenes"};
            ratkin.xenotypes=new Dictionary<XenotypeDef,float> {{ratGenes,1}};
            DefDatabase<PawnKindDef>.AllDefsListForReading=new List<PawnKindDef> {human,ratkin,kiiro};
            DefDatabase<XenotypeDef>.AllDefsListForReading=new List<XenotypeDef> {XenotypeDefOf.Baseliner,pig,ratGenes};
            Faction.OfPlayer=new Faction {def=new FactionDef {isPlayer=true,basicMemberKind=ratkin},xenotypes=new Dictionary<XenotypeDef,float>{{ratGenes,1}}};
            ModsConfig.BiotechActive=true; Rand.Force=true;
            var request=AwakeningRaceSelection.CreateRequest(Gender.Female);
            Check(request.KindDef==ratkin && request.Faction==Faction.OfPlayer && request.ForcedXenotype==null,"player half preserves faction kind and native genes");
            Check(request.FixedGender==Gender.Female && request.ForceGenerateNewPawn,"selected race preserves animal gender and fresh generation");
            Check(AnimalFormGender.MatchesHumanRequest(new Pawn {def=ratkin.race},request),"expected generated race accepted");
            Check(!AnimalFormGender.MatchesHumanRequest(new Pawn {def=human.race},request),"unexpected generated race rejected");
            Rand.Force=false; Rand.ForceIndex=0;
            request=AwakeningRaceSelection.CreateRequest(Gender.Male);
            Check(request.KindDef==human && request.Faction==null && request.ForcedXenotype==XenotypeDefOf.Baseliner,"human alternative bypasses HAR player faction replacement and ratkin faction genes");
            Rand.ForceIndex=1; request=AwakeningRaceSelection.CreateRequest(null);
            Check(request.ForcedXenotype==pig && request.KindDef==human,"vanilla xenotype is an independent selectable alternative");
            Rand.ForceIndex=2; request=AwakeningRaceSelection.CreateRequest(null);
            Check(request.KindDef==kiiro && request.ForcedXenotype==XenotypeDefOf.Baseliner,"other body race uses its own default genes without player faction influence");
            for(int i=0;i<50;i++) DefDatabase<PawnKindDef>.AllDefsListForReading.Add(new PawnKindDef {defName="KiiroSoldier"+i,race=kiiro.race,isFighter=true,combatPower=100});
            Check(AwakeningRaceSelection.CreateRequest(null).KindDef==kiiro,"many job definitions do not replace preferred civilian representative");
            foreach(string name in new[] {"Boss","Mutant","Mech","Entity","Drone"}) {
                var k=new PawnKindDef {defName=name,race=new ThingDef {defName=name,race=new RaceProperties {Humanlike=true}}};
                k.isBoss=name=="Boss"; k.mutant=name=="Mutant"?new object():null; k.race.race.IsMechanoid=name=="Mech"; k.race.race.IsAnomalyEntity=name=="Entity"; k.race.race.IsDrone=name=="Drone";
                DefDatabase<PawnKindDef>.AllDefsListForReading.Add(k);
            }
            var counts=new Dictionary<string,int>(); Rand.Force=null; Rand.ForceIndex=null;
            for(int i=0;i<20000;i++) { request=AwakeningRaceSelection.CreateRequest(null); string key=request.KindDef.race.defName+"/"+(request.ForcedXenotype?.defName??""); if(!counts.ContainsKey(key)) counts[key]=0; counts[key]++; }
            Check(counts.Count==4,"special entities and player-race xenotype alias never enter alternative pool");
            Check(counts["Ratkin/"]>9600 && counts["Ratkin/"]<10400,"player faction race is approximately fifty percent");
            foreach(string key in new[] {"Human/Baseliner","Human/Pigskin","Kiiro/Baseliner"}) Check(counts[key]>3000 && counts[key]<3700,"alternatives uniformly share the other half: "+key);
            Faction.OfPlayer.def.basicMemberKind=human; Faction.OfPlayer.xenotypes=null; Rand.Force=false; Rand.ForceIndex=0;
            request=AwakeningRaceSelection.CreateRequest(null);
            Check(request.KindDef==human && request.ForcedXenotype==pig,"human colony excludes its baseline identity but retains other vanilla xenotypes");
            ModsConfig.BiotechActive=false; DefDatabase<PawnKindDef>.AllDefsListForReading=new List<PawnKindDef>{human};
            request=AwakeningRaceSelection.CreateRequest(null);
            Check(request.KindDef==human && request.Faction==Faction.OfPlayer,"no alternatives safely falls back to player race without Biotech or race mods");
            DefDatabase<PawnKindDef>.AllDefsListForReading.Add(ratkin); Rand.ForceIndex=0;
            request=AwakeningRaceSelection.CreateRequest(null);
            Check(request.KindDef==ratkin && request.ForcedXenotype==null && request.Faction==null,"HAR alternatives work without Biotech");
            Faction.OfPlayer.def.basicMemberKind=null; Rand.Force=true;
            Check(AwakeningRaceSelection.CreateRequest(null).KindDef==human,"missing faction basic kind falls back to vanilla colonist");
            return "PASS: "+checks+" awakening race assertions; 20000 rolls: "+string.Join(", ",counts)+" (engine/HAR stubs).";
        }
    }
}

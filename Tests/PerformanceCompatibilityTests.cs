namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        public class FixedGenderMissingField { public void CompTick() {} }
        public class FixedGenderWrongField { public int changeGenderOnce; public void CompTick() {} }

        private static void RunPerformanceCompatibilityTests()
        {
            Reset(); var map=new Map(); var queue=MapComponent_EnemyShapeshifters.For(map);
            int reads=map.componentReads;
            for(int i=0;i<100000;i++) Patch_EnemyShiftSafePoint.Prefix(map);
            Check(map.componentReads==reads && map.mapPawns.reads==0,"100000 idle map safe points perform no component-list or pawn-list scans after registration");
            var map2=new Map(); var queue2=MapComponent_EnemyShapeshifters.For(map2);
            Check(queue!=queue2 && MapComponent_EnemyShapeshifters.For(map)==queue,"map component cache isolates maps");
            var rebuilt=new MapComponent_EnemyShapeshifters(map);
            Check(MapComponent_EnemyShapeshifters.For(map)==rebuilt,"reconstructed map component replaces previous registration");
            Check(MapComponent_EnemyShapeshifters.For(null)==null,"missing map has no component");

            Reset(); map=new Map(); Pawn human1,human2;
            var animal1=EnemyForm(out human1,map); var lord1=EnemyLord(animal1,map);
            var animal2=EnemyForm(out human2,map); EnemyLord(animal2,map);
            Patch_EnemyDownedReturn.Prefix(animal1); Patch_EnemyDownedReturn.Prefix(animal2);
            animal1.Downed=animal2.Downed=true; lord1.failLost=true; GenSpawn.FailBefore=p=>p==human1;
            Patch_EnemyShiftSafePoint.Prefix(map);
            Check(!animal1.comp.enemyReturnPending && animal1.comp.HasStoredPawn && !animal1.Destroyed,"foreign raid-loss callback failure keeps original body and releases pending flag");
            Check(human2.Spawned && animal2.Destroyed,"one failing return callback does not discard another queued return");
            Check(Log.once.Contains(81729174),"foreign callback error is logged with deduplication key");
            var brokenMap=new Map {failComponentLookup=true};
            Patch_EnemyShiftSafePoint.Prefix(brokenMap); Patch_EnemyShiftSafePoint.Prefix(brokenMap);
            Check(Log.once.Contains(81729173),"safe-point prefix contains unexpected errors instead of aborting native map ticking");

            Reset(); map=new Map(); Pawn human; var animal=Form(out human,map); var component=new ThingComp {parent=animal}; bool pending=true;
            AnimalModCompatibility.FixedGenderPrefix(component,ref pending);
            reads=TransformUtility.transformedLookups;
            for(int i=0;i<100000;i++) AnimalModCompatibility.FixedGenderPrefix(component,ref pending);
            Check(!pending && TransformUtility.transformedLookups==reads,"100000 VEF ticks after first rewrite do not query transformation state");
            pending=true; AnimalModCompatibility.FixedGenderPrefix(component,ref pending);
            Check(!pending,"VEF reload resetting pending flag preserves transformed gender again");
            var ordinary=new Pawn(); GenSpawn.Spawn(ordinary,new IntVec3(),map); pending=true;
            Check(AnimalModCompatibility.FixedGenderPrefix(new ThingComp {parent=ordinary},ref pending) && pending,"ordinary mapped animal still receives native first gender rewrite");
            animal.DeSpawn(DestroyMode.Vanish); pending=true;
            AnimalModCompatibility.FixedGenderPrefix(component,ref pending);
            Check(pending,"unspawned form defers gender rewrite handling until placement");
            var harmony=new Harmony(); AccessTools.optionalType=typeof(FixedGenderMissingField); AnimalModCompatibility.Install(harmony);
            Check(harmony.patches==0,"changed VEF layout without expected field skips optional hook");
            AccessTools.optionalType=typeof(FixedGenderWrongField); AnimalModCompatibility.Install(harmony);
            Check(harmony.patches==0,"changed VEF field type skips optional hook");
            AccessTools.optionalType=typeof(FakeFixedGender); harmony.fail=true; AnimalModCompatibility.Install(harmony);
            Check(harmony.patches==0,"optional Harmony failure does not escape installation");

            Reset(); var victim=new Pawn {RaceProps=new RaceProperties {Humanlike=true,Animal=false}};
            var damage=new DamageInfo(DamageDefOf.Blunt,5) {Instigator=new Pawn()}; reads=Current.Game.componentReads;
            for(int i=0;i<10000;i++) Patch_LatentAnimalPlayerHarm.Prefix(victim,damage,5);
            Check(Current.Game.componentReads==reads,"10000 human damage callbacks avoid harm component lookup");
            IEnumerable<Gizmo> original=new[] {new Gizmo()}; IEnumerable<Gizmo> result=original;
            for(int i=0;i<10000;i++) Patch_OwnedAnimalAwakeningGizmos.Postfix(victim,ref result);
            Check(object.ReferenceEquals(original,result),"ordinary pawn gizmos keep original enumerable without stacking wrappers");
        }
    }
}

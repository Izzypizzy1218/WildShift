namespace RimWorld { public class StockGenerator_Animals {} }
namespace WildShift.Tests
{
    public static partial class StabilityTests
    {
        private static void RunTraderTestModeTests()
        {
            Reset(); var animal=new Pawn { Faction=null }; IEnumerable<Thing> stock=new Thing[] { animal };
            var original=stock; Patch_TraderShapeshifterTestMode.Postfix(ref stock);
            Check(stock==original && !animal.health.hediffSet.latent,"disabled mode preserves stock and baseline roll");
            WildShiftMod.Settings.forceTraderShapeshifters=true;
            var already=new Pawn { Faction=null }; already.health.hediffSet.latent=true;
            var owned=new Pawn(); var nonAnimal=new Pawn { Faction=null }; nonAnimal.RaceProps.Animal=false;
            var dead=new Pawn { Faction=null,Dead=true }; var item=new Thing();
            stock=new Thing[] { animal,already,owned,nonAnimal,dead,item }; original=stock;
            Patch_TraderShapeshifterTestMode.Postfix(ref stock);
            Check(!animal.health.hediffSet.latent,"lazy stock is not eagerly generated or marked");
            var result=stock.ToArray();
            Check(result.SequenceEqual(original),"stock order and identities are preserved");
            Check(animal.health.hediffSet.latent && animal.health.added==1,"eligible fresh trader animal is guaranteed marked");
            Check(already.health.added==0,"existing latent marker is not duplicated");
            Check(!owned.health.hediffSet.latent && !nonAnimal.health.hediffSet.latent && !dead.health.hediffSet.latent,"owned dead and ineligible animals are excluded");
            animal=new Pawn { Faction=null }; stock=new Thing[] { animal };
            Patch_TraderShapeshifterTestMode.Postfix(ref stock); WildShiftMod.Settings.forceTraderShapeshifters=false;
            stock.ToArray(); Check(!animal.health.hediffSet.latent,"disabling before lazy enumeration stops forced marking");
            Reset(); WildShiftMod.Settings.forceTraderShapeshifters=true;
            var many=Enumerable.Range(0,100).Select(i=>new Pawn { Faction=null }).ToArray(); stock=many;
            Patch_TraderShapeshifterTestMode.Postfix(ref stock); stock.ToArray();
            Check(many.All(p=>p.health.hediffSet.latent),"all 100 eligible stock animals are marked without random failure");
            var wild=new Pawn { Faction=null }; Check(!wild.health.hediffSet.latent,"non-stock wild animal is untouched");
            Reset();
        }
    }
}

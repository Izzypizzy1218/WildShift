# Shared biological state

WildShift preserves the original human as the owner of genes, traits and personal needs. The active animal exposes those same objects through non-owning views. The animal's actual native gene lists, original traits and original needs are saved independently; borrowed human objects are not deep-saved a second time.

- Native gene consumers read the original active genes. This includes ordinary stats, capacities, pain/damage modifiers, immunity, permanent-wound prevention, darkness, water movement, metabolic food cost and other declarative gene flags. The original human's age determines activation and the source age curve; the animal keeps its own physical age.
- Canonical gene/trait mutations synchronize the active body's view before the operation returns and invalidate only that body's native stat entries. Repeated addition/removal in one tick retains correct cached stats; the ordinary per-tick view cache remains enabled.
- Traits retain their original objects and suppression. Native hunger, need membership, disease and social checks read that view. The source trait objects still belong to the human.
- Work restrictions combine the body's restrictions with the original human's genes, traits, backstories and other native restrictions. Transforming does not bypass a violence prohibition or a permanently disabled work type.
- Animal mating retains the native species path and sterility checks. When a parent is transformed, its effective Fertility multiplies the native conception chance; an ordinary animal parent keeps factor1. The human's age/gender determines the shared fertility age curve, avoiding a human fertility-age curve treating an adult animal shape as a human child. Mammal chance50% therefore becomes75% at150% fertility or25% at50% fertility. Egg fertilization applies the corresponding chance to its native fertilization operation. Both parents' factors compose once and chance clamps to0–100%.
- Trait price offsets are consumed once by the ordinary pawn quality-price calculation through the trait view. `GeneDef.marketValueFactor` is a genepack-price field in vanilla, not a separate pawn-sale multiplier; WildShift does not invent a pawn multiplier from it.
- The original need objects, including mood and chemical needs, advance once using the active body. The original human is not given another full pawn tick. Thought handlers evaluate the active body and its environment. Food and rest percentages survive body-owner changes and save/reload without changing levels while saving.
- Reviewed native gene callbacks advance once with a temporary active-body context, then restore the original gene owner. Hemogen/drain and chemical-dependency clocks retain their existing dedicated bridges. The original human's biological age continues through the isolated age tracker.
- Return restores the source owners and the animal's own trackers before the original human is released. Reusing the same cached animal rebuilds the views.
- Render-tree gene nodes use the animal's own genes so that personal human gene graphics are not attached to an animal merely because their mechanical effects are shared.

## Custom gene callbacks

Plain declarative modifiers on a mod's `GeneDef` use the same native consumer paths. A mod subclass that inherits a native `Tick`/`TickInterval` callback retains that native support. A custom overridden callback needs review because it may assume a human body or manage another resource system. An author can opt a reviewed callback into active-body execution:

```xml
<modExtensions>
  <li Class="WildShift.SharedFormGeneCallbacksExtension" />
</modExtensions>
```

This opt-in does not transfer ownership, run `PostAdd` again, or create a second copy of a gene. It does not replace the separate health-buff or ability compatibility gates.

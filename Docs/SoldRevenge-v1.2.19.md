# Sold shapeshifter revenge — v1.2.19

## Player behavior

Selling a player-owned shapeshifter while it is in its animal form can cause that same person to return with an animal pack and attack a player settlement. This applies to sales made after installing this version; old sales cannot be reconstructed from an existing save.

- The default chance is **50% per original pawn**, scheduled **15–45 game days after sale**. The chance is adjustable from 0% to 100% in WildShift settings.
- Only an actual transformed pawn containing its original shapeshifter is eligible. Selling an ordinary animal or an unawakened latent animal does not create this event.
- The leader is the **actual original pawn**, retaining its name, body race, skills and assigned species. A new human copy is never generated. Existing transformation/reversion code handles possessions and saved genetic-form state.
- The leader brings **2–5 ordinary animals of the assigned species**. Count scales with the map's native threat points and that animal kind's combat power, within those limits. Companions are not additional latent shapeshifters.
- A separate hidden hostile faction handles the personal revenge. It does not change the buyer faction's diplomatic relations. The hidden faction does not generate settlements or ordinary random raids.
- The normal enemy shapeshifter assault, return and retreat behavior applies. Former recruitment quest tags remain on the original pawn and do not block this personal revenge transformation. Existing enemy safety restrictions still apply to forms configured to die automatically on downing.

## Scheduling and cancellation

The trader must have left and released the pawn as a loose world pawn. There is no extraction from a live map, visiting trader, orbital stock, caravan or another container. Settlement stock can therefore delay or outlast the event until the native game releases the pawn.

At least one player home map must exist. The recorded sale map is preferred if it remains a player home; otherwise another player home is used. Arrivals wait while big threats are disabled, the selected map has high story danger, or either body is downed/in a mental state. Revenge arrivals are separated by at least three game days. Failed arrival attempts retry after one day.

Buyback immediately cancels the record. The next hourly check also cancels it when the active pawn has returned to player ownership, is a colony prisoner/slave, or either required body has died or been destroyed. Merely tending a still non-player pawn does not change this ownership rule. Unresolved events expire 90 days after their initially scheduled date. Setting the chance to 0% cancels pending records at the next check.

Each original pawn receives only one enabled sale roll, including a failed roll or a later buyback. Repeated sales cannot reroll or stack revenge events. Sales made while the chance is 0% do not consume a roll.

## Save handling and performance

The game component saves references to the sold form and original pawn, due/expiry ticks and the intended home map. The transformed form continues to own the original pawn through the existing holder. Pending live pawns receive a temporary critical-world-pawn reason so native garbage collection cannot silently remove the original before arrival. Cancellation, expiry and completed arrivals release that protection.

The empty queue returns immediately. A nonempty queue is processed once per 2,500 ticks (one game hour); there is no per-tick scan of world or map pawns. Animal followers use their native assault/retreat duties through WildShift's existing enemy think trees.

## Verification

Validation uses isolated RimWorld 1.6 profiles, with Core only and with all five official expansions. The original player profile and saves are not used. Each profile starts a fresh map, saves before the revenge, loads in a separate process, saves the active assault, and loads again for actual AI combat ticks.

The fixture passed **52 checks in each profile (104 total)**. It exercises native orbital and visiting-caravan sale/buyback callbacks, ordinary-animal and 0% controls, duplicate-sale rejection, recovery/death/expiry cancellation, live-stock delay, native orbital departure, world-GC protection, exact original identity, old recruitment tags, peaceful delay, pack composition/duties, repeated-processing protection, save/load and 1,200 native combat ticks followed by return to the same human pawn. The final runs recorded 16 native attack attempts in Core and 15 with all expansions. Engine checks and logs are retained with the release verification artifacts.

Separate checks passed 89 logic, 17 awakening-race, 246 stability and 15 special-training assertions (367 total), real Harmony injection against a modeled VEF callback, and native API/XML validation (37 Harmony targets, 10 injected fields, 10 enemy signatures and 14 XML files). Engine batch-mode validation checks game logic and AI; it is not a rendered gameplay demonstration. This release does not claim a new full-mod-list visual or compatibility audit.

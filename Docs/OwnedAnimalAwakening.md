# Purchased and owned animal awakening — v1.2.7

As of v1.2.10, completed awakening and failed candidate cleanup are saved. Repeated clicks after failed animal deletion only finish cleanup; they do not create another human. If a cancelled candidate could not be deleted, its cleanup must succeed before another generation attempt. See [recovery safeguards](Recovery-v1.2.10.md).

Select an eligible player-owned latent animal on a map and use **Awaken as human**. Previously purchased animals are supported. The human receives the animal's kind as its fixed transformation form.

## Rules

- Purchased animals may manually awaken regardless of old or new damage. Buying an animal clears a previous medical-origin rejection; it does not erase damage history. Pawn replacement never occurs inside a trade callback.
- The harm record still prevents player-inflicted injury followed by medical bonding from producing an automatic human. A rejected medical recruitment now records a separate manual-awakening block, preventing immediate bypass via the button. Normal taming remains possible.
- Owned animals with no recorded medical-origin rejection can use the command. Before v1.2.7, acquisition routes were not recorded: an old owned animal cannot be distinguished as bought versus medically recruited. It is not blocked merely because of old harm. Medical rejection tracking begins with this version; earlier medical abuse cannot be classified retroactively.
- The animal must be alive, spawned on a map, not downed and not in a mental state. For caravan and orbital purchases, wait for map arrival.
- Inventory, carried items, equipment and apparel are automatically placed nearby during awakening. There is no manual unloading prerequisite. Items are not automatically worn by the human because animal gear may be incompatible.
- If a drop fails or a mod throws during item handling, the original animal and remaining items are retained. Any items already dropped stay on the ground. The generated human is cleaned up; the player can clear space and retry.

## 한국어 사용법

구매한 **잠재된 변신자**를 맵에서 선택하고 **인간으로 각성**을 누르세요. 구매 동물은 피해 이력이나 소지품 때문에 버튼이 막히지 않습니다. 짐·장비·의복은 각성할 때 주변 바닥에 자동으로 내려놓습니다. 원래 동물 종은 이후 변신할 고유 형상이 됩니다.

플레이어 측이 다치게 한 뒤 치료로 길들여져 각성이 거부된 경우에는 버튼으로 우회할 수 없습니다. 이때의 거부 상태는 별도로 저장되며, 이후 정상 구매하면 해제됩니다. 일반 길들이기는 계속 가능합니다. 이전 버전의 구매·치료 합류 이력은 없으므로 기존 동물을 피해 기록만으로 치료 합류 사례라고 간주하지 않습니다.

## Implementation and verification

The gizmo postfix preserves original gizmos. Its callback rechecks ownership, state and medical-origin rejection and uses the existing reentrancy guard. Pawn.PreTraded's purchase postfix only clears the rejection after native ownership changes; it does not transform during delivery.

The shared reveal places the human before dropping belongings using native inventory/equipment/apparel/carry APIs. It checks that all four containers are empty before deleting the animal. Failed/partial drops clean up the generated human and retain the original animal. This protection also applies to normal and medical revelation. Ordinary later animal-form reversion, prosthetics and pregnancy persistence are unchanged.

The game component saves distinct sets of IDs for player harm and rejected medical recruitment. Tests use a label-aware serialization stub; this is not a real Scribe round-trip. The current 89 logic + 164 stability assertions cover medical rejection, purchase exemption, later damage, independent save fields, legacy history, successful four-container preservation, failed/partial/throwing drops, retry, reentrancy, ownership and map state. Production compilation and 30 Harmony target checks passed.

Live validation remains: awaken a damaged purchased animal, an older purchased animal, a loaded animal and one delivered by caravan/pod. Confirm possessions remain nearby and the assigned form matches. Verify a newly player-harmed medical recruit is blocked until normal taming or subsequent purchase. Save/reload that rejection and purchase exemption before retrying.

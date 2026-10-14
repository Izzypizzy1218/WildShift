# Medical awakening — v1.2.5

As of v1.2.7, manual awakening does not check harm alone. A rejected player-harm medical recruitment saves a separate manual block; a later purchase clears that block without erasing the harm record. Legacy saves have no acquisition-route history, so existing owned animals are not presumed medically recruited from old damage alone. The medical damage rule below remains unchanged. See [manual awakening](OwnedAnimalAwakening.md).

Normal successful taming awakens an eligible latent animal regardless of bond or harm history. Medical-bond taming now also awakens it, unless that animal has recorded damage from a player-owned instigator. This supersedes the v1.2.4 blanket exclusion.

## Behavior

- Wild predator or enemy injuries: medical awakening is allowed when native treatment creates a bond and recruits the animal.
- Actual player-owned damage: medical awakening is blocked for that animal, including damage from colonists, colony animals, turrets and traps when the instigator carries the player faction. Native treatment, bonding and recruitment still occur.
- Zero actual damage: no harm record. Later nonplayer damage does not erase a player-harm record. Deliberate attacks, friendly fire and self-defense use the same attribution rule.
- Death and resurrection: neither clear the record. Resurrection alone does not disqualify an animal.
- Normal taming: still permitted after player damage; no bonded relationship is required.
- Already owned animals: treatment does not retroactively awaken them. Only a new nonplayer-to-player transition during treatment qualifies.

## Implementation and limits

Damage attribution reads `DamageInfo.Instigator.Faction.IsPlayer` at the time damage is reported. The source must exist, have a faction, and belong to the player. The reported `totalDamageDealt` must be greater than zero, and the victim must still carry `WildShift_FieldShapeshifterMarker`. On success, the victim's `thingIDNumber` is added to the saved harm set. This checks player ownership, not general diplomatic friendliness: a visiting allied-faction pawn does not count as player-owned. Later faction changes by the attacker do not erase the victim's recorded history.

`Pawn.PostApplyDamage` receives the actual damage result, including lethal hits. Its prefix records the stable thing ID only for a latent marked animal hit by a player-owned instigator. `GameComponent_LatentAnimalHarm` saves a set of integer IDs using `Scribe_Collections` with `LookMode.Value`; it does not retain Pawn references or change existing hediff classes. No recurring scan is added.

`TendUtility.DoTend` has a separate prefix/postfix pair. The prefix checks a marked, eligible, living, nonplayer animal and a player doctor; the postfix requires native recruitment to have succeeded. Revelation occurs after treatment finishes, avoiding destruction of the patient inside the nested `DoRecruit` call. Deferred revelation rechecks the harm record and the existing reveal method rechecks placement/faction/liveness.

History begins after upgrading. Earlier damage, hidden damage from third-party mods and instigators without a player faction cannot be reconstructed. Such unknown damage is not attributed to the player. Custom mods that skip the native damage or treatment methods require separate compatibility work. This is not a complete anti-exploit system for indirect environmental manipulation.

## 한국어: 피해 판정과 각성 규칙

- 게임이 피해와 함께 전달하는 공격 주체(`DamageInfo.Instigator`)의 소속이 플레이어인지 확인합니다. 정착민, 아군 동물, 포탑·함정 등이 공격 주체로 전달되면 해당 소속을 사용합니다. 단순히 우호적인 다른 세력은 플레이어 소속으로 계산하지 않습니다.
- 실제 피해량이 0보다 크고 피해 대상에게 잠재된 변신자 표식이 있을 때만, 그 동물의 고유 ID를 피해 이력에 기록합니다. 완전히 막힌 공격은 기록하지 않습니다.
- 플레이어 측 피해 이력이 있으면 치료 중 유대로 자동 길들여져도 각성하지 않습니다. 일반 길들이기에 성공했을 때의 각성은 계속 가능합니다.
- 야생동물·적에게 다친 동물은 치료 중 유대가 생겨 정착지 소속으로 길들여지면 각성할 수 있습니다. 치료만 받았거나 이미 정착지 소속인 경우에는 자동 각성하지 않습니다.
- 기록은 저장·불러오기와 사망·부활 후에도 유지됩니다. 고의 공격, 오사, 방어 공격은 구분하지 않습니다.
- 피해 이력 기록은 v1.2.5 적용 후부터 시작합니다. 이전 피해나 공격 주체가 없는 환경 피해는 플레이어 측 피해로 소급 추정하지 않습니다.

## Verification

- 20 added assertions compile and exercise the production harm component and both production taming hooks against engine stubs: wild/enemy/player/object damage, zero/unknown damage, mixed attackers, lethal damage and resurrection, stable-ID serialization, missing legacy data, normal taming, failed treatment/recruitment, unmarked/owned animals, nonplayer doctors, deferred harm and independent animals.
- Total: 89 logic and 131 stability assertions (220). The serialization test invokes the production ExposeData method with a stub value-set copy; it is not real Scribe or a live resurrection test.
- Production compilation uses the installed RimWorld/Harmony assemblies. Metadata verification covers 28 Harmony targets and the exact new damage/treatment signatures.
- In-game confirmation remains: compare predator-injured and player-injured marked animals, force successful medical bonding on a disposable save, save/reload a blocked animal, resurrect it, and verify that only normal taming can still awaken the blocked animal. Verify treatment finishes without accessing a destroyed patient.

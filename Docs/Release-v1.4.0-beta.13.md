# v1.4.0-beta.13 — 2026-11-28

## 한국어

- **런타임 최적화.** 일반 폰의 변신 여부를 확인하는 비용을 줄였습니다. 변신 중에는 건강 상태·장비·유전자·능력이 그대로일 때 반복 계산을 줄입니다.
- **재변신 장비와 알파 애니멀즈 호환.** 같은 동물 몸을 다시 사용할 때 인간 본체의 주무기와 의복이 다시 연결됩니다. 헬릭시엔 변신 중 랫킨 돌진창을 내려놓을 때 발생하던 예외를 수정했습니다. VE Framework가 같은 공유 능력 명령을 두 번 전달하는 경우 중복을 제거하고 동물 고유 명령을 유지합니다.
- **공유 능력과 범위 버프.** 활성 헤모겐 유전자를 선택하고, 지원 범위를 벗어난 능력을 동물형 목록에서 제거합니다. 능력의 시전자나 공유 중인 트래커의 소유자가 어긋나면 활성 동물형으로 복구합니다. 중첩 범위 버프는 호출마다 별도 대상 목록을 사용합니다.
- **검증.** 림월드 1.6·1.7의 실제 게임 저장·불러오기 검사에서 버전별 895개가 통과했습니다. 1.7 공유 능력 API 검사 78개도 통과했습니다.
- **알려진 호환 제한.** 인간 본체에 신경열 트래커가 없는 상태에서 다른 모드가 변신체 자체에 신경열 트래커를 붙이면, 다음 갱신에서 새 트래커가 제거될 수 있습니다.

## English

- **Runtime optimization.** Reduced the cost of checking ordinary pawns for transformation and cut repeated work for unchanged health, gear, genes and abilities on active forms.
- **Equipment on repeat transformations and Alpha Animals compatibility.** Reconnect the original pawn’s primary weapon and apparel when reusing an animal body. Fixed an exception when a Ratkin Heavy Lance was dropped during transformation into a Helixien. Deduplicated identical shared ability commands from VE Framework while preserving intrinsic animal commands.
- **Shared abilities and auras.** Select the active hemogen gene and remove abilities that become unsupported from the animal form. Restore an ability’s caster or a borrowed tracker’s owner to the active form when the binding drifts. Nested aura invocations use separate recipient lists.
- **Validation.** Passed 895 in-game save and reload checks per version in RimWorld 1.6 and 1.7. Also passed 78 shared-ability API checks in RimWorld 1.7.
- **Known compatibility limitation.** If a mod adds a psychic entropy tracker directly to an active animal form whose original human has none, the next update can remove the added tracker.

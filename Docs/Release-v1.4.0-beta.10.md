# v1.4.0-beta.10 — 2026-11-18

## 한국어

- **건강 탭 최적화.** 상처와 건강 상태가 많이 쌓인 변신자의 건강 탭에서 상처 묶음과 글자 높이를 반복 계산하는 작업을 줄였습니다. 테스트에서 표시 순서, 색상, 위치가 기존과 일치했습니다.
- **모드 제거용 저장 준비.** WildShift 설정에서 변신자를 인간으로 복귀시키고 별도의 복구용 저장을 만들 수 있습니다. 모든 복귀가 성공해야 새 저장을 만들며, 기존 파일은 보존됩니다.
- **인간 본체 보호.** B&S 지능화 변환이 몸을 교체하기 전에 실패하거나, 인간 복귀 중 수납 알림에서 예외가 발생할 때 원래 인간을 회수하도록 수정했습니다.
- **기존 곤충 형상 유지.** 곤충 옵션을 꺼도 이미 보관 중인 유효한 곤충 형상을 계속 사용할 수 있습니다. 배정과 보관 형상이 어긋난 기존 저장도 해당 형상을 복구합니다.
- **건강 효과 처리 개선.** 다른 Harmony 패치가 원본 틱을 건너뛸 때 건강 효과의 연결이 지워지는 문제를 수정했습니다. 활성 변신자가 없는 맵에서는 범위 버프의 추가 순회를 생략합니다.
- **기타 수정.** 기존 정착민의 퀘스트 재영입 시 변신자 확률을 다시 굴리던 문제와 커스텀 형상을 배정받은 복수 습격 동료의 예산 계산을 수정했습니다. 1.7 Harmony 등록 경고도 해결했습니다.

**검증.** 실제 림월드 1.6·1.7에서 버전별 886개 검사를 통과했습니다. 복구용 저장을 WildShift 없이 다시 열어 맵과 상단에 원래 정착민이 남는 것을 확인했습니다. 이때 로드 로그에는 WildShift 항목 누락이 표시될 수 있습니다. B&S 복구는 오류를 주입해 검증했습니다.

적이 많은 전투에서 변신자를 선택할 때 발생하는 심한 지연은 아직 해결이 확인되지 않았습니다.

## English

- **Health tab optimization.** Reduced repeated injury grouping and text measurement for shapeshifters with many health conditions. The tested display order, colors, and positions matched the original layout.
- **Removal preparation.** A new action in WildShift's settings returns active shapeshifters to human form and writes a separate recovery save. It writes the new save after all returns succeed and keeps the previous save file.
- **Human body recovery.** Recover the stored human if B&S sapience conversion fails before the replacement body takes over, or if a container callback throws during return to human form.
- **Stored insectoid forms.** Valid stored insectoid forms remain usable after disabling the insectoid option. Older saves can recover stored forms that no longer match the assignment.
- **Health effect handling.** Preserve health links when another Harmony patch skips the original tick. Skip the additional aura scan on maps with no active shapeshifters.
- **Other fixes.** Stop repeated shapeshifter rolls for existing colonists during quest recruitment, use the assigned forms' actual costs for additional revenge raiders, and fix a Harmony registration warning in 1.7.

**Validation.** Passed 886 checks per version in RimWorld 1.6 and 1.7. The original map and caravan colonists were present after opening the recovery save with WildShift disabled. The load log can still report missing WildShift entries. The B&S recovery path was tested by injecting a failure.

The reported severe slowdown when selecting a shapeshifter during enemy-heavy combat is still under investigation.

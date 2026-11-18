# v1.4.0-beta.9 — 2026-11-18

## 한국어

- **ISEKAI CREATURES ADD-ON 호환 수정:** 변신한 정착민의 징집 버튼이 작동하지 않던 문제를 수정했습니다. 중복 징집 명령만 정리하며, 일반 길들인 ISEKAI 동물의 징집 기능은 유지됩니다.
- **반복 처리 감소:** 공유 상태 처리가 필요한 활성 변신체만 해당 틱 처리에 진입하도록 개선하고, 징집 명령 제작자 정보의 반복 조회를 줄였습니다.
- **임시 메모리 할당 감소:** 렌더링·건강 알림과 장비·능력치 조회의 불필요한 객체 생성과 중복 검색을 줄였습니다. 유전자·특성·약물·보철·장비 효과와 같은 틱의 상태 변경 반영은 유지됩니다.
- **오라 처리 개선:** 범위 밖 대상을 먼저 제외해 불필요한 소유자 조회를 줄였습니다. 실제 오라 효과와 기존 연결의 갱신·정리는 유지됩니다.
- **예외 발생 시 상태 복원 보완:** 임시 몸 연결 중 오류가 발생해도 건강 효과의 원래 소유자와 부위를 복원합니다. 다른 모드가 처리를 중단하거나 오류를 내는 경우에도 중첩된 외형·렌더링·자동 공격 문맥을 보존하도록 보완했습니다.
- **인간 복귀 후 콜백 중단:** 지원되는 유전자 콜백 실행 중 인간으로 복귀하거나 활성 몸이 바뀌면, 이전 동물 몸에서 후속 콜백을 계속 실행하지 않도록 수정했습니다.

**검증:** 실제 초기화된 림월드 1.6·1.7에서 각각 875개 검사를 통과했습니다. 실제 ISEKAI 애드온의 징집·해제·인간 복귀와 일반 동물 대조 검사를 두 버전에서 수행했으며, 1.7의 역순 모드 로딩도 확인했습니다.

**남은 확인 사항:** 적이 많은 전투에서 변신한 정착민을 선택할 때 발생한다는 심한 지연은 아직 해결이 확인되지 않았습니다. 이번 변경은 확인된 반복 처리와 임시 할당을 줄이는 개선이며, 모든 환경의 FPS·TPS 향상을 보장하지 않습니다.

## English

- **ISEKAI CREATURES ADD-ON compatibility:** Fixed the draft button doing nothing on transformed colonists. Only the duplicate draft command is removed; ordinary tamed ISEKAI creatures retain their drafting functionality.
- **Less repeated runtime work:** Shared runtime ticks now enter the relevant processing only for initialized active forms. Repeated draft-command producer metadata lookups have also been reduced.
- **Fewer temporary allocations:** Reduced unnecessary object creation and duplicate lookups in rendering, health notifications, equipment and capacity queries. Shared gene, trait, drug, implant and equipment effects, including same-tick updates, are preserved.
- **Aura processing improvements:** Out-of-range recipients are rejected before unnecessary owner lookups. Actual aura effects and existing-link updates and cleanup are preserved.
- **Safer state restoration:** Health effects recover their original owner and body part if temporary body binding fails. Nested appearance, rendering and automatic-combat contexts are also preserved when another mod skips processing or throws an exception.
- **Stop callbacks after returning to human:** If a supported gene callback returns the pawn to human form or changes the active body, subsequent callbacks no longer continue on the previous animal body.

**Validation:** Passed 875 checks in each of initialized RimWorld 1.6 and 1.7. Drafting, undrafting, returning to human and ordinary-animal controls were checked with the actual ISEKAI add-on in both versions, including reversed mod load order in 1.7.

**Still under investigation:** The reported severe slowdown when selecting a transformed colonist during enemy-heavy combat is not confirmed resolved. These changes reduce specific repeated work and temporary allocations; they do not guarantee higher FPS or TPS in every setup.

# v1.4.0-beta.11 — 2026-11-19

## 한국어

- **초능력 버튼 최적화.** 초능력이 많은 정착민을 선택했을 때, 명령창이 신경열 증가량과 한도를 버튼마다 다시 계산하던 작업을 줄였습니다. 같은 화면 갱신 안에서 이 두 수치를 재사용합니다. 각 능력의 현재 신경열·예약 비용 검사와 실제 시전은 기존 게임 로직을 따릅니다.
- **전투 재현 검사.** 림월드 1.6에서 능력 32개를 가진 서나라 햄스터 랫킨의 인간형과 트리케라톱스형을 각각 적 60명과 교전시켰습니다. 동물형에는 능력 버튼 31개가 표시됐습니다. 트리케라톱스형을 선택했을 때 UI 호출당 평균 CPU 시간이 약 26~28ms에서 약 3ms로 줄었습니다. 전투 진행과 피격 타이밍은 실행마다 달랐습니다.
- **동물 형상과 실제 시전.** 1.6에서 테스트 구성에 포함된 동물 형상 63종의 변신, 소집, 건강 탭, 인간 복귀와 능력 버튼 목록을 확인했습니다. 적 60명은 대기 상태로 두고 검사했으며, 측정이 불안정했던 5종은 예열 후 다시 시험했습니다. 별도 시전 검사에서 인간형과 트리케라톱스형 모두 Painblock 효과가 적용되고 신경열이 증가하는 것을 확인했습니다.
- **1.7 검증 범위.** 1.7 게임 어셈블리를 사용한 격리 Harmony 검사 22개를 통과했습니다. 위 실게임 시전과 전투 성능 측정은 1.6에서 수행했습니다.

## English

- **Psycast command optimization.** Reduced repeated neural heat gain and limit calculations when selecting a colonist with many psycasts. The command bar reuses those two stat values within the same UI pass. Each ability still uses the game's own current heat, queued cost, and casting checks.
- **Combat reproduction.** Tested both the human and Triceratops forms of a Moustate Hamster Ratkin against 60 enemies in RimWorld 1.6. The human had 32 abilities and the animal form displayed 31 ability commands. For the selected Triceratops, mean CPU time per UI call fell from about 26–28 ms to about 3 ms. Combat progression and hit timing varied across runs.
- **Animal forms and live casting.** Checked transformation, drafting, the Health tab, ability commands, and return to human across all 63 forms available in the 1.6 test setup. The sweep used 60 waiting hostiles. Five forms with anomalous short samples were retested after warm-up. In separate live casting checks, both forms successfully cast Painblock and gained neural heat.
- **1.7 validation scope.** Passed 22 isolated Harmony checks against the RimWorld 1.7 game assemblies. The live cast and combat measurements above were performed in 1.6.

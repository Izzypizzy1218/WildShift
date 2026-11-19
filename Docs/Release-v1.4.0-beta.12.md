# v1.4.0-beta.12 — 2026-11-19

## 한국어

- **동물 변신 상태의 틱 비용 감소.** 같은 계산에서 이미 확인한 인간 본체와 변신 표시를 재사용하고, 공유 능력치와 이념·장비 효과의 중복 검사를 줄였습니다. 유전자·특성·장비·조건부 효과의 변경은 같은 틱에 반영됩니다.
- **1.6 성능 측정.** 같은 세이브에서 소집된 와르그 50명으로 TPS 상한을 풀고 측정했습니다. beta.11은 347 TPS, 이전 작업본은 423 TPS였고, 이번 빌드는 두 실행에서 542 TPS와 557 TPS를 기록했습니다.
- **1.6·1.7 저장 검증.** 변신 중 저장과 불러오기, 인간 복귀, 상단에서의 복귀를 포함한 실게임 검사에서 버전별 888개 항목을 통과했습니다.
- **랫킨 전투 재현.** 서나라 관련 모드를 제외한 1.6에서 능력 32개를 가진 기본 랫킨을 적 60명과 교전시켰습니다. 트리케라톱스의 선택 중 명령창 처리 시간이 화면 갱신당 약 2.7ms에서 2.0ms로 줄었습니다. 첫 변신의 클릭부터 화면 갱신까지는 약 56ms였으며, 이 시험에서 지속적인 TPS 급락은 재현되지 않았습니다.
- **동물 형상 및 알려진 호환 문제.** 1.6 테스트 구성에서 210종을 검사해 209종의 변신과 초기 소집, 210종 모두의 인간 복귀를 확인했습니다. 일부 Alpha Animals 형상에서 VE Framework의 공유 능력 명령 중복이나 길들이기 제한이 확인됐습니다. 헬릭시엔이 랫킨 중창을 장착한 상태로 변신할 때 발생하는 장비 해제 예외는 beta.11과 이번 빌드에서 모두 재현됐습니다.

## English

- **Lower tick cost for active animal forms.** Reuse the original pawn and transformation marker within the same calculation and reduce repeated checks for shared stats, Ideology effects, and gear. Changes to genes, traits, gear, and conditional modifiers still take effect in the same tick.
- **RimWorld 1.6 benchmark.** Measured 50 drafted Wargs in the same save with the TPS cap removed. The published beta.11 reached 347 TPS and the earlier development build reached 423 TPS. This build reached 542 TPS and 557 TPS in two runs.
- **RimWorld 1.6 and 1.7 save validation.** Passed 888 checks per version in the actual game, including saving and loading during transformation and returning to human form on a map and in a caravan.
- **Ratkin combat reproduction.** Tested a base Ratkin with 32 abilities against 60 enemies in 1.6 with the Moustate-related mods removed. While the Triceratops was selected, command-bar processing dropped from about 2.7 ms to 2.0 ms per repaint. The first transformation still took about 56 ms from click to repaint. The sustained TPS drop did not reproduce in this scenario.
- **Animal forms and known compatibility issues.** Checked 210 forms available in the 1.6 test setup. 209 transformed and could initially be drafted, and all 210 returned to the original human. Some Alpha Animals forms expose duplicate ability command entries through VE Framework or lose control due to untameable behavior. The equipment removal exception when transforming into a Helixien while wielding a Ratkin Heavy Lance reproduced in both beta.11 and this build.

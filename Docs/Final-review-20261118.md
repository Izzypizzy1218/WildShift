# 최종 재검수 — 2026-11-18

앞선 [성능 점검](Performance-bottom-up-20261117.md)에 이어, 확인된 세 경로를 추가 보완하고 로컬에 반영했다. **원제보의 심한 선택 중 지연을 해결했다고 확정하거나 전체 TPS 향상을 주장하는 변경은 아니다.**

## 추가 변경

| 경로 | 문제와 보완 | 유지한 범위 |
| --- | --- | --- |
| [징집 토글 제작자 판별](../Source/WildShift/Patch_TransformedAnimalDraftUI.cs) | 같은 제작자 Type에서 `Assembly.GetName()`을 반복하던 분류를 `ConcurrentDictionary<Type,bool>`에 저장한다. 재사용 factory도 정적 delegate로 둔다. | hotkey와 현재 callback 제작자는 매번 확인한다. 판별식은 그대로이며 pawn·명령·능력치 상태는 캐시하지 않는다. |
| [건강 효과의 임시 몸 바인딩](../Source/WildShift/SharedFormHealth.cs) | `BodyScope` 생성 도중 native 부위 조회가 예외를 내면 `using`의 Dispose에 도달하지 못한다. 생성자 catch에서 원래 pawn/부위를 복원하고 미완성 부위 매핑을 버린 뒤 원래 예외를 다시 던진다. | 정상 callback과 native 부위 선택을 생략하지 않는다. 재시도 때 정상 매핑을 다시 만들 수 있게 한다. |
| [외형 iterator와 gene callback](../Source/WildShift/SharedFormBiology.cs) | 외형 iterator에 우리 prefix가 진입했는지 양수 토큰으로 구분해, 앞선 외부 prefix의 중단·예외가 바깥 스코프를 지우지 않게 한다. gene Tick 중 복귀/몸 교체가 발생하면 뒤의 callback을 중단한다. | 중첩과 예외 전파를 유지한다. callback 허용 정책과 명시적 opt-in 범위를 넓히지 않으며, 유전자 효과 자체를 제거하지 않는다. |

## 완료한 제한적 검증

| 검증 | 확인한 결과 | 해석 범위 |
| --- | --- | --- |
| 실제 compiled 제작자 분류기 | baseline·후보 모두 **11개 경계 검사 통과**. warm 조회 10만 회 ×5표본의 대표값에서 일반/Draftable 제작자 추가 할당 **19,200,000 B → 0 B**, ISEKAI 제작자 **11,200,288 B → 0 B**. | native Command_Toggle/KeyBindingDef와 생성한 제작자 메타데이터를 쓰는 콘솔 검사다. 실제 add-on 명령 dispatch나 게임 FPS/TPS 측정이 아니다. 최초 cache 구축 비용은 warm 수치에서 제외된다. |
| 실제 native 1.7 렌더·외형 iterator 스코프 | 이전 AD4136 DLL은 선행 외형 prefix 예외에서 바깥 스코프를 보존하지 못하는 실패를 재현했다. 후보 60D549 DLL은 **50 checks 통과**, 그중 외형 스코프는 **24 checks**다. | 완결된 실제 iterator 분기와 주입한 경계 예외를 확인한다. 정상 dynamic-node 생성이나 초기화 게임·1.6 Unity 렌더링의 통과를 대신하지 않는다. 이 실행에는 시간·할당 측정 loop가 없다. |
| native 1.7 API/Harmony | 후보 DLL로 **473 assertions 통과**. | 별도 초기화 게임을 시작하지 않는 API·격리 객체 그래프 검사다. |

## 최종 실게임 확인

- 실제 초기화된 **1.6·1.7 각각 875개 검사 통과**. 세 프로세스에 걸친 비활성/활성 몸 저장·재로드, 복귀, 같은 틱 강화 변경과 새 예외·콜백 재진입 검사를 포함한다. 실행 폴더는 `native16-candidate-v3`, `native17-candidate-v3`이며 정상 사용자 설정 해시도 유지됐다.
- 이전 DLL의 `native17-baseline-red-v2`에서 생성자 실패 후 소유자/부위·부분 캐시 복구 실패와 보관된 몸의 후속 유전자 실행을 재현했다. 새 DLL은 같은 검사를 통과했다. 초기 fixture의 CWT reflection overload 선택과 생성된 hive Lord 정리는 테스트 준비 오류로 수정했고, 해당 중간 실패 기록도 보존했다.
- 실제 ISEKAI add-on의 native 명령 dispatch는 **1.6·1.7 각 12 cases**, 1.7 역순 로딩 **12 cases** 모두 통과했다. 일반 길들인 동물의 버튼, 양쪽 초기 징집 상태, 실제 틱 유지와 인간 복귀를 확인했다. 메타데이터 콘솔의 가상 제작자만으로 호환을 판정하지 않았다.
- 79종 환경에서 적 60명의 native 전투 후 상처·카메라·틱을 고정한 선택/해제/재선택 구간은 **56.76 / 58.81 / 58.58 FPS**, 새 native 오류 **0건**이었다. 기존 외부 모드 로드 오류 **14건** 때문에 전체 wrapper는 FAIL이며, 선택 구간만 유효한 관찰이다. 첫 시도는 카메라가 움직여 비교에서 제외했다.
- 바이너리는 아래 해시로 저장소와 로컬 설치 양쪽에 반영하고 네 파일을 대조했다. 기존 네 DLL은 `before-local-install`에 보존했다. 원격 배포는 하지 않았다.

| 최종 버전 | SHA256 |
| --- | --- |
| 1.6 | `8B536DA06434D70560332E2F7929E91AC14A0B7AFD22D6C888EC790D18237F3B` |
| 1.7 | `60D549C3814AF59BDAC174D480999DFF0701564A6712D145A18182B3C5B05903` |

## 측정 조건 재검증

초기 microbench에서는 빠른 일반 폰 경로가 뒤의 능력치 조회에 제공하던 JIT 준비 시간까지 줄여, 짧은 조회 표본의 시간이 크게 달랐다. 파일별 분리 실험 후 동일한 캐릭터 입력, 준비 시간과 `DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`을 둔 별도 프로필로 비교했다. **MoveSpeed 2만 회의 5표본 중앙값은 최적화 전 52.23ms / 이전 반영본 51.23ms / 이번 후보 51.27ms**였다. 앞선 짧은 표본의 큰 차이는 이 조건에서 재현되지 않았다. 이 환경 변수는 검사 자식 프로세스에만 적용되며 정상 게임 설정을 바꾸지 않는다.

해당 결과는 단일 경로의 준비된 반복 조회다. 실제 정착지의 TPS, 선택 중 전투나 모든 유전자·장비 조합의 처리 시간으로 확대하지 않는다. 제작자 분류 캐시도 최초 생성 비용과 UI 전체 할당을 포함한 값이 아니다.

## 근거

작업 출력 루트는 `outputs/wildshift-final-review-20261118/`이다.

- `draft-metadata-review.json`, `draft-metadata-baseline/result.json`, `draft-metadata-candidate/result.json`: 동일 private 분류기를 호출한 원본·후보의 판별 경계와 microbench. 후보 SHA256은 `60D549C3814AF59BDAC174D480999DFF0701564A6712D145A18182B3C5B05903`이다.
- `appearance-baseline-red-status.json`, `appearance-baseline-red.log`: 이전 DLL의 기대된 실패와 실제 실패 지점.
- `render-final/runner-result.json`, `render-final/run/result.json`: 50개 스코프 회귀와 native/Harmony/실행 DLL 해시.
- `runtime17.log`: 473개 API assertions의 통과 기록.
- `native17-baseline-red-v2/`, `native16-candidate-v3/`, `native17-candidate-v3/`: 실패 재현과 최종 native 회귀.
- `micro17-stable-{preopt,previous,candidate}/`: JIT 조건·준비 시간을 맞춘 반복 조회와 입력 해시.
- `isekai16-final/`, `isekai17-final/`, `isekai17-reversed-final/`: 실제 add-on 명령 dispatch 결과.
- `completion.json`, `final-source-manifest.json`: 최종 설치·검증 요약과 소스 해시. GUI 원본은 `outputs/wildshift-reporter-modlist-20261117/final-review-20261118-available79-frozen-v2/`에 보존한다.
- `scope-review-before/`, `lifecycle/`: 수정 전 소스 및 경계 회귀 준비 자료.

기존 검사 통과를 다른 입력 해시의 새 통과로 옮겨 적지 않는다. 무효·실패·미완결 실행은 보존하며, 미측정 경로를 확정 병목으로 분류하지 않는다.

## beta.9 배포 파일 재검증

버전 표기를 `v1.4.0-beta.9`로 변경한 두 DLL을 별도로 빌드하고, 해당 파일로 실제 초기화된 1.6·1.7 각각 875개 검사를 다시 통과했다. 세 프로세스의 저장·불러오기와 정상 사용자 설정 보존을 확인했다. 생산 코드 74개 파일은 위 최종 검증본과 동일하다. 1.7 API 검사 473개와 별도 native 스코프 검사 50개도 새 DLL로 통과했다. 이 재검증은 전투 선택 지연의 해결이나 새로운 FPS·TPS 측정을 뜻하지 않는다.

| 배포 DLL | SHA256 |
| --- | --- |
| 1.6 | `E41ED495A4D8EACF3BCC2B17D196003D6EFE3B83F0833C703F96AD949CB20CBE` |
| 1.7 | `2CB4124F1AF556DFC978FE8485654F04CB6A6ACCA78F389758001340771B940F` |

첫 1.7 재검사에서는 생성된 파괴 불가능한 모놀리스가 소화 거품의 요청 표적을 막았고, 테스트의 `Open` 대체 경로가 표적을 맵 중앙의 사거리 밖으로 옮겨 검사가 실패했다. 이전 통과 맵에서는 모놀리스가 다른 위치에 있었다. 게임 코드는 바꾸지 않고, Foam·Jump 검사에서 시전자 반경 3칸의 통행·시야 조건을 만족하는 표적을 선택하도록 테스트만 수정했다. 실제 능력의 `ValidateTarget` 검사는 그대로 유지하며, 실패 시 위치·사거리·차단 의상을 기록한다. 수정된 동일 테스트 소스로 두 엔진의 전체 검사를 통과했다.

배포 재검증 출력은 `outputs/wildshift-beta9-publish-20261118/`의 `native16-v2/`, `native17-v2/`, `runtime17/`, `render17/`이다. 최초 실패는 `native17/`에 보존한다. `expected-dlls.json`과 배포 명세는 검사한 파일과 공개 패키지를 연결한다. 앞선 ISEKAI 호환·성능 측정은 해당 원래 입력과 범위에 한정한다.

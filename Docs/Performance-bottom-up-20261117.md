# Bottom-up 성능 점검과 검증 범위

이번 변경은 반복되는 소유자 검색과 불필요한 객체 생성을 줄이고, 다른 모드가 Harmony 처리를 건너뛰거나 예외를 내는 경우의 상태 복원을 보완한다. **원제보의 ‘적이 많은 전투에서 변신·징집한 폰을 선택하면 발생하는 심한 지연’은 재현하지 못했으며, 해결이 확정된 상태가 아니다.** 아래의 미세 경로 개선을 원제보 해결이나 전체 모드팩의 성능 향상으로 해석하지 않는다.

작업은 2026-11-17에 시작해 11-18까지 이어졌다. 초기 측정과 최종 후보의 입력 해시가 다르므로, 최종 후보에 대한 통과 여부와 해시는 별도 최종 결과를 참조한다.

## 2026-11-18 최종 확인

- 실제 초기화된 **1.6·1.7 각각 856개 검사 통과**. 세 프로세스에 걸친 비활성/활성 몸 저장·재로드, 복귀, 동일 틱 강화 변경, native callback, 오라, 초기화 실패·재시도와 다른 prefix의 중단/예외 복원을 확인했다. 최종 실행은 `native16-final-v5`, `native17-final-v5`이며 두 실행 모두 정상 설정 해시가 유지됐다.
- 최종 1.7 DLL로 native API/Harmony **473 assertions**, 렌더 스코프 **26 checks**도 통과했다(`runtime17-complete`, `render-scope-complete`). 1.7 초기화 게임 내 Prefix/Finalizer 50만 쌍의 추가 할당은 baseline **12,000,000 B → 최종 0 B**였다. 렌더링 전체의 메모리나 FPS 향상 수치는 아니다.
- 일반 폰의 runtime 진입 미세검사에서 hediff 목록 길이에 비례하던 검색이 사라졌다. 이는 등록되지 않은 폰에 대한 조기 반환 비용만 측정한 결과다. 공유 MoveSpeed 조회 등은 서로 다른 게임/캐릭터·조건부 modifier 문맥과 임시 테스트 패치의 영향을 완전히 분리하지 못했으므로 해당 표본으로 개선률이나 회귀율을 계산하지 않았다.
- 제공 목록 중 79종을 사용한 실제 1.6 GUI에서 적 60명의 native 전투를 거친 뒤 같은 상처·카메라·틱을 고정하고 선택/해제/재선택을 비교했다. **59.20 / 59.52 / 58.44 FPS**, GUI 평균 **1.249 / 1.012 / 1.280 ms**였고 세 구간 모두 상태·카메라가 같고 새 native 오류는 없었다. 기존 외부 모드 로드 오류 14건 때문에 전체 wrapper는 FAIL이며, 완료한 선택 구간만 유효한 관찰로 남긴다. 전체 모드팩 통과나 원제보 해결의 근거가 아니다.
- 같은 시간대의 완결된 추가 50명 후보 표본은 **398.47 µs/tick / 891.13 TPS**였다(`paired50-B3-final-after`). 기존 A1/B1은 **394.81 / 380.38 µs/tick**였으나 후보 해시가 다르다. B2와 뒤이은 A3는 도구 세션 중단으로 완료하지 못해 제외했다. 반복 비교가 완성되지 않았으므로 최종 후보의 정착지 성능 향상률은 확정하지 않는다. 초기 0/20/50명 표본의 마지막 private memory는 약 **796–815 MiB**, managed heap은 **638–651 MiB**였으며, 조건별 60초 시험으로 장시간 누수 유무를 판정하지 않는다.
- 최종 DLL을 저장소와 정상 로컬 설치의 두 버전에 복사하고 총 네 파일의 해시를 대조했다. 기존 DLL 네 개는 `before-local-optimization-install`에 보존했다. 원격 배포는 하지 않았다.

| 최종 DLL | SHA256 |
| --- | --- |
| 1.6 | `C7AD1CF069FE0CA8BE0845BEB3941729B658AAAF3857449549D37F4A0038C0C7` |
| 1.7 | `AD4136D95A19C1B150A7486635CF4FE365364BABAF9EE3CE7DD6A200EB66BE6C` |

최종 GUI 기록은 `outputs/wildshift-reporter-modlist-20261117/bottomup-final-available79-ratkin-triceratops60-frozen/`에 있다. 이번 과정에서 드러난 fixture의 상속 MethodInfo 선택, JIT에 의해 관찰되지 않는 작은 getter 후크, 무작위 초기 장비·약물 상태의 혼입은 검사를 고쳐 해결했다. 실제 효과의 기대값을 약화하거나 계산 결과를 대체하지 않았으며, 실패·불완전 실행은 그대로 보존했다.

## 변경한 여섯 영역

| 영역 | 변경 내용 | 유지한 동작과 검증 경계 |
| --- | --- | --- |
| 일반 폰의 런타임 tick gate | [SharedFormRuntime](../Source/WildShift/SharedFormRuntime.cs)에 초기화를 마친 몸의 약한 참조 membership을 둔다. 등록되지 않은 일반 폰은 전역 Tick/TickInterval 후크에서 전체 hediff 검색 전에 반환한다. | 전역 후크 자체는 유지한다. 등록된 몸은 매번 살아 있는 원본과 실제 소유 관계를 다시 확인한다. 초기화 전 등록 해제, 모든 모듈의 초기화 성공 후 등록, 예외 시 해제, Release 선행 해제를 적용한다. 능력치 결과의 영구·틱 캐시가 아니다. |
| 렌더링 스코프의 객체 할당 제거 | [SharedFormGear](../Source/WildShift/SharedFormGear.cs)의 render-tree Prefix/Finalizer만 class IDisposable 대신 양수 진입 깊이 토큰을 사용한다. 일반 NativeOnly API는 유지한다. | 기본값 0은 우리 prefix가 실행되지 않았음을 뜻한다. 앞선 외부 prefix의 throw/false에서 바깥 스코프를 지우지 않으며, 정상 중첩·원본/후행 예외에서는 원래 깊이를 복원한다. 일반 폰·초상화의 이미 준비된 render-tree 조회에도 적용되는 경로다. |
| 장비·해부학의 중복 원본 검색 제거 | [SharedFormGear](../Source/WildShift/SharedFormGear.cs)와 [SharedFormAnatomy](../Source/WildShift/SharedFormAnatomy.cs)가 바로 앞에서 검증한 원본을 같은 함수 호출 안의 Refresh에 전달한다. | 외부 callback을 건너뛰어 원본을 재사용하지 않는다. 장비/hediff 목록 비교, 장비 교체·품질·내구도, 보철 signature와 동적 stage 검사는 유지한다. 호출 사이에 결과를 저장하는 새 캐시는 없다. |
| 건강 투영 membership·callback 할당 | [SharedFormHealth](../Source/WildShift/SharedFormHealth.cs)가 같은 refresh에서 만든 sourceSet으로 낡은 투영을 찾는다. 피해·verb 사용·entropy·처치 알림은 기존 BodyScope struct 안에서 직접 전달한다. | source 객체, callback 횟수·순서·인수, 일시적인 몸/부위 바인딩과 예외 후 복원을 유지한다. Action을 다시 Func로 감싸는 closure도 제거한다. 실제 효과나 callback 자체를 생략하지 않는다. |
| aura 첫 번째 순회의 빠른 제외 | [SharedFormHealthEffects](../Source/WildShift/SharedFormHealthEffects.cs)가 null/self/healthless/dead/범위 밖 대상을 원본 identity 검색 전에 거른다. 최종 후보는 health null 검사를 Dead 조회보다 먼저 둔다. | 기존 거리 비교식, native 대상 필터, 실제 buff 생성·링크·소멸 타이머 갱신 순서는 유지한다. native 전체 순회와 변신한 source의 기존 링크 보정 순회를 제거한 변경은 아니다. |
| 자동 공격의 미진입 상태 복원 | [Patch_TransformedAutomaticCombat](../Source/WildShift/Patch_TransformedAutomaticCombat.cs)이 previous와 entered를 담는 값 형식 상태를 사용한다. | 앞선 외부 prefix가 먼저 throw/false하여 우리 prefix가 실행되지 않았으면 Finalizer는 아무것도 복원하지 않는다. 우리 처리에 진입한 뒤 eligibility 조회나 원본이 예외를 내면 이전 자동 공격 문맥을 복원한다. 수동 공격과 native 표적 검색 정책을 바꾸지 않는다. |

## 검증 종류를 구분한 기록

| 검증 | 확인할 수 있는 것 | 확인할 수 없는 것 / 결과 범위 |
| --- | --- | --- |
| 실제 1.6·1.7 엔진의 공유 상태 회귀 | 실제 pawn/장비/health 객체, 같은 틱 변경, 저장·복귀·소유 관계와 callback 경계 | 초기 baseline은 두 버전 각각 **762 assertions 통과**. 최종 후보의 추가 회귀·입력 해시·오류 목록은 별도 최종 결과 참조. assertion 수를 FPS/TPS 결과로 해석하지 않는다. |
| 실제 1.6·1.7 엔진 내부 microbench | 실제 runtime/query 경로의 반복 호출 비용과 결과값 | 전체 pawn tick이나 정착지 성능이 아니다. 1.6 Mono에서 allocation 계측이 지원되지 않는 표본의 0은 ‘할당 없음’의 증거로 쓰지 않는다. 최종 후보의 수치는 별도 최종 결과 참조. |
| 1.7 render-scope 콘솔 회귀 | 실제 native render-tree 메서드에 Harmony를 적용한 중첩·skip·예외 복원 | 별도 게임을 시작하지 않는 콘솔 검사다. 화면 렌더링, 선택 GUI의 FPS, 1.6 Unity 동작 검증과 구분한다. 미진입을 구분하지 않은 초기 int 후보는 폐기했으며 최종 후보는 양수 진입 토큰을 사용한다. |
| 1.7 headless population 측정 | 총 50명이 정지한 고정 상황에서 변신자 0/20/50명일 때의 전체 native tick 비용. 조건별 측정 길이는 60초 | 렌더링·마우스 선택·전투 표적 검색이 없는 별도 부하다. 제보의 선택 중 끊김이나 전투 FPS를 재현하는 시험이 아니다. |
| 선택 GUI 제보 확인과 정적 호출 분석 | 선택/비선택 조건과 실제 소비 경로의 구분 | 앞선 제한된 재현에서 원제보의 심각한 지연은 확인하지 못했다. 그 결과를 모든 저장·장비·유전자·모드 조합에 확대하지 않는다. |

지속 회귀는 [runtime 등록 검사](../Tests/SharedState17/RuntimeRegistrationEngineChecks.cs), [건강 callback 검사](../Tests/SharedState17/SharedHealthEngineChecks.cs), [aura 검사](../Tests/SharedState17/SharedAuraPerformanceEngineChecks.cs), [자동 공격 검사](../Tests/SharedState17/SharedAutomaticCombatEngineChecks.cs)에 보존한다. 공유 fixture는 1.6·1.7에서 해당 검사를 연결한다. [render-scope 전용 runner](../Tests/Run-RenderScope17Tests.ps1)는 외부의 새 출력 폴더와 예상 production DLL SHA256을 요구하고, 실제 실행 DLL/native/Harmony 입력 해시를 확인한다.

### 첫 population 비교는 속도 효과의 근거에서 제외

| 변신자 수 / 전체 50명 | 초기 before µs/tick | 초기 after µs/tick |
| ---: | ---: | ---: |
| 0 | 256.7 | 258.5 |
| 20 | 299.3 | 336.0 |
| 50 | 384.2 | 478.2 |

이 사이 시스템 시각의 큰 점프와 MUpdate2 updater의 CPU 한 코어 사용이 관측되었다. 환경의 동등성이 보장되지 않아 위 표는 기록으로만 남긴다. 모드가 빨라졌거나 느려졌다는 판단, background updater가 차이를 전부 일으켰다는 인과 판단에 사용하지 않는다.

가까운 시점에 다시 측정한 50명 A1(old)/B1(candidate)은 각각 **394.8 / 380.4 µs/tick**, 실제 진행 속도는 약 **900 TPS**였다. 이는 완성된 반복 A/B 결과가 아닌 한 쌍의 관찰이다. 뒤이은 B2는 도구/session 단절로 60초 중 약 40초만 확보되어 완성 표본에서 제외한다. 반복·교차 비교가 끝나기 전에 개선률이나 TPS 상승을 확정하지 않는다.

마지막 aura의 health-null/Dead 검사 순서 보완은 비교 뒤의 입력 변경이다. 따라서 앞선 candidate의 수치를 새 `candidate*-complete` 해시의 성능 결과로 옮겨 적지 않는다. 최종 바이너리와 같은 해시로 수행한 회귀·추가 측정은 별도 최종 결과 참조.

### 무효화한 중간 검사와 최종 판정 기준

- 초기 render int 후보는 미진입 default state가 바깥 스코프를 지우는 문제가 있었다. 선행 prefix 예외를 넣은 실제 native 회귀에서 실패를 확인하고 폐기했다. 최종 양수 토큰 버전은 미진입 0을 구분한다. 외부 콘솔의 원본 24 MB / 초기 후보 0 B 관찰을 최종 버전의 새 FPS·TPS 측정으로 바꾸지 않는다.
- 첫 candidate native health 검사 실패는 상속된 callback의 MethodInfo를 고르는 fixture 오류였다. DeclaredOnly로 대상 선택을 바로잡았으며, 이 실패를 생산 callback 오류로 분류하지 않는다. 수정한 fixture와 최종 후보의 실행 판정은 별도 최종 결과 참조.
- 정상 프로필·세이브 보존, native 오류 유무, loaded input hash 일치 여부는 각 실행 결과를 따른다. 이전 통과를 다른 해시의 최종 후보 통과로 대신하지 않는다.

## 정적 coverage와 남긴 위험

shared/runtime/health 경로와 별도로 나머지 source 59개를 진입점·전체 목록 접근·반복·할당 기준으로 읽었다. 적 변신 MapPostTick은 queue가 비면 전체 pawn을 훑지 않는다. 전체 목록 접근은 로드 복원, ColonistBar entriesDirty, 게임 종료 판정의 120틱 캐시 만료/무효화, Lone 시작 복구의 제한된 재시도, 2500틱 간격 복수 기록 처리 등에 묶여 있다.

변신·각성의 native PawnGenerator, 대량 복수 무리 생성, 아주 큰 사용자 규칙 목록의 설정 GUI는 입력 규모에 따른 순간 비용이 남는다. 능력 공급원 목록의 반복 Contains/유전자 검사, 공유 stage의 반복 유효성 확인, 조건부 외부 모드 reflection도 계측 후보로 남겼다. 근거 없이 broad pawn/tick/frame 캐시, 유전자·건강 효과 생략, 레이드 규모 상한을 추가하지 않았다.

native `GizmoGridDrawer`의 collection cache는 **1.6.4871 rev591과 1.7 모두** 같은 프레임·같은 순서의 선택 객체 목록에서 GetGizmos 재열거를 막는다. command의 Disabled/사용 가능 상태 재평가는 별도 경로다. 1.6 확인 DLL 해시와 이전 실제 실행의 rev591은 일치하며, 해당 engine의 Version.txt에 남은 rev590은 오래된 텍스트 값으로 구분했다.

세부 외부 근거는 workspace의 `outputs/wildshift-bottomup-20261117/` 아래 `bottomup-ui-audit.md`, `nonshared-source-coverage.json`, `shared-query-audit.md`, `runtime-aura-audit.md`, `native16/gizmo-cache-cross-version.json`과 각 실행의 원본 결과에 보존한다. **최종 결과도 원제보의 심각한 선택 지연을 재현한 증거가 없는 한 ‘원제보 해결 확인’으로 기록하지 않는다.**

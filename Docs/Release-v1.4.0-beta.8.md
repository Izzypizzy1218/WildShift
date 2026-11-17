# WildShift v1.4.0-beta.8

## 한국어

- **자동 전투 행동 수정:** 변신동물이 점프팩 같은 수동 전용 행동을 임의로 사용하던 문제를 수정했습니다. 플레이어가 직접 내리는 점프 명령은 그대로 사용할 수 있습니다.
- **첫 변신 지연 감소:** B&S·Vanilla Genetics Expanded 호환 타입을 찾을 때 발생하던 불필요한 전체 검색을 제거했습니다. 동일한 60명 적 테스트에서 첫 변신 시간이 약 **295ms → 41ms**로 줄었습니다.
- **장비 공유 캐시 개선:** 갑옷 내구도·품질·페르소나 특성 변화가 관련 없는 공유 건강 효과까지 재생성하던 처리를 줄였습니다. 실제 장비 능력치 변화는 즉시 반영됩니다.
- **생각 조회 처리 개선:** 인간의 이념을 잠시 참조하는 동안 효과가 그대로인데도 건강 캐시를 반복 초기화하던 문제를 수정했습니다. 실제 강화·부작용이나 장비 교체에 따른 변화는 계속 즉시 반영됩니다.
- **징집한 가축형 변신자 처리 수정:** 방목 가축으로 분류되는 동물 형상도 징집 중에는 적의 정상적인 전투 대상으로 인식됩니다. 일반 가축의 보호 규칙은 유지됩니다.
- **사회적 상호작용 오류 수정:** 상호작용 tracker가 있는 변신동물의 중복 진행을 제거하고, 복원된 상단·월드 상태에서 맵 전용 상호작용을 실행하다 발생하던 오류를 수정했습니다. 유전자·나이·욕구 진행은 유지됩니다.

- **창작마당 제목 복구:** 한국어 제목이 비워지던 업로드 처리를 수정했습니다. 한국어·영어 제목과 설명을 함께 지정하고 배포 후 다시 확인합니다.

**검증:** 실제 RimWorld 1.6·1.7에서 각각 **751개 검사**를 통과했습니다. 모든 DLC를 활성화하고, 세 개의 새 게임 프로세스에 걸친 저장·불러오기를 포함했습니다.

**남은 확인 사항:** 적이 많은 전투에서 변신동물을 선택했을 때 발생한다는 심한 끊김은 아직 완전히 해결됐다고 확인하지 못했습니다. 위 첫 변신 측정은 특정 테스트 결과이며, 전체 전투 FPS·TPS 개선율을 뜻하지 않습니다.

## English

- **Automatic combat actions:** Fixed transformed animals automatically using manual-only actions such as jump packs. Explicit player-issued jump orders remain available.
- **First-transformation latency:** Removed an unnecessary exhaustive type search in B&S and Vanilla Genetics Expanded compatibility lookup. In one matched 60-hostile test, the first transformation dropped from approximately **295ms to 41ms**.
- **Shared gear caching:** Reduced unrelated shared-health rebuilds when armor condition, quality or persona traits change. Actual equipment stat changes still apply immediately.
- **Personal thought queries:** Fixed repeated health cache resets when temporarily consulting the original person's ideology leaves the effects unchanged. Real buffs, penalties and equipment replacements still update immediately.
- **Drafted livestock forms:** Controllable, drafted shapeshifters in roaming livestock forms are now recognized as normal combat targets by enemies. Ordinary livestock protection remains intact.
- **Social interaction ticking:** Removed duplicate interaction intervals on transformed animals with an interaction tracker, and fixed map-only interaction code running on restored caravan/world forms. Gene callbacks, aging and needs continue normally.

- **Workshop title repair:** Fixed the upload path that left the Korean title blank. Localized titles and descriptions are now submitted together and checked again after publication.

**Validation:** Passed **751 checks each** in actual RimWorld 1.6 and 1.7 with all DLCs enabled, including save/load coverage across three fresh game processes.

**Still under investigation:** The reported severe slowdown when selecting a transformed pawn during large battles is not yet confirmed fully resolved. The first-transformation measurement above is specific to that test and does not represent an overall combat FPS or TPS improvement rate.

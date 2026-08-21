# LeakResponse → Integration 인수인계

> LeakResponse 담당 Codex는 작업 종료 전에 아래 내용을 실제 결과로 교체한다. 빈 항목을 삭제하지 말고 `없음` 또는 `미검증`으로 표시한다.

## 작업 식별

- 브랜치: `feature/leakresponse`
- commit: 미커밋 작업 트리 (`febb455` 위 변경)
- 기준 develop commit: `febb455`
- 작성자/작성일: Codex / 2026-08-20

## 변경 목적과 동작

- P-102에서 검증된 `INT` 누출 대응 리그, 배치 Ghost 루트, LeakResponse 진입점을 작성 위치 그대로 사용한다.
- 흡입·토출 밸브, 누출 VFX, LeakPoint, 흡착삭·패드, 집게, 폐기물 용기를 한 리그로 유지해 시설별 복제 코드 없이 같은 상호작용을 사용한다.
- P-102 설비 모델의 기존 Collider가 없는 상위 렌더 단위에 런타임 BoxCollider를 보강한다. Valve/Wheel/Ghost/Leak 계층은 기능 Collider와 충돌하지 않도록 제외한다.
- `INT/StageBagAnchor`로 공용 대응 가방을 P-102 작업 위치에 고정한다.
- LeakPoint에 완전 3D 공간 누출음을 생성한다. 외부 음원 자산을 추가하지 않고 절차형 hiss를 사용하며 토출 밸브 `LeakIntensity01`에 따라 음량이 감소하고 정지한다.
- 기존 흡착재 Ghost의 HandGhost 전용 셰이더를 런타임 URP Unlit 투명·양면·ZWrite Off 재질로 교체해 자홍색/한쪽 눈/뒤에서만 보이는 문제를 방지한다. 설치 후에는 원래 불투명 재질을 복원한다.
- 기존 뚜껑 근접 스냅, 집게 Grip Toggle + Trigger 회수, 컨트롤러 계층 추적 기반 stick 이동 안정화 로직을 유지했다.
- 자막과 WAV는 P-102 고정 대본을 처음부터 재생한다.
- 단계 진입·완료, TTS 시청, 가방 장비 요청, 흡착재·집게 사용을 공용 텔레메트리 계약으로 보고한다. 점수·DB·대시보드 UI는 추가하지 않았다.

## 변경 파일

- `Assets/Scenes/06_LeakResponse.unity`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/LeakResponseFacilityBinding.cs`의 `P102LeakEnvironmentSetup`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/LeakResponseSpatialLeakAudio.cs` + meta
- `Assets/3.SSH/Scripts/LeakResponse/Narration/LeakResponseTelemetryReporter.cs` + meta
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102LeakRuntimeBootstrap.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102LeakNarrationController.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102SharedBagItemHandler.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102RecoveryTongsRuntime.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102RuntimeUtility.cs`
- `Assets/3.SSH/Scripts/ObjectInstallation/PreplacedGhostInstallItem.cs`
- `Assets/3.SSH/Editor/LeakResponseIntegrationTests.cs` + meta
- `docs/handoffs/leakresponse/LATEST.md`

## Scene·Prefab·Inspector 수동 변경

- `06_LeakResponse.unity`의 `INT` 아래 `StageBagAnchor` 추가
  - local position: `(-0.45, 0.05, -4.8)`
  - local rotation Y: `180°`
- 그 외 런타임 바인딩·Collider·AudioSource·텔레메트리 컴포넌트는 Bootstrap이 생성하며 Scene에 직렬화하지 않는다.

## 공용 계약 사용

- 입력/장비: 기존 `SharedXRInputContract`, `SharedPlayerEquipment`, `SharedEquipmentInventory`, `SharedEquipmentIds`
- 상태: `SetLeakState(LeakState.Isolated)`
- 텔레메트리: `SharedTrainingTelemetry.Current`
  - 단계: SuctionValve, DischargeValve, AbsorbentSock, AbsorbentPad, Recovery, WasteDisposal
  - 콘텐츠 ID: `LeakResponse.TTS.01`~`LeakResponse.TTS.26` 중 실제 완료된 Cue
  - 장비: AbsorbentSock, AbsorbentPad, RecoveryTongs
- 계약 사용 위치: `P102LeakEnvironmentSetup`, `LeakResponseTelemetryReporter`, `P102SharedBagItemHandler`, `P102RecoveryTongsRuntime`

## 검증

- Unity 6000.4.8f1 스크립트 재컴파일: 성공, 마지막 Domain Reload 이후 새 `error CS` 0개
- LeakResponse EditMode + 기존 회귀 테스트: 이번 Integration 변경 후 미실행
- 코드 정적 확인: 시설 선택 API·시설별 위치 이동 분기 0개, P-102 고정 환경 구성 확인
- `git diff --check` (LeakResponse 허용 경로): 통과
- App 전체 PlayMode Smoke: 미검증
- Quest 실기기 확인: 미검증

## 알려진 문제와 위험

- 런타임 복합 Collider의 실제 보행 감각과 Ghost 양안 렌더링은 Quest 실기기 재확인이 필요하다.
- 클래스/로그와 런타임 시나리오는 P-102 고정이다.

## Integration 요청

- LeakResponse 활성화 전에 PreEntry의 P-102 정지 상태가 `SharedIncidentState`에 기록되어야 한다.
- 병합 후 P-102 App 전체 전환 Smoke를 실행한다.
- 텔레메트리 저장/UI 구현은 Integration에서 공용 계약 수신부로만 연결하며 LeakResponse 코드를 직접 참조하지 않는다.

## 권장 병합·재검증 순서

1. 변경 파일 범위 확인
2. `feature/leakresponse` EditMode 테스트 실행
3. LeakResponse 단독 실행에서 P-102 위치, Valve 양손, 3D Sound, Ghost 양안, 가방, 집게 Trigger, 뚜껑 근접 스냅 확인
4. `feature/leakresponse → develop` 병합
5. App 전체 PlayMode Smoke와 Quest 실기기 확인

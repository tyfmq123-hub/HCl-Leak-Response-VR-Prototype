# Leak 물방울 / 가방 흡착재 플레이어 충돌 수정

역할: Integration. 작업 브랜치: `integration/xr-height-stability-20260907`.
이전 공용 XR 높이 수정 위에 이어서 작업했으며 commit/push는 하지 않았다.

## 실제 재현과 수정

### 흡착패드가 사람을 들어 올림

- 실제 App의 `P102SharedBagItemHandler`에서 Grip press/release → 기존 `INT_AbsorbentPad_01`을 꺼내는 경로로 재현했다. 가방 진입 위치에 실제 공용 CharacterController를 놓고 72Hz XRI/PhysX로 계산했다. 수정 전 패드가 0.28초 동안 상승할 때 플레이어 원점이 **1.14067m** 올라갔다.
- `PresentAtBagMouth`는 기존 패드를 kinematic으로 바꾸고 가방 Anchor 아래에서 위로 이동한다. 약 1m 폭의 solid collider가 플레이어 capsule과 충돌해 capsule을 밀어 올렸다. 카메라 높이 보정 문제가 아니라 공급 물체와 몸통 사이 물리 충돌이었다.
- `P102AbsorbentPlayerCollisionGuard`를 실제로 꺼낸 흡착삭/패드에 한 번만 붙인다. 활성 XROrigin의 CharacterController와 해당 공급 물체 solid collider 사이만 `Physics.IgnoreCollision` 처리한다. 재활성화로 물리 pair가 초기화될 때도 복구한다.
- Grab 검색에 사용하는 기존 Default layer, collider 크기/위치, 바닥/환경 충돌, trigger, Rigidbody/집기/설치 흐름은 유지한다. 새 장비를 복제하지 않고 기존 소스 인스턴스를 사용한다.
- 수정 후 동일 배치에서 Sock/Pad 상승 중 원점 상승량 **0.00000m**. 실제 XRI 선택/해제, source/capsule 재활성화, 기존 설치 흐름도 검사했다.

### PreEntry → Leak 진입 후 물방울만 멈춤

- LeakResponseOnly에서는 정상 재생. 전체 과정에서 PPE 완료를 주입한 뒤 실제 PreEntry additive 대기 → `CompletePreEntryAndHandoff`로 진입하면 `FallingDrops`가 `active=true, intensity=0.7, emission=7, playing=false, particleCount=0`이었다. 미스트는 별도 system이라 계속 보였다.
- 이전 검사는 emission 값만 확인해 이 재생 중단을 놓쳤다. 새로운 검사는 테스트 코드에서 Play/강도/culling을 만지기 **전** 실제 particleCount와 재생 상태 및 D3D11 렌더를 확인한다.
- 추가 진단에서 렌더 노출과 AlwaysSimulate 변경만으로는 멈춘 시스템이 재개되지 않았고, Play 호출 시 실제 입자가 다시 나왔다. 최종 수정은 크기/재질/culling을 변경하지 않는다.
- `HClAbsorptionVisualController.LateUpdate`에서 활성 Stage의 양수 누출 상태와 실제 입자 재생을 동기화한다. 시작 콜백 이후 물방울이 멈춰 있으면 기존 `UpdateFallingDropsEmission`으로 재개한다. 누출량이 그대로라 Valve 이벤트가 다시 오지 않아도 동작한다.
- Pause(timeScale=0), 비활성 component, 누출량 0, 흡수/정리 완료에서는 재생을 되살리지 않는다. 밸브 상태/미스트/물방울 형태/누출 위치는 바꾸지 않았다.

## 변경 파일

- `Assets/3.SSH/Scripts/LeakResponse/HClAbsorptionVisualController.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102SharedBagItemHandler.cs`
- 신규 `Assets/3.SSH/Scripts/LeakResponse/Narration/P102AbsorbentPlayerCollisionGuard.cs` + `.meta`
- 신규 `Assets/Editor/IntegrationLeakSuppliesValidation.cs` + `.meta`
- 신규 `Assets/Editor/Tests/LeakSuppliesRegressionTests.cs` + `.meta`
- 이 문서와 `docs/handoffs/leakresponse/LATEST.md`

## 검증

- Unity 6000.4.8f1 별도 Integration worktree에서 Edit Mode **174/174 PASS**, C# 컴파일 오류 0.
- `IntegrationLeakSuppliesValidation.Run`: 실제 App LeakResponseOnly, 실제 Grip handler/기존 Sock→Pad, XRI 선택/해제, collision pair 재활성화, 바닥/Grab layer 보존, 실제 DIS 밸브 닫힘, 최종 흡수 정리 PASS.
- `IntegrationLeakSuppliesValidation.RunFull`: 실제 App Full → PPE 완료 주입 → PreEntry additive → Leak handoff. 수정 후 변경하지 않은 물방울이 실제로 재생되고 렌더에서 확인됨. Pause/Resume 및 0 → 양수 누출 전환 PASS.
- `Logs/LeakSuppliesValidation/result.txt`와 화면 `02-front-leak.png`, `03-side-leak.png`는 opt-in 검증 실행 시 생성한다. 검증은 Scene을 저장하지 않는다.
- 최종 단독 검사 26 PASS / 전체 handoff 검사 28 PASS. 실제 DIS 밸브를 닫은 뒤 물방울이 정지하고, 전체 흡수 정리 8초 뒤에도 재생되지 않음을 확인했다.
- 공용 XR 높이 회귀 90 PASS: 두 키 높이에서 PPE/PreEntry/Leak의 12cm 턱 18회 왕복, 반복 crouch/stand, PPE 측정기·책상, Pause·전환 중력 잠금.
- 7개 Scene/직렬화/Build Settings 검사 `Failures=0`. 기존 뚜껑 6초/FixedUpdate 300회 → handoff 위치/회전 0 오차, 원통 벽 16개+바닥 collider, 이탈 뚜껑 복귀 검사 PASS.
- App 전체 전환 smoke `RESULT PASS`: 전체 과정과 PPE/PreEntry/Leak 단독 진입, 공용 XR 1개, PreEntry 측정기, 가방 Sock→Pad, 완료 화면 흐름 보존.
- Git 누락/GUID 검사: 3,950 경로 / 2,130 GUID PASS (`--skip-lfs-fsck`, 기존 대용량 자산은 이번 수정 대상 아님).

## 보존 / 수동 확인 / 한계

- 이번 수정은 Scene/Prefab/Audio/Material/ProjectSettings를 변경하지 않았다. 사용자가 맞춘 뚜껑/고스트/가방/설비 배치, TTS, 훈련 순서, 완료 이벤트, 서버 전송 계약을 유지한다.
- 원본 `E:\WorkSpace\HCl-Leak-Response-VR-Prototype-main`에 같은 코드와 검사 파일을 반영했다. 원본 Git 브랜치/기존 변경/스테이징은 건드리지 않았다.
- Unity Play 종료 후 컴파일이 끝나면 `01_App`부터 다시 실행한다. setup 메뉴나 Scene 재생성이 필요 없다.
- 코드 반영 시점 원본 Assembly-CSharp.dll은 17:17의 이전 컴파일이었다. 이번 174개 테스트/실행 검증은 별도 worktree Unity에서 완료한 것이며, 원본 Play 세션이 자동으로 최신 코드로 바뀌었다고 가정하면 안 된다.
- 검사는 실제 App/PhysX/렌더링을 사용하지만 HMD/버튼/일부 완료 cue는 주입했다. 두 사용자의 실제 Quest Link 착용 상태를 대신한 것은 아니다. 가방 가까이서 패드 꺼내기와 PreEntry→Leak 이동 후 물방울을 Link에서 최종 확인해야 한다.
- 검증 Unity 시작 때 Unity Editor Search 내부 `ArgumentOutOfRangeException`이 별도로 관측된다. 최종 훈련 테스트 경로의 프로젝트 Exception 및 C# 컴파일 오류와 구분한다.
- 보존 검사 첫 시도는 화면 캡처를 요구하는 기존 검사에 `-nographics`를 사용해 Null graphics RenderTexture에서 Unity native crash가 발생했다. `-force-d3d11`로 재실행해 성공했다. 실제 프로젝트 코드/씬을 바꿔 회피하지 않았으며 원본 Unity는 종료하지 않았다.
- 추가 Integration 요청 없음. 커밋/푸시 미실행.

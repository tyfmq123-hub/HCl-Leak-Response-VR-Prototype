# Integration — Game View 복구와 씬 전환 로딩 조정

## 범위

- 브랜치: `integration/vr-regression-20260907` (develop 기반, 직전 검증 커밋 `3534c1c`).
- 적용 대상: `E:\WorkSpace\HCl-Leak-Response-VR-Prototype-main`.
- 원본 반영 후 17개 경로의 내용 또는 삭제 상태가 검증본과 모두 일치함을 확인했다. 이 후속 변경에서 Stage Scene/Prefab/Audio/ProjectSettings/Packages 변경은 0개다.
- 기존 원본의 수정 대상 파일을 비교해 내용 변경이 없음을 확인했다. 줄바꿈 차이만 정규화해 비교했다. 원본의 다른 미커밋 변경은 보존한다.
- Scene, Prefab, HMD FOV/projection, 입력 계약, TTS/음성 파일, 뚜껑·집게·고스트·풍향계 작성 위치, 피드백/텔레메트리 전송은 변경하지 않는다.

## 변경 파일과 동작

1. 삭제: `Assets/Editor/XRGameViewFullFrame.cs`, `XRGameViewFullFrame.shader` 및 두 `.meta`.
   - 기존 코드는 `endContextRendering`에서 정상 CameraTarget을 검게 지우고 XR render-pass texture를 직접 다시 그렸다. 텍스처가 null이 아니어도 현재 mirror용으로 읽을 수 있는 영상인지 보장하지 못한다. 사용자 보고의 검은 여백/빈 중앙 화면과 맞는 위험 경로다.
   - 이 덮어쓰기와 별도 `context.Submit()`을 완전히 제거했다. Unity/URP/XR provider의 기본 mirror가 출력한다. HMD 영상/FOV를 변경하거나 관전자 카메라를 추가하지 않는다.
   - 이전 Full Eye 메뉴도 제거한다. 전체 눈 시야 확장 기능은 철회했다. 기본 mirror 복구와 Quest 합산 시야를 그대로 표시하는 것은 다른 요구이며, 후자는 완료로 주장하지 않는다.
2. 수정: `Assets/Scripts/App/SceneFlowController.cs`.
   - 공용 App/XR camera를 계속 유지한다. 기존 additive load plan과 PreEntry→이미 로드된 LeakResponse handoff는 그대로다.
   - 기존 Stage 해제와 장비 정리, unload 이후 다음 load, 비활성 dependency 준비, handoff의 shared-XR 설정/Leak 활성화를 각각 프레임 경계로 분리한다.
   - primary Stage 활성화와 lifecycle 연결 사이에는 임의 지연을 추가하지 않는다. 기존 narration startup 순서를 보존한다.
   - 의도된 짧은 암전 fade는 유지한다. Quest 시스템 모래시계를 숨기거나 강제로 XR subsystem을 중지/재시작하지 않는다.
3. 추가: `Assets/Scripts/App/SceneLoadingBudget.cs` 및 `.meta`.
   - 전환 중만 `Application.backgroundLoadingPriority=Low`로 비동기 자산 통합 작업량을 낮춘다. 정상/복구 완료, iterator 정리, App 파괴 때 이전 값을 복구한다. 다른 시스템이 새 값을 썼다면 덮어쓰지 않는다.
   - Unity 6 문서상 Player에서는 Low가 프레임당 2ms 자산 통합 예산이며, Editor에는 효과가 없다. Awake/Start 전체 시간의 상한도 아니다. [Unity 문서](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-backgroundLoadingPriority.html)
4. 테스트: `Assets/Editor/Tests/SceneLoadingBudgetTests.cs`(+meta), `IntegrationVrRegressionTests.cs`.
   - 원래 우선순위 4종 복구/예외 복구/중복 dispose/다른 소유자 설정 보존/미러 override 부재 검사.
   - 삭제된 미러의 합성 텍스처 GPU 복사·비율 테스트 5개 제거. 이 테스트들은 실제 Quest mirror를 검증하지 못했으므로 당시 통과를 실제 XR 정상 출력의 근거로 쓰면 안 된다.
5. 진단: `Assets/Editor/IntegrationTransitionProbe.cs`(+meta).
   - 명시적으로 `IntegrationTransitionProbe.RunSmoke`를 실행할 때만 활성화되는 Editor 전용 진단. LateUpdate에서 전환 구간별 긴 프레임과 카메라 유지 여부를 기록한다. 일반 플레이/빌드에는 실행되지 않는다.

## 검증 결과

- Unity **6000.4.8f1**, 별도 검증 worktree에서 실행. 사용자 원본 Unity/Quest의 실행 중인 세션은 강제로 종료하지 않았다.
- Edit Mode **162/162 Passed**, failed/skipped 0. 결과: `outputs/xr-mirror-editmode.xml` (작업 폴더).
- C# 컴파일 오류 **0개**: Edit Mode, App smoke, Interaction regression의 세 실행 로그 검사.
- App Play Mode **33개 검사 PASS**: 전체 과정/재시작/처음으로/세 씬 단독 실행. 공용 XR/camera 1개와 동일 camera instance, 측정기 꺼내기/잡기, additive 연결 등 포함. 완료 이벤트는 자동 검사에서 주입하므로 실제 사용자의 전 과정 수행과 구분한다.
- 전환 probe **16개 구간 모두 lostCameraFrames=0**, 종료 시 기존 loading priority 복구. Batch Editor에서는 실제 XR mirror rendering이 실행되지 않아 renderedFrames=0이다. 이를 HMD 프레임 유지 증거로 해석하지 않는다.
- 수정 후 CPU 관측치에는 최초 Title 763.9ms, PPE 170.6ms 등 긴 프레임이 아직 있다. 이 테스트는 XR 미실행 Batch/Editor 캐시 조건이므로 Quest 수치도, 안정적인 전후 성능 비교도 아니다. 모래시계 완전 제거가 검증됐다고 주장하지 않는다.
- App→LeakResponse 실제 로딩 후 작성 뚜껑 pose 보존/놓치기·선택 취소 복원, 집게 Grip/Trigger 20회 유지/최종 ghost에서만 반납/입력 복구, 물방울 emission 및 flange 정방향 `3초` 검사 **PASS**.
- 렌더 캡처: `Logs/VRRegression/01-runtime-closed.png`, `02-after-cancel.png`, `03-flange-countdown.png`. 이는 검증용 카메라 캡처이지 실제 Quest mirror 캡처가 아니다.
- 기존 Unity Editor Search 초기화의 `ArgumentOutOfRangeException` 1건은 Play Mode 각 시작 로그에 남는다. 프로젝트 컴파일 오류/훈련 runtime exception과 구분하며 Search 설정은 이번 범위에서 변경하지 않았다.

## 남은 확인 및 수동 작업

- 코드 반영 후 Play를 중지하고 Unity 컴파일이 끝난 다음 App을 다시 실행해야 기존 mirror callback이 해제된다. Scene/Prefab 재생성, setup 메뉴 실행, Inspector 재배치는 불필요하다.
- Quest 2의 ADB 연결은 읽기 확인했지만 수정 APK 설치나 실제 Link/HMD 전환은 실행하지 않았다. 실제 Game View 출력과 Quest 모래시계 재현 여부는 기기에서 재확인해야 한다.
- 비동기 scene load도 활성화 때 main-thread 작업이 발생한다. 이번 수정은 그 작업의 일부를 분산하고 Player의 자산 통합 예산을 낮춘 조치다. 활성화 자체의 큰 hitch가 남는 경우 기기 Profiler로 원인을 특정한 뒤 해당 초기화/자산을 추가로 나눠야 한다. [Meta 설명](https://developers.meta.com/horizon/blog/avoiding-hitches-when-loading-scenes-in-unity/)
- 기존 짧은 전환 암전은 유지한다. 장시간 검은 화면+시스템 모래시계가 완전히 해결됐다는 검증은 아직 없다.
- 삭제한 미러 소스와 shader는 직전 Git 커밋 `3534c1c`에서 복구 가능하다. 사용자 Scene/Prefab/음성 자산은 삭제하지 않았다.
- Integration 요청: 추가 서버/계정 권한 없음. 원격 push/merge는 하지 않는다.

# Link / App 공용 XR 높이 누적 수정 — 2026-09-07

2026-09-08 후속: 이 수정은 `06480dd`에 커밋하고 최신 팀 코드와 통합했다. 현재 제출 검증/배포 경로는 [제출 검증 보고서](SUBMISSION_VALIDATION_2026-09-08.md)를 참고한다. 아래 미커밋 상태는 9월 7일 당시 기록이다.

- 역할: Integration. 작업 브랜치: `integration/xr-height-stability-20260907`.
- 기준: develop `5770f855` (배포 main `19dba002`와 같은 트리). 이번 수정은 아직 commit/push하지 않았다.
- 사용자 조건: 두 사람 모두 실제로 서서 Quest Link 케이블 + Unity Play. 실제 헤드셋을 착용한 검수는 수행하지 않았다.

## 코드와 물리 재현에서 확인한 원인

1. `01_App` 공용 XROrigin은 Tracking Origin이 NotSpecified(0), CameraYOffset이 0이었다. Floor(2)를 명시한 Stage 단독 리그와 달라 Link가 Device 기준으로 시작할 가능성이 있었다. 낮은 시작 시점의 실제 Link 원인 확정은 아직 아니다.
2. `StageXRProfileController.ApplySharedLocomotionPolicy`가 GravityProvider를 끈 뒤 이동만 다시 켰다. 설치된 XRI 3.4.1 `ContinuousMoveProvider.MoveRig`는 GravityProvider 참조가 있으면 비활성 상태여도 대체 중력을 적용하지 않는다. 기존 Gravity의 mediator도 비활성 자식 mediator를 가리켰다.
3. 72Hz 입력/자세 주입 + 실제 XRI/PhysX에서 12cm 턱을 넘고 멈추면 PPE·PreEntry·LeakResponse 모두 바닥보다 **0.12000m 높은 상태가 유지**됐다.
4. 기존 GravityProvider를 켜고 mediator만 바로잡아도 해결되지 않았다. 머리부터 아래로 검사하는 SphereCast가 Common XR Origin 자신의 Default-layer CharacterController에 부딪혀 `isGrounded=true`, 실제 capsule은 `isGrounded=false`였다.
5. 자기 몸을 제외해도 실제 PPE 측정기를 손 앞에 놓으면 같은 head cast가 측정기를 바닥으로 취급했다. 측정기와 플레이어의 `Physics.IgnoreCollision`은 true였지만 sphere cast에는 적용되지 않아 0.12m 높이에서 내려오지 않았다.

## 수정 파일과 동작

- `Assets/Scripts/App/SharedCharacterGravityProvider.cs` (+ meta): 기존 공용 Gravity 노드의 중력 컴포넌트를 교체. 기존 canonical mediator/body transformer/CharacterController를 사용하며, 발밑 capsule 충돌 결과로 중력을 적용한다. 스틱이 중립일 때도 아래 방향 접촉을 유지하고 장비의 기존 IgnoreCollision을 존중한다. 카메라/HMD pose·고정 키·Stage 작성 위치를 덮어쓰지 않는다.
- `Assets/Scripts/App/StageXRProfileController.cs`: Floor 요청, Stage 이동 허용 시 중력 담당 1개만 활성화/초기화. ContinuousMove의 legacy/head-cast 중력 참조와 fallback을 끄고 중력 중복을 방지한다.
- `Assets/Scripts/App/TransitionServices.cs`: 전환 잠금 시 locomotion provider도 중지한다. 바닥 Scene을 내리는 동안 낙하하지 않으며 목적지 profile이 활성 상태를 결정한다. 기존 pause/resume은 같은 LocomotionProvider 계약을 사용한다.
- `Assets/Scenes/01_App.unity`: Common XR root의 layer만 Ignore Raycast, tracking 요청 Floor, move fallback off, 기존 Gravity 컴포넌트를 capsule 중력으로 교체하고 시작 시 disabled로 저장. Transform·CameraOffset 높이·FOV·projection은 변경하지 않았다.
- `Assets/Editor/AppIntegrationBuilder.cs`: 향후 공용 App 재생성 시에도 동일한 Floor/중력 정책을 생성한다. Stage 원본이 아닌 builder 소유 clone만 교체한다. 이번 작업에서 BuildAll/setup/Scene 재생성은 실행하지 않았다.
- `Assets/Editor/IntegrationSceneValidator.cs`, `AppPlayModeSmokeRunner.cs`: Floor 및 중력 중복/누락 검증 추가.
- `Assets/Editor/IntegrationXrHeightValidation.cs` (+ meta): 명시적으로 실행하는 실제 물리 회귀 검사. 임시 fixture와 실제 PPE 측정기/책상 Collider를 Play 상태에서만 사용하고 Scene에 저장하지 않는다. `-hclNoXR`가 있는 batch에서만 OpenXR 초기화를 메모리상으로 끈다. 일반 Unity Play/빌드의 XR 설정에는 영향을 주지 않는다.
- `Assets/Editor/Tests/SharedXrHeightContractTests.cs` (+ meta): 직렬화된 App 정책, 중력 담당, 전환 잠금/복구 검사.
- 각 Stage `docs/handoffs/*/LATEST.md`: 이 보고서와 적용/검증 제한 연결.

Stage 04/05/06 Scene, Prefab, 장비 배치, 뚜껑/집게/측정기 pose, TTS/MP3, UI, 입력 키, 텔레메트리/서버 주소는 변경하지 않았다. TimeManager는 검사 중 Edit Mode pause 호출로 재직렬화된 것을 원래 값으로 되돌리고, pause 검사는 Play Mode로 분리했다. ProjectSettings 변경은 남기지 않았다.

## 검증

- 컴파일: 최종 검증 Unity 6000.4.8f1의 프로젝트 C# 오류 0.
- Edit Mode **170/170 통과**, 실패/건너뜀 0: `outputs/xr-height-170-editmode.xml` (검증 worktree 바깥 outputs 폴더).
- App Play Mode smoke **33 PASS / RESULT PASS**: `Logs/AppPlayModeSmoke.log`. 전체 교육, 반복, 각 Stage 단독 모드의 공용 XR/이동/장비 인계/기존 Grab과 완료 흐름.
- 높이 Play Mode **90 PASS / RESULT PASS**: `Logs/XrHeightValidation/result.txt`.
  - 1.55m / 1.85m 두 자세, 3개 Stage × 턱 3회 = 18회. 12cm 턱을 실제로 오른 뒤 복귀 높이 차이 모두 **0.00000m**(출력 정밀도 기준).
  - 스틱 중립에서 숙였다 일어나기 각 3회, 실제 PPE 측정기가 손 아래에 있는 2개 경우, 실제 PPE 책상 접근 후 숙였다 일어나기 2개 경우 모두 높이 누적 없음.
  - 세 Stage에서 공중 pause 유지, resume 후 바닥 복귀, 전환 시작 즉시 중력/이동 중지, 메뉴에서 중력 비활성 확인.
- 7개 Scene의 직렬화/공용 XR 정책 검사: `Logs/AppIntegrationValidation.log`, **Failures=0**.
- 기존 뚜껑 낙하 회귀: `Logs/WasteLidLifecycle/result.txt`, **RESULT PASS**. PreEntry에서 additive 대기 300 FixedUpdate(6초) 및 Leak 인계 후 작성된 닫힌 위치와 차이 **0.00000m**. 바닥/벽 17개 Collider와 stray-position 복구도 유지.
- 저장소 무결성: `validate_unity_repository.py --skip-lfs-fsck` 통과, 3943 tracked paths / 2127 Unity GUIDs. 새 C#/meta 포함. 기존 LFS 바이너리는 변경하지 않아 이번 검사에서 fsck는 재실행하지 않았다.
- 자동 검사에 등장하는 Unity Search `ArgumentOutOfRangeException`은 기존 에디터 내부 오류로, 프로젝트 컴파일 오류와 구분한다. NullGfx에서 설치된 OpenXR 런타임이 유효하지 않은 세션으로 무한 reference-space 오류를 내는 검사 환경 문제를 만나 해당 검증 프로세스만 중지했고, 이후 검사는 `-hclNoXR`로 장치 초기화 없이 완료했다.

## 적용과 남은 실기기 확인

1. 원본에 패치를 반영한 뒤 **Play를 중지하고 `Assets/Scenes/01_App.unity`를 다시 열어** 실행한다. 이전에 열려 있던 App Scene을 저장해 새 중력 컴포넌트를 덮어쓰지 않는다. Stage Scene의 사용자 변경은 유지한다.
2. Quest를 실제로 착용하고 Link 실행 후 바닥 높이/시점 확인 → PPE 책상 접근 → 숙여 물건 줍기 → Leak 턱 왕복 → 일시정지/재개 → Scene 전환을 두 사용자에게 재검증한다.
3. `RequestedTrackingOriginMode=Floor` 검증은 실제 OpenXR가 Floor를 수락했다는 실기기 검증이 아니다. Quest 경계 바닥/Link 기준 자체가 잘못 보정됐거나 런타임이 Floor를 지원하지 않으면 별도 확인이 필요하다. 고정 1.6m 보정이나 자동 재중앙 정렬로 숨기지 않는다.
4. 이번 대상은 **01_App에서 시작하는 공용 XR**이다. 04_PPE/05_PreEntry/06_LeakResponse를 직접 여는 개발용 리그는 변경하지 않았다. 사용자가 실제로 어느 Scene에서 Play했는지 추가 확인 요청 상태다.
5. `E:\WorkSpace\HCl-Leak-Response-VR-Prototype-main`에 관련 16개 파일을 반영했고 검증본과 줄바꿈 정규화 후 내용 일치를 확인했다. 원본의 브랜치/인덱스와 다른 미커밋 변경은 건드리지 않았다. 별도 작업 브랜치에서만 이 16개 파일을 stage했으며 commit/push/main 변경은 하지 않았다.
6. 컴파일·자동 Play 결과는 동일한 수정 소스를 가진 별도 검증 worktree에서 얻은 것이다. 원본 Unity의 새 컴파일/실제 Link 착용 실행까지 확인했다는 뜻은 아니다. 원본에서 Play 중지·App 다시 열기·컴파일 완료 후 실기기 재검증이 남아 있다.

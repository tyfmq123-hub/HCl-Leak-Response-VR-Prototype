# HCl Leak Response VR Repository Rules

이 파일은 저장소 전체에 적용되는 Codex 작업 규칙이다. 하위 문서와 충돌하면 더 엄격한 규칙을 따른다.

## 1. 역할과 공식 브랜치

- 작업 시작 전에 `Integration`, `PPE`, `PreEntry`, `LeakResponse` 중 하나를 선언한다.
- 공식 흐름은 `feature/ppe|feature/preentry|feature/leakresponse → develop → main`이다.
- `main`은 최종 안정본, `develop`은 Integration 관리자가 검증하는 통합 개발본이다.
- 기존 `integration` 브랜치는 사용하지 않는다. 새 commit, push, merge, Pull Request를 만들지 않는다.
- Stage 담당자는 정확히 자기 브랜치만 사용한다: `feature/ppe`, `feature/preentry`, `feature/leakresponse`.
- Stage 담당자는 `develop`, `main`, `integration`에 직접 commit/push하지 않는다.
- `main` Pull Request의 head는 `develop`만 허용한다.
- Integration 관리자의 별도 작업 브랜치는 `integration/<작업명>`을 사용하고 base를 `develop`로 한다.

## 2. Codex 필수 시작 절차

Stage 작업자는 파일을 수정하기 전에 아래 문서를 모두 읽는다.

1. 이 `AGENTS.md`
2. `docs/integration/STAGE_EDITING_BOUNDARIES.md`
3. 자기 역할 문서
   - PPE: `docs/codex/PPE_WORKER.md`
   - PreEntry: `docs/codex/PREENTRY_WORKER.md`
   - LeakResponse: `docs/codex/LEAKRESPONSE_WORKER.md`

현재 브랜치와 `git status --short`를 먼저 확인한다. 역할·브랜치·변경 경로가 일치하지 않거나 다른 사람의 변경이 있으면 수정하지 말고 보고한다.

## 3. Integration 전용 보호 영역

Integration 작업이라고 명시되지 않은 경우 다음을 수정하지 않는다.

- `Assets/Scenes/01_App.unity`, `02_Title.unity`, `03_Start.unity`, `07_Complete.unity`
- `Assets/Scripts/App/`
- `Assets/Resources/App/`
- `Assets/Common/XR/`
- `Assets/Common/InteractionGhosts/`
- `Assets/Editor/AppIntegrationBuilder.cs`
- `Assets/Editor/AppPlayModeSmokeRunner.cs`
- `Assets/Editor/IntegrationSceneValidator.cs`
- `Assets/*/Resources/SharedXR/`
- `ProjectSettings/`, `Packages/`, `.github/`, `scripts/ci/`
- 모든 `*.asmdef`, `*.asmref`와 그 `.meta`
- 공용 XR Origin, Main Camera, Controller Tracking, EventSystem, Input Actions
- `SharedPlayerEquipment`, 공용 가방, `StageBagAnchor`, Retrieval Zone/Anchor 계약
- `SharedIncidentState`, `SharedTrainingTelemetry`, SceneFlow, Stage 완료 Adapter

공용 API나 설정이 부족하면 보호 파일을 고치지 말고 자기 인수인계 문서의 `Integration 요청`에 필요한 계약과 이유를 적는다.

## 4. Stage 소유 경계

- PPE: `Assets/1.JSR/`와 `Assets/Scenes/04_PPE.unity`
- PreEntry: `Assets/2.YJH/`와 `Assets/Scenes/05_PreEntry.unity`
- LeakResponse: `Assets/3.SSH/`와 `Assets/Scenes/06_LeakResponse.unity`
- 위 범위 안에서도 3절의 Integration 보호 영역은 수정 금지다.
- 자기 Stage가 아닌 Scene, Script, Prefab, Material, Audio, Resource를 수정하지 않는다.
- `Assets/VRHandPoseToolOutput/`에 새 Pose/스크립트 의존성을 만들지 않는다.
- 여러 Stage가 함께 참조하는 자산은 공용 자산으로 보고 Integration 관리자에게 요청한다.

## 5. 런타임 보호 원칙

- Stage에 새 XR Origin, XR Main Camera, EventSystem, InputActionManager를 추가하지 않는다.
- Camera FOV, stereo projection, projection matrix를 변경하지 않는다.
- Controller Tracking Transform이나 Ray Origin을 Visual 보정 목적으로 회전하지 않는다.
- Standard Ray와 Near/Far 포인터를 한 손에서 동시에 활성화하지 않는다.
- Stage에서 `SceneManager`를 이용한 별도 Scene 전환을 구현하지 않는다.
- Stage 완료 이벤트의 이름·형식·발생 조건과 App 완료 Adapter를 수정하지 않는다.
- 공용 가방에 Stage 이름이나 아이템 선택 로직을 하드코딩하지 않는다.
- Stage별 장비 선택과 버튼 동작은 해당 Stage handler에 둔다.
- Stage가 DB, HTTP, 파일 저장을 직접 구현하지 않는다. 텔레메트리는 공용 계약으로만 전달한다.

## 6. Git 및 인수인계 강제 규칙

- `git add .`, `git add -A`, `git add --all`을 사용하지 않는다.
- 담당 범위 파일과 자기 인수인계 파일만 경로를 명시해 stage한다.
- 보호 파일이 예상치 않게 바뀌면 restore/reset하지 말고 Integration 관리자에게 경로와 원인을 보고한다.
- 작업 종료 전에 자기 문서를 반드시 갱신한다.
  - PPE: `docs/handoffs/ppe/LATEST.md`
  - PreEntry: `docs/handoffs/preentry/LATEST.md`
  - LeakResponse: `docs/handoffs/leakresponse/LATEST.md`
- 인수인계에는 변경 파일, 동작 변화, Inspector/Scene 수동 작업, 검증 결과, 미검증 항목, 알려진 문제, Integration 요청을 빠짐없이 기록한다.
- Codex의 최종 답변에도 브랜치, commit, 변경 파일, 검증, 인수인계 문서 경로를 적는다.
- 상세 경계와 합치기 기준은 `docs/integration/STAGE_EDITING_BOUNDARIES.md`를 따른다.

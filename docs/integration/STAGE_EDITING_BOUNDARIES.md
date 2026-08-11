# HCl 누출 대응 VR Stage 작업 경계

이 문서는 PPE, 현장 진입 전(PreEntry), 누출 대응(LeakResponse) Stage를 동시에 개발할 때 소유권과 통합 계약을 보호하기 위한 강제 작업 경계다. 아래 경로는 현재 저장소의 Scene, 스크립트, 리소스 및 완료 이벤트 연결을 직접 확인해 기록했다. 경로가 겹치거나 여러 Stage에서 참조되는 에셋은 공용 영역으로 본다.

## A. 역할 구분

작업 역할은 다음 네 가지로 고정한다.

- Integration 관리자
- PPE 담당자
- PreEntry 담당자
- LeakResponse 담당자

모든 작업자는 파일을 수정하기 전에 자신의 역할을 명확히 선언해야 한다. 역할이 불명확하면 수정하지 말고 먼저 확인한다. Integration 관리자만 공용 통합 영역을 수정하고 Stage 브랜치를 `develop`에 합칠 수 있다.

## B. 모든 Stage 담당자의 공용 수정 금지 영역

PPE, PreEntry, LeakResponse 담당자는 다음 항목을 수정·이동·삭제·이름 변경·재생성할 수 없다.

- `Assets/Scenes/App/` 전체와 `Assets/Scenes/App.meta`
- `Assets/Scripts/App/` 전체와 `Assets/Scripts/App.meta`
- `Assets/Resources/App/` 전체와 `Assets/Resources/App.meta`
- App Scene: `Assets/Scenes/App/App.unity`
- Start Scene: `Assets/Scenes/App/Start.unity`
- Complete Scene: `Assets/Scenes/App/Complete.unity`
- `AppBootstrap`, `SceneFlowController`, `StageXRProfileController`
- `TransitionInputLock`, `ScreenFadeController` 및 그 밖의 전환 서비스
- `StageLifecycleAdapter`와 모든 Stage 완료 연결
- `Assets/Editor/AppIntegrationBuilder.cs`
- `Assets/Editor/AppPlayModeSmokeRunner.cs`
- `Assets/Editor/IntegrationSceneValidator.cs`
- App Scene의 `Common XR Origin`과 모든 자식
- `Main Camera`, `Camera Offset`, `Left Controller`, `Right Controller`
- `XR Ray Interactor`, `XRInteractorLineVisual`, `LineRenderer`
- 이동·회전·텔레포트 Provider와 Locomotion 구성
- `Input Action Manager`, Input Action Asset 및 연결 정보
- `EventSystem`, `XR UI Input Module`, `Tracked Device Graphic Raycaster`
- `Assets/Resources/App/StageXRProfiles/`의 PPE, PreEntry, LeakResponse XR Profile과 연결 정보
- Scene 전환 버튼, Stage ID 및 Additive Scene 전환 구성
- `ProjectSettings/` 전체(특히 `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/TagManager.asset`)
- `Packages/` 전체

현재 공용 XR 구성은 `Assets/Scenes/App/App.unity`에 있으며 `Common XR Origin` 아래에 `Camera Offset`, `Main Camera`, 좌·우 Controller와 Locomotion 구성이 있다. 공용 `EventSystem`도 App Scene이 소유한다. `Assets/Editor/AppIntegrationBuilder.cs`는 이 구성을 만들고, `StageXRProfileController`는 Stage별 XR 상태를 적용한다. 이 구조는 Integration 관리자만 변경한다.

## C. 모든 Stage 담당자의 공통 금지 행동

Stage 담당자는 자신의 Scene에 다음 항목을 새로 추가하거나, 통합 과정에서 제거·비활성화된 항목을 복구하거나 활성화할 수 없다.

- XR Origin
- Main Camera
- EventSystem
- Input Action Manager
- 별도 이동·회전·텔레포트 Provider
- 별도 Scene 전환 관리자
- 별도 `AppBootstrap`
- 별도 `SceneFlowController`

Stage 코드에서 다음 API를 직접 호출할 수 없다.

- `SceneManager.LoadScene`
- `SceneManager.LoadSceneAsync`
- `SceneManager.UnloadSceneAsync`
- `SceneManager.SetActiveScene`

Scene 로드와 전환은 Integration 영역의 `SceneFlowController`가 담당한다. 다른 Stage의 Scene·스크립트·에셋에서 문제를 발견해도 직접 수정하지 않고 파일 경로와 증상만 Integration 관리자에게 보고한다. 여러 Stage가 함께 참조하는 에셋과 PPE에서 다음 Stage로 유지되는 장비 데이터·모델은 공용 영역으로 취급하고 담당자가 단독 수정하지 않는다.

## D. PPE 담당자 허용 범위

현재 저장소에서 확인한 PPE 전용 경로는 다음과 같다.

- Scene: `Assets/Scenes/PPE_PreparationRoom.unity`
- 동작·UI·자막 로직: `Assets/Scripts/PPE/`
- UI/상호작용 Prefab: `Assets/Prefabs/PPE/`
- UI 이미지·폰트·PPE 리소스: `Assets/Resources/PPE/`
- 음성: `Assets/Audio/PPE/`
- Material/Shader: `Assets/Materials/PPE/`, `Assets/Shaders/PPE/`
- PPE 장비 모델: `Assets/PPE/PPE_Cabinet/`, `Assets/PPE/PPE_ChemicalFaceShield/`, `Assets/PPE/PPE_ChemicalGlb/`, `Assets/PPE/PPE_ChemicalSafetyBoots/`, `Assets/PPE/PPE_ChemicalSuit/`, `Assets/PPE/PPE_FaceRespirator/`, `Assets/PPE/PPE_glrove/`, `Assets/PPE/PPE_wardrobe/`

PPE 담당자는 위 경로에서 PPE 장비 교육 내용, 전용 UI, 음성·자막, 모델·Material, 상호작용 및 PPE Scene 내부 전용 콘텐츠만 수정할 수 있다. 경로 안에 있더라도 아래 통합 계약과 연동 부분은 수정할 수 없다.

- 완료 이벤트 소유 파일: `Assets/Scripts/PPE/PPESequenceManager.cs`
- 완료 계약: `PPESequenceManager.OnPPESequenceCompleted`
- 이벤트 이름·형식·발생 조건·호출 시점
- `Assets/Scripts/App/StageLifecycleAdapter.cs`의 PPE 구독 및 완료 처리
- `Assets/Resources/App/StageXRProfiles/PPE.asset`
- PPE Scene 이름과 경로
- PPE 완료 결과를 다음 Stage까지 유지하는 노란색 장갑 상태 연결
- 장비 보관·배출·회수·복구 및 영속 장비 인벤토리 연결
- 기존 손·장갑·Grab 연결
- `Assets/Scripts/PPE/ChemicalGlovesWearableVisual.cs`의 `ApplyPersistentState` 등 통합 연동 부분
- `Assets/Scripts/PPE/PPELoadoutData.cs`의 영속 상태 및 통합 연동 부분

`Assets/PPE/INT_AbsorbentPad_01/`, `Assets/PPE/INT_AbsorbentSock_Curved_01/`, `Assets/PPE/INT_AbsorbentSock_Straight_01/`, `Assets/PPE/INT_RecoveryTongs/`는 LeakResponse에서도 사용하는 장비이므로 PPE 담당자의 단독 수정 범위가 아니다.

## E. PreEntry 담당자 허용 범위

현재 저장소에서 확인한 PreEntry 전용 경로는 다음과 같다.

- Scene: `Assets/3.JH/Scenes/Prototype/JH_Test_Scene.unity`
- 시나리오·상호작용·UI/자막 로직: `Assets/3.JH/Scripts/Scenario/`
- UI와 UI Material/Font: `Assets/3.JH/UI/`
- 음성: `Assets/3.JH/Audio/TTS/`
- 모델·Material·Shader·Texture·Animation: `Assets/3.JH/Art/`
- 전용 Prefab/Data: `Assets/3.JH/Prefabs/`, `Assets/3.JH/Data/`

PreEntry 담당자는 위 경로에서 현장 진입 전 교육 내용, 전용 UI, 음성·자막, 모델·Material, 상호작용 및 PreEntry Scene 내부 전용 콘텐츠만 수정할 수 있다. 경로 안에 있더라도 아래 통합 계약은 수정할 수 없다.

- 완료 이벤트 소유 파일: `Assets/3.JH/Scripts/Scenario/EntrySafetyStateMachine.cs`
- 완료 계약: `EntrySafetyStateMachine.SectionCompleted`
- 이벤트 이름·형식·발생 조건·호출 시점
- `EntrySafetyScenarioController.StateMachine`을 통한 상태 머신 노출
- `Assets/Scripts/App/StageLifecycleAdapter.cs`의 PreEntry 구독 및 완료 처리
- `Assets/Resources/App/StageXRProfiles/PreEntry.asset`
- PreEntry 통합 초기화와 공용 장비 전달 연결
- PreEntry Scene 이름과 경로

## F. LeakResponse 담당자 허용 범위

현재 저장소에서 확인한 LeakResponse 전용 경로는 다음과 같다.

- Scene: `Assets/Scenes/SSH_VR_Project.unity`
- 누출 대응 및 내레이션 로직: `Assets/Scripts/LeakResponse/`, `Assets/Scripts/LeakResponseLogic/`
- 밸브·흡착재 설치 로직: `Assets/Scripts/ValveSystem/`, `Assets/Scripts/ObjectInstallation/`
- 음성 및 TTS 매핑: `Assets/Resources/LeakTTS/`, `Assets/Resources/P102/Unity_TTS_Map.csv`
- 누출 Particle/시각효과: `Assets/WaterLeakSystem/`, `Assets/Material/WaterLeakSystem/`, `Assets/Material/Drop/`
- 전용 UI 구현: `Assets/Scripts/LeakResponse/Narration/ValveProgressUI_Runtime.cs`와 같은 Scene 내부 런타임 UI
- 누출·회수 모델: `Assets/PPE/INT_AbsorbentPad_01/`, `Assets/PPE/INT_AbsorbentSock_Curved_01/`, `Assets/PPE/INT_AbsorbentSock_Straight_01/`, `Assets/PPE/INT_RecoveryTongs/`
- 폐기물 용기 Material: `Assets/GeneratedMaterials/BlenderFBX/Sidecar/CelShader/hazardous_waste/`, `Assets/GeneratedMaterials/BlenderFBX/Sidecar/CelShader/INT_WasteContainer_01/`

LeakResponse 담당자는 위 경로에서 누출 대응 교육 내용, 누출 현장 전용 오브젝트의 위치·크기·Material, 누출·흡착·밸브·회수 작업, 전용 UI·음성·자막·Particle·시각효과·상호작용 및 LeakResponse Scene 내부 전용 콘텐츠만 수정할 수 있다. 단, PPE와 함께 참조하는 흡착재·회수 장비 모델은 공용 에셋이므로 Integration 관리자 승인 없이 단독 수정할 수 없다. 또한 아래 통합 계약은 수정할 수 없다.

- 완료 큐 소유 파일: `Assets/Scripts/LeakResponse/Narration/P102LeakNarrationController.cs`
- 완료 큐 발생 파일: `Assets/Scripts/LeakResponse/Narration/P102WasteContainerRuntime.cs`
- 완료 계약: `P102LeakNarrationCue.ContainerClosedTaskCompleted`
- 실제 Adapter 연결: `P102LeakNarrationController.CueCompleted`를 구독하고 완료 큐를 판별하는 `Assets/Scripts/App/StageLifecycleAdapter.cs`
- 완료 큐 이름·형식·발생 조건·호출 시점
- `Assets/Resources/App/StageXRProfiles/LeakResponse.asset`
- `P102LeakRuntimeBootstrap`을 통한 통합 초기화 연결
- LeakResponse Scene 이름과 경로

## G. 담당자 Git 규칙

- 모든 Stage 브랜치는 최신 `origin/develop`에서 생성한다.
- PPE 브랜치 이름은 `stage/ppe-작업명`을 사용한다.
- PreEntry 브랜치 이름은 `stage/preentry-작업명`을 사용한다.
- LeakResponse 브랜치 이름은 `stage/leakresponse-작업명`을 사용한다.
- Stage 담당자는 `main`과 `develop`에 직접 commit하거나 push할 수 없다.
- 자기 Stage 허용 파일만 정확한 경로로 명시해 stage한다.
- `git add .`과 `git add -A`를 사용하지 않는다.
- 새 Unity 에셋은 대응하는 `.meta` 파일을 반드시 함께 포함한다.
- 다른 Scene의 자동 저장 변경은 commit하지 않는다.
- `ProjectSettings`와 `Packages`의 자동 변경은 commit하지 않는다.
- 공용 보호 파일의 변경은 commit하지 않는다.
- 프로젝트 폴더 전체를 덮어쓰는 방식으로 결과를 전달하지 않는다.
- Stage 브랜치를 `develop`에 병합하는 작업은 Integration 관리자만 수행한다.
- 담당자는 자기 Stage 브랜치에만 push한다.
- 보호 영역 변경이 필요하면 직접 수정하지 않고 Integration 관리자에게 요청한다.

## 작업 전 최종 확인

1. 자신의 역할과 Stage를 선언한다.
2. 변경 예정 파일이 허용 경로이며 공용 계약이 아닌지 확인한다.
3. 다른 Stage나 공용 에셋 문제가 보이면 경로와 증상만 기록한다.
4. commit 전 `git status --short`와 staged 파일 목록을 검토한다.
5. 새 Unity 에셋과 `.meta`가 짝을 이루는지 확인한다.

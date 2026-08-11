# HCl Leak Response VR Repository Rules

- 작업 시작 전에 역할을 Integration, PPE, PreEntry, LeakResponse 중 하나로 선언한다.
- Integration 작업이라고 명시되지 않은 경우 공용 통합 영역을 수정하지 않는다.
- Stage 담당자는 `docs/integration/STAGE_EDITING_BOUNDARIES.md`의 자기 Stage 허용 범위만 수정한다.
- 역할이 불명확하면 파일 수정 전에 질문한다.
- 다른 Stage 문제는 직접 수정하지 않고 경로와 증상만 보고한다.
- `Assets/Scenes/App`, `Assets/Scripts/App`, `Assets/Resources/App`는 Integration 관리자 전용이다.
- 공용 XR Origin, Main Camera, EventSystem, Input Actions, ProjectSettings, Packages는 Stage 담당자가 수정하지 않는다.
- Stage 완료 이벤트와 Adapter를 수정하지 않는다.
- Stage에서 `SceneManager`를 이용한 별도 Scene 전환을 구현하지 않는다.
- Git stage 전에 변경 파일 전체가 담당자 허용 범위인지 확인한다.
- 보호 파일이 변경되면 commit하지 않고 Integration 관리자에게 보고한다.
- `git add .`과 `git add -A`를 사용하지 않는다.
- 상세 경계는 `docs/integration/STAGE_EDITING_BOUNDARIES.md`를 따른다.

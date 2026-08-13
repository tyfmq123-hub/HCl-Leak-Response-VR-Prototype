# HCl Leak Response VR Repository Rules

- 작업 시작 전에 역할을 Integration, PPE, PreEntry, LeakResponse 중 하나로 선언한다.
- Integration 작업이라고 명시되지 않은 경우 공용 통합 영역을 수정하지 않는다.
- Integration 관리자는 cross-stage 호환성 작업 시 각 기능의 실제 Stage 소유 영역을 수정할 수 있지만, Stage 전용 로직을 App 공용 코드로 옮기지 않는다.
- Stage 담당자는 `docs/integration/STAGE_EDITING_BOUNDARIES.md`의 자기 Stage 허용 범위만 수정한다.
- 역할이 불명확하면 파일 수정 전에 질문한다.
- `Assets/Scenes/App`, `Assets/Scripts/App`, `Assets/Resources/App`, `Assets/Common/XR`는 Integration 관리자 전용이다.
- 공용 XR Origin, Main Camera, Controller Tracking, EventSystem, Input Actions, Shared Player Equipment, ProjectSettings, Packages는 Stage 담당자가 수정하지 않는다.
- Controller Tracking과 공용 입력 기반은 Integration 소유이고, 버튼으로 실행되는 게임 동작은 해당 Stage 소유이다.
- 공용 가방은 Transform, Retrieval Zone/Anchor, 장비 상태 계약만 제공한다. 가방에서 어떤 아이템을 꺼낼지는 Stage handler가 결정한다.
- App 공용 코드에 Stage 이름을 조건으로 버튼 동작이나 가방 내용물을 하드코딩하지 않는다.
- Stage 완료 이벤트와 완료 Adapter를 수정하지 않는다.
- Stage에서 `SceneManager`를 이용한 별도 Scene 전환을 구현하지 않는다.
- 다른 Stage 문제는 직접 수정하지 않고 경로와 증상만 보고한다. 단, Integration 역할의 통합 호환성 수정은 예외다.
- Git stage 전에 변경 파일 전체가 담당자 허용 범위인지 확인한다.
- 보호 파일이 예상치 않게 변경되면 commit하지 않고 Integration 관리자에게 보고한다.
- `git add .`과 `git add -A`를 사용하지 않는다.
- 상세 경계는 `docs/integration/STAGE_EDITING_BOUNDARIES.md`를 따른다.

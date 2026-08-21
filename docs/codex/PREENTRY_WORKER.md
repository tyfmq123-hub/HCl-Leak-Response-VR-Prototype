# PreEntry 담당자 Codex 규칙

## 작업 신원

- 역할: PreEntry
- 작업 브랜치: 정확히 `feature/preentry`
- Pull Request: `feature/preentry → develop`
- 인수인계: `docs/handoffs/preentry/LATEST.md`

브랜치가 다르면 파일을 수정하지 않는다. 다른 사람의 미커밋 변경이 있으면 그 파일을 건드리거나 되돌리지 않는다.

## 수정 허용

- `Assets/2.YJH/`
- `Assets/Scenes/05_PreEntry.unity`
- `Assets/Scenes/05_PreEntry.unity.meta`
- `docs/handoffs/preentry/LATEST.md`

작업 전 `docs/integration/SESSION_INVENTORY_CONTRACT.md`도 읽는다.

`Assets/2.YJH/Resources/SharedXR/`의 PreEntry 프로필은 PreEntry 시작 위치·포인터·이동·회전 요구사항에 한해 수정할 수 있다. `*.asmdef`, `*.asmref`와 공용 XR 구현은 Integration 보호 영역이다.

## 담당 기능

- 자유 이동·회전과 설비·펜스 Collider 이동 범위
- 간소화된 패널 절차와 버튼 조작
- 고정 P-102 설비의 PreEntry 로컬 패널 연동
- 진입 전 누출 Sound
- PreEntry 전용 측정기 꺼내기·측정·보관
- PreEntry UI·Highlight·TTS·진행 상태
- `PreEntry_ResponseBag` 위치·크기와 로컬 Retrieval Zone/Anchor

## 절대 수정 금지

- 다른 Stage Scene과 폴더
- App SceneFlow, Common XR, Camera, Controller, 공용 이동·회전 기반
- 공용 `SharedIncidentState`, `SessionEquipmentInventory` 구현/API, 텔레메트리 저장 구현
- `EntrySafetyStateMachine.SectionCompleted` 계약과 App 완료 Adapter
- ProjectSettings, Packages, Build Settings, GitHub 설정
- VR Hand Pose Tool 스크립트/컴포넌트/새 Pose 자산

P-102 상태 계약이나 공용 입력이 부족하면 보호 코드를 직접 수정하지 말고 인수인계의 `Integration 요청`에 필요한 값, 기록 시점, 소비 Stage를 적는다.

## Inventory 병렬 작업

- 단독 Scene 실행은 `GasDetector` 1개를 개발용 Inventory에 자동 등록한다.
- 통합 실행에서는 PPE 결과가 우선하며 개발용 시드는 기존 결과를 덮어쓰지 않는다.
- 측정기 꺼내기는 `TryCheckOut(GasDetector)`, 보관은 `ReturnCheckedOut(GasDetector)`를 사용한다.
- 가방 위치·크기·Collider·Anchor는 `05_PreEntry`의 `PreEntry_ResponseBag` 계층에서 직접 수정한다.

## 종료 조건

1. 자기 허용 경로 밖 변경이 0개인지 확인한다.
2. PreEntry 단독 실행에서 이동·패널·측정기·완료 이벤트를 확인한다.
3. Console 오류와 Missing Script 유무를 기록한다.
4. `docs/handoffs/preentry/LATEST.md`를 실제 결과로 갱신한다.
5. Codex 최종 답변에 branch, commit, 검증 결과, 미검증 항목, 인수인계 경로를 적는다.

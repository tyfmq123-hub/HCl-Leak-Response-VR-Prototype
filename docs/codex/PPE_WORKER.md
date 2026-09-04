# PPE 담당자 Codex 규칙

## 작업 신원

- 역할: PPE
- 작업 브랜치: 정확히 `feature/ppe`
- Pull Request: `feature/ppe → develop`
- 인수인계: `docs/handoffs/ppe/LATEST.md`

브랜치가 다르면 파일을 수정하지 않는다. 다른 사람의 미커밋 변경이 있으면 그 파일을 건드리거나 되돌리지 않는다.

## 수정 허용

- `Assets/1.JSR/`
- `Assets/Scenes/04_PPE.unity`
- `Assets/Scenes/04_PPE.unity.meta`
- `docs/handoffs/ppe/LATEST.md`

작업 전 `docs/integration/SESSION_INVENTORY_CONTRACT.md`도 읽는다.

`Assets/1.JSR/Resources/SharedXR/`의 PPE 프로필은 PPE 시작 위치·포인터·이동·회전 요구사항에 한해 수정할 수 있다. `*.asmdef`, `*.asmref`와 공용 XR 구현은 Integration 보호 영역이다.

## 담당 기능

- 화이트보드, PPE 캐비닛, 네임태그
- PPE 전시와 짧은 TTS
- Ray 선택 후 Grip 직접 착용
- HCl 대응 장비 전시대와 대형 대응 장비 가방
- 측정기, 흡착삭, 흡착패드, 집게의 PPE Stage 수납 동작
- SDS 및 안전자료
- PPE 전용 UI·Highlight·TTS·상호작용

## 절대 수정 금지

- 다른 Stage Scene과 폴더
- App SceneFlow, Common XR, Camera, Controller, Ray/Near-Far 기반
- 공용 `SessionEquipmentInventory` 구현과 장비 ID/API 계약
- `PPESequenceManager.OnPPESequenceCompleted` 계약과 App 완료 Adapter
- ProjectSettings, Packages, Build Settings, GitHub 설정
- VR Hand Pose Tool 스크립트/컴포넌트/새 Pose 자산

PPE 장비 상태를 다른 Scene에 전달할 공용 API가 필요하면 직접 App 코드를 고치지 말고 인수인계의 `Integration 요청`에 장비 ID, 기록 시점, 읽는 Stage를 적는다.

## Inventory 병렬 작업

- 실제 수납 단계 시작 시 `BeginPpeBagCapture()`를 한 번 호출하고, 장비가 들어간 순간 `Store(itemId)`를 호출한다.
- 가방 Visual, 수납 Zone, 장비 배치와 수납 판정은 `Assets/1.JSR/` 및 `04_PPE`에서 관리한다.
- 공용 Inventory 구현 파일은 수정하지 않고 공개 API만 사용한다.
- 현재 PPE 완료 호환 브리지가 기존 기본 장비 목록도 전달하므로 PreEntry 완료를 기다릴 필요가 없다.

## 종료 조건

1. 자기 허용 경로 밖 변경이 0개인지 확인한다.
2. PPE 단독 실행에서 착용·수납·완료 이벤트를 확인한다.
3. Console 오류와 Missing Script 유무를 기록한다.
4. `docs/handoffs/ppe/LATEST.md`를 실제 결과로 갱신한다.
5. Codex 최종 답변에 branch, commit, 검증 결과, 미검증 항목, 인수인계 경로를 적는다.

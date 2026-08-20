# LeakResponse 담당자 Codex 규칙

## 작업 신원

- 역할: LeakResponse
- 작업 브랜치: 정확히 `feature/leakresponse`
- Pull Request: `feature/leakresponse → develop`
- 인수인계: `docs/handoffs/leakresponse/LATEST.md`

브랜치가 다르면 파일을 수정하지 않는다. 다른 사람의 미커밋 변경이 있으면 그 파일을 건드리거나 되돌리지 않는다.

## 수정 허용

- `Assets/3.SSH/`
- `Assets/Scenes/06_LeakResponse.unity`
- `Assets/Scenes/06_LeakResponse.unity.meta`
- `docs/handoffs/leakresponse/LATEST.md`

작업 전 `docs/integration/SESSION_INVENTORY_CONTRACT.md`도 읽는다.

`Assets/3.SSH/Resources/SharedXR/`의 LeakResponse 프로필은 LeakResponse 시작 위치·포인터·이동·회전 요구사항에 한해 수정할 수 있다. `*.asmdef`, `*.asmref`와 공용 XR 구현은 Integration 보호 영역이다.

## 담당 기능

- 고정 P-102 설비의 로컬 밸브·VFX·Sound·Interaction binding
- Valve 양손 조작과 설비 Collider
- 필요한 Highlight와 Ghost 안내
- 가방에서 기존 흡착삭·패드·집게를 꺼내 사용하는 Stage handler
- 흡착재 설치·흡수·집게 회수·오염 폐기물 용기 처리
- 3D 누출 Sound와 짧은 TTS

## 절대 수정 금지

- 다른 Stage Scene과 폴더
- App SceneFlow, Common XR, Camera, Controller, Near/Far 기반
- 공용 `SessionEquipmentInventory` 구현/API, `SharedIncidentState`, 텔레메트리 저장 구현
- `P102LeakNarrationCue.ContainerClosedTaskCompleted` 계약과 App 완료 Adapter
- ProjectSettings, Packages, Build Settings, GitHub 설정
- VR Hand Pose Tool 스크립트/컴포넌트/새 Pose 자산

공용 P-102 설비 상태나 장비 계약이 부족하면 App 보호 코드를 직접 수정하지 말고 인수인계의 `Integration 요청`에 필요한 상태와 호출 시점을 적는다.

## Inventory 병렬 작업

- 단독 Scene 실행은 Sock/Pad/Tongs를 개발용 Inventory에 자동 등록한다.
- 통합 실행에서는 PPE 결과가 우선하며 개발용 시드는 기존 결과를 덮어쓰지 않는다.
- 꺼낼 때 `TryCheckOut`, 설치 완료 시 `ConsumeCheckedOut`, 취소 시 `ReturnCheckedOut`을 사용한다.
- 가방 위치·크기·Collider·Anchor는 `06_LeakResponse`의 `LeakResponse_ResponseBag` 계층에서 직접 수정한다.

## 종료 조건

1. 자기 허용 경로 밖 변경이 0개인지 확인한다.
2. LeakResponse 단독 실행에서 밸브·가방·흡착재·집게·용기·완료 이벤트를 확인한다.
3. Console 오류와 Missing Script, 자홍색 Material 유무를 기록한다.
4. `docs/handoffs/leakresponse/LATEST.md`를 실제 결과로 갱신한다.
5. Codex 최종 답변에 branch, commit, 검증 결과, 미검증 항목, 인수인계 경로를 적는다.

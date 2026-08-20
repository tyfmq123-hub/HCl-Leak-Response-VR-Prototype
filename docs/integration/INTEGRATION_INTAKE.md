# Integration 관리자 인수 절차

Stage Pull Request를 합치기 전에 변경 코드보다 먼저 해당 인수인계 문서를 읽는다.

- PPE: `docs/handoffs/ppe/LATEST.md`
- PreEntry: `docs/handoffs/preentry/LATEST.md`
- LeakResponse: `docs/handoffs/leakresponse/LATEST.md`

## 인수 순서

1. PR base가 `develop`, head가 담당자의 정확한 feature 브랜치인지 확인한다.
2. GitHub `Branch and stage scope guard`가 통과했는지 확인한다.
3. 인수인계의 기준 develop commit과 현재 develop 차이를 확인한다.
4. 변경 파일이 Stage 소유 범위와 일치하는지 확인한다.
5. 완료 이벤트, 공용 입력, 가방, 사고 상태, 텔레메트리 계약 변경 요청을 검토한다.
6. Scene/Prefab 수동 변경과 Unity 재직렬화 범위를 확인한다.
7. Stage 단독 검증 후 App 통합 검증을 수행한다.
8. 문제가 있으면 Stage 파일을 임의로 다시 설계하기 전에 담당자에게 재현 조건을 전달한다.

## 통합 검증 최소 기준

- Unity compile error 0
- 8개 Build Scene Missing Script 0, Broken Reference 0
- 활성 XR Origin/Main Camera/EventSystem/InputActionManager 각 1개
- 공용 가방과 SharedPlayerEquipment 각 1개
- Stage 전환 후 장비·설비 상태 유지
- PPE → PreEntry → LeakResponse → Complete 전체 PlayMode Smoke 통과
- 해당 Stage Editor 테스트 통과
- Quest 실기기에서 손·Ray·Grab·렌더링 확인 여부 기록

## 병합 금지 조건

- 인수인계 문서가 초기 템플릿 그대로임
- 보호 파일 변경이 포함됨
- 다른 Stage Scene이 함께 변경됨
- Missing Script, 자홍색 Material, 중복 XR/Camera/Ray가 발견됨
- 테스트 실패를 원인·재현 조건 없이 `무시 가능`으로 적음
- 공용 변경 요청을 Stage 코드의 하드코딩으로 우회함

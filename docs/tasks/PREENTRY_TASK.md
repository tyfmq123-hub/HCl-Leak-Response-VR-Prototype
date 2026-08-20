# PreEntry 담당자 작업지시서

이 문서에 적힌 항목만 PreEntry 작업 범위로 사용한다. 임의로 기능을 추가하거나 Integration 공용 영역을 수정하지 않는다.

## Codex에 처음 전달할 내용

```text
나는 PreEntry 담당자다. AGENTS.md, docs/integration/STAGE_EDITING_BOUNDARIES.md, docs/codex/PREENTRY_WORKER.md, docs/tasks/PREENTRY_TASK.md를 끝까지 읽고 작업해. 브랜치는 feature/preentry만 사용하고 허용 경로 밖은 수정하지 마. 아래 확정 작업만 구현하고, 끝나면 docs/handoffs/preentry/LATEST.md를 실제 변경·검증 결과로 작성해.
```

## 확정 작업

### 이동 범위

- 자유 로코모션과 회전을 유지한다.
- 설비, 펜스, Collider를 이용해 사용자가 허용 구역 밖으로 이동하지 못하게 한다.
- 새 XR Origin, Camera, 이동 Provider를 추가하지 않고 기존 공용 이동 기반을 사용한다.

### 패널 절차

- 불필요하게 긴 패널 절차를 간소화한다.
- VR에서 패널 버튼을 안정적으로 누를 수 있게 조작을 개선한다.
- 교육 시나리오의 고정 누출 설비인 P-102와 해당 패널 행만 절차에 연동한다.
- `SharedIncidentState`에는 P-102 운전/정지 상태만 기록한다. 교육생 설비 선택 기능은 만들지 않는다.
- Scene 안의 버튼, 설비, Audio, Collider 참조는 PreEntry 로컬 binding으로 유지한다.

### 대응 장비 가방과 Sound

- 기존 공용 대응 장비 가방이 PreEntry의 지정 위치에 보이게 한다.
- Stage 전용 가방 Visual을 복제하지 않는다.
- `StageBagAnchor` 또는 공용 가방 계약 변경이 필요하면 직접 App 코드를 고치지 말고 원하는 위치와 이유를 인수인계에 적는다.
- 누출 Sound는 누출 구역 진입 전부터 재생되게 한다.

### 대시보드 준비 이벤트

- 대시보드 화면이나 DB를 만들지 않는다.
- 기존 `SharedTrainingTelemetry` 계약만 사용해 다음 의미 있는 이벤트를 전달한다.
  - PreEntry Stage 진입·완료
  - 패널 절차 시작·완료와 반복 실패
  - P-102 정지 처리 결과
  - 측정기 또는 가방 기능 사용
- 매 프레임 위치·회전 값이나 불필요한 세부 로그는 남기지 않는다.

## 보류 항목

- 윈드삭은 이번 작업에서 구현하지 않는다.

## 이번 작업에 포함하지 않는 것

- App Scene 전환, Common XR, Camera, Controller, 공용 이동 기반 수정
- 다른 Stage Scene 수정
- 공용 `SharedIncidentState`, 가방, Inventory, Telemetry 저장 구현 수정
- VR Hand Pose Tool 재추가
- 교육생 점수, 숙련도, 능력 평가 기능
- 윈드삭 구현

## 완료 확인

1. PreEntry에서 자유 이동·회전이 가능하고 Collider 경계를 통과하지 않는다.
2. 간소화된 패널 버튼을 VR에서 안정적으로 조작할 수 있다.
3. P-102 설비와 P-102 패널 행 상태가 올바르게 연결된다.
4. 공용 가방이 지정 위치에 있고 중복 가방이 없다.
5. 진입 전부터 누출 Sound가 재생된다.
6. Console compile error와 Missing Script가 없다.
7. 자기 허용 범위 밖 변경 파일이 없다.
8. `docs/handoffs/preentry/LATEST.md`에 변경 파일, Inspector 작업, 검증, 미검증 항목, Integration 요청을 기록한다.

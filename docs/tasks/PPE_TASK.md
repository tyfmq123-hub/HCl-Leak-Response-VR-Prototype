# PPE 담당자 작업지시서

이 문서에 적힌 항목만 PPE 작업 범위로 사용한다. 임의로 기능을 추가하거나 Integration 공용 영역을 수정하지 않는다.

## Codex에 처음 전달할 내용

```text
나는 PPE 담당자다. AGENTS.md, docs/integration/STAGE_EDITING_BOUNDARIES.md, docs/codex/PPE_WORKER.md, docs/tasks/PPE_TASK.md를 끝까지 읽고 작업해. 브랜치는 feature/ppe만 사용하고 허용 경로 밖은 수정하지 마. 아래 확정 작업만 구현하고, 끝나면 docs/handoffs/ppe/LATEST.md를 실제 변경·검증 결과로 작성해.
```

## 확정 작업

### 공간과 안내

- 화이트보드를 PPE 교육 흐름에 맞게 구성한다.
- PPE 캐비닛을 배치하고 사용할 PPE가 구분되게 한다.
- PPE 네임태그를 제공한다.
- 여러 PPE를 전시하고, 필요한 대상에만 짧은 TTS 설명을 제공한다.
- HCl 대응 장비 전시대와 대응 장비 네임태그를 구성한다.
- SDS 및 안전자료를 확인할 수 있게 한다.
- 상시 자막은 제거하고 필요한 TTS 중심으로 구성한다.

### PPE 착용

- Ray는 착용할 PPE를 선택하거나 가리키는 용도로 사용한다.
- 실제 착용은 해당 PPE를 Grip으로 직접 잡는 방식으로 처리한다.
- 기존 PPE 완료 순서와 `PPESequenceManager.OnPPESequenceCompleted` 계약은 변경하지 않는다.

### 대응 장비 가방과 수납

- PPE 공간에 대형 대응 장비 가방이 보이고 사용할 수 있게 한다.
- 측정기, 흡착삭, 흡착패드, 집게를 Grip으로 잡아 가방에 수납할 수 있게 한다.
- 수납 성공 시 기존 공용 Inventory 계약에 상태를 전달한다.
- App 공용 가방을 사용하며 Stage 전용 가방 Visual을 새로 복제하지 않는다.
- 공용 가방 또는 Inventory API가 부족하면 App 코드를 직접 고치지 말고 인수인계의 `Integration 요청`에 적는다.

### 대시보드 준비 이벤트

- 대시보드 화면이나 DB를 만들지 않는다.
- 기존 `SharedTrainingTelemetry` 계약만 사용해 다음 의미 있는 이벤트를 전달한다.
  - PPE Stage 진입·완료
  - PPE 또는 대응 장비 설명 확인
  - SDS 확인
  - 측정기·흡착삭·흡착패드·집게 수납 성공 또는 반복 실패
- 매 프레임 위치·회전 값이나 불필요한 세부 로그는 남기지 않는다.

## 이번 작업에 포함하지 않는 것

- App Scene 전환, Common XR, Camera, Controller, Ray 기반 수정
- 다른 Stage Scene 수정
- 공용 가방·Inventory·Telemetry 저장 구현 수정
- VR Hand Pose Tool 재추가
- 교육생 점수, 숙련도, 능력 평가 기능
- 상시 자막 재추가

## 완료 확인

1. PPE 단독 Scene에서 Ray 선택 후 Grip 착용이 동작한다.
2. 네 종류 대응 장비를 Grip으로 가방에 수납할 수 있다.
3. PPE 설명, 대응 장비 설명, SDS와 필요한 TTS가 동작한다.
4. 상시 자막이 남아 있지 않다.
5. Console compile error와 Missing Script가 없다.
6. 자기 허용 범위 밖 변경 파일이 없다.
7. `docs/handoffs/ppe/LATEST.md`에 변경 파일, Inspector 작업, 검증, 미검증 항목, Integration 요청을 기록한다.

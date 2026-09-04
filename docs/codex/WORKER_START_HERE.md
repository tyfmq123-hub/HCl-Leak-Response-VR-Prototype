# Stage 작업자 Codex 시작 안내

각 담당자는 자기 작업을 시작할 때 아래 한 줄을 Codex에 그대로 전달한다.

## PPE

```text
나는 PPE 담당자다. 파일을 수정하기 전에 AGENTS.md, docs/integration/STAGE_EDITING_BOUNDARIES.md, docs/codex/PPE_WORKER.md를 끝까지 읽고 현재 브랜치와 변경 상태를 확인해. feature/ppe와 허용 경로 밖은 수정하지 말고, 작업 종료 전에 docs/handoffs/ppe/LATEST.md를 실제 변경 내용으로 갱신해. Integration 영역 변경이 필요하면 직접 고치지 말고 인수인계의 Integration 요청에 적어.
```

## PreEntry

```text
나는 PreEntry 담당자다. 파일을 수정하기 전에 AGENTS.md, docs/integration/STAGE_EDITING_BOUNDARIES.md, docs/codex/PREENTRY_WORKER.md를 끝까지 읽고 현재 브랜치와 변경 상태를 확인해. feature/preentry와 허용 경로 밖은 수정하지 말고, 작업 종료 전에 docs/handoffs/preentry/LATEST.md를 실제 변경 내용으로 갱신해. Integration 영역 변경이 필요하면 직접 고치지 말고 인수인계의 Integration 요청에 적어.
```

## LeakResponse

```text
나는 LeakResponse 담당자다. 파일을 수정하기 전에 AGENTS.md, docs/integration/STAGE_EDITING_BOUNDARIES.md, docs/codex/LEAKRESPONSE_WORKER.md를 끝까지 읽고 현재 브랜치와 변경 상태를 확인해. feature/leakresponse와 허용 경로 밖은 수정하지 말고, 작업 종료 전에 docs/handoffs/leakresponse/LATEST.md를 실제 변경 내용으로 갱신해. Integration 영역 변경이 필요하면 직접 고치지 말고 인수인계의 Integration 요청에 적어.
```

Codex가 문서를 읽기 전에 수정을 시작하거나 다른 브랜치로 올리겠다고 하면 중단시키고 위 문장을 다시 전달한다.

## 공통 Git 흐름

1. Integration 관리자가 검증한 최신 `develop`을 기준으로 자기 feature 브랜치를 갱신한다.
2. 자기 브랜치에서 자기 Stage만 수정한다.
3. 변경 파일을 경로별로 확인하고 자기 파일만 명시적으로 stage한다.
4. 자기 `docs/handoffs/<stage>/LATEST.md`를 갱신한다.
5. 자기 feature 브랜치에만 push한다.
6. Pull Request base는 `develop`, compare는 자기 feature 브랜치로 지정한다.

`main`, `develop`, 기존 `integration`에는 직접 push하지 않는다.

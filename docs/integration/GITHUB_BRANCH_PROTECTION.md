# GitHub 브랜치 보호 설정

저장소 파일의 규칙만으로 direct push를 막을 수 없으므로 GitHub 서버에서 `main`과 `develop`을 보호해야 한다.

## 적용 대상

### main

- Pull Request 필수
- head branch는 `develop`만 허용
- `Branch and stage scope guard` 통과 필수
- Code Owner 승인 1명 필수
- 마지막 push 이후 승인 필수
- stale review 자동 해제
- 대화 해결 필수
- linear history 필수
- 관리자 포함 direct push, force push, 삭제 금지

### develop

- Pull Request 필수
- Stage head는 `feature/ppe`, `feature/preentry`, `feature/leakresponse`만 허용
- Integration head는 `integration/<작업명>`만 허용
- 기존 `integration` head 금지
- `Branch and stage scope guard` 통과 필수
- Code Owner 승인 1명 필수
- 마지막 push 이후 승인 필수
- stale review 자동 해제
- 대화 해결 필수
- linear history 필수
- 관리자 포함 direct push, force push, 삭제 금지

## 이중 보호

- GitHub branch protection은 direct push와 무승인 merge를 막는다.
- `.github/workflows/branch-scope-guard.yml`은 PR의 head/base와 Stage별 변경 경로를 검사한다.
- `.github/CODEOWNERS`는 Integration 소유자의 검토를 요구한다.
- `AGENTS.md`와 `docs/codex/*.md`는 Codex가 수정 전에 지켜야 할 로컬 규칙이다.

GitHub 요금제나 저장소 권한 때문에 보호 API 적용이 거부되면 저장소 Settings → Branches 또는 Rules에서 같은 항목을 수동 적용하고, 적용 전까지 Stage 담당자에게 merge 권한을 주지 않는다.

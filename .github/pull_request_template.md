## 작업 역할과 브랜치

- [ ] PPE (`feature/ppe → develop`)
- [ ] PreEntry (`feature/preentry → develop`)
- [ ] LeakResponse (`feature/leakresponse → develop`)
- [ ] Integration (`integration/<작업명> → develop`)
- [ ] Release (`develop → main`)

## 변경 내용

- 목적:
- 주요 동작 변화:
- Unity Scene/Prefab/Inspector 수동 변경:

## 인수인계

- [ ] 담당 Stage의 `docs/handoffs/<stage>/LATEST.md`를 갱신했다.
- 인수인계 문서 경로:
- Integration 공용 계약 요청:

## 검증

- [ ] 담당 범위 밖 파일이 없다.
- [ ] Console compile error가 없다.
- [ ] Missing Script를 확인했다.
- [ ] 담당 Stage 단독 동작을 확인했다.
- [ ] 통합 검증이 필요한 항목을 인수인계에 적었다.

검증 명령/결과:

## 금지사항 확인

- [ ] `main`, `develop`, 기존 `integration`에 직접 push하지 않았다.
- [ ] 다른 Stage Scene이나 Integration 보호 파일을 수정하지 않았다.
- [ ] `git add .`, `git add -A`, `git add --all`을 사용하지 않았다.

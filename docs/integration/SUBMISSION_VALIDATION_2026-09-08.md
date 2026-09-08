# 제출 통합 검증 — 2026-09-08

역할: Integration. 브랜치: `integration/xr-height-stability-20260907`.

## 통합 기준과 보존

- 지연되어 있던 높이/흡착재/물방울 수정 25개 파일을 `06480dd`에 먼저 보존했다.
- 확인한 최신 main: `c9e240a9c3040ce4ccbc7aaaef9e646505532668`.
- 확인한 최신 develop: `db1a980f793a0d2c4924102e8c51cf086a8f1d95`.
- 팀원 PreEntry 게이지 수정 `6400e4d`와 PPE 속장갑 손 색상 수정 `db1a980`를 병합했다. 두 Stage 인수인계 문서의 추가 내용 충돌만 양쪽을 모두 남겨 해결했다. 코드 충돌은 없었다.
- 통합 이력: `d322cae` (최신 develop), `677e00d` (이미 배포된 main 이력).
- 팀원 PPE/PreEntry 런타임 코드는 develop와 동일하다. `04_PPE`, `05_PreEntry`, `06_LeakResponse` Scene, Prefab, Audio, Material, Packages, ProjectSettings는 이번 통합에서 변경하지 않았다.
- 원본 E 드라이브 checkout의 브랜치, 인덱스, 팀원/사용자 미커밋 변경은 수정하지 않았다. 별도 작업폴더에서 통합/검증한다. force-push/reset/Scene 재생성은 하지 않는다.
- 배포 경로는 `integration/xr-height-stability-20260907 → develop → main` PR이다. 최종 원격 반영 여부와 해시는 해당 PR 및 완료 보고를 기준으로 확인한다. 아래 9월 7일 보고서의 “commit/push 미실행”은 당시 기록이다.

## 변경 파일

높이 수정 상세 파일과 원인은 [SHARED_XR_HEIGHT_2026-09-07.md](SHARED_XR_HEIGHT_2026-09-07.md), 흡착재/물방울 수정은 [LEAK_SUPPLIES_DROPS_2026-09-07.md](LEAK_SUPPLIES_DROPS_2026-09-07.md)를 참고한다.

- 런타임: `SharedCharacterGravityProvider` 신규, `StageXRProfileController`, `TransitionServices`, `01_App.unity`, `HClAbsorptionVisualController`, `P102SharedBagItemHandler`, `P102AbsorbentPlayerCollisionGuard` 신규.
- 검증/재생성 일관성: `AppIntegrationBuilder`, `AppPlayModeSmokeRunner`, `IntegrationSceneValidator`, 신규 `IntegrationXrHeightValidation`, `IntegrationLeakSuppliesValidation`, `SharedXrHeightContractTests`, `LeakSuppliesRegressionTests`. 신규 스크립트의 meta 포함.
- 추가 통합 수정: `Assets/1.JSR/Editor/PPEInnerGloveHandTintTests.cs`의 RGBA 비교에 채널별 `0.00001` 허용오차를 적용했다. 최초 175개 중 이 테스트만 실패했으며 표시되는 RGB는 같았지만 부동소수점 비트가 달랐다. 런타임 색상/착용 동작은 수정하지 않았다. 테스트 삭제/건너뛰기/성공 강제 처리를 하지 않았다.
- 문서: 세 Stage `docs/handoffs/*/LATEST.md`, 두 상세 원인 보고서, 이 제출 검증 보고서.

## 최신 팀 코드와 합친 후 다시 실행한 검증

Unity `6000.4.8f1`, Windows D3D11, 별도 Integration 작업폴더. 다음 결과는 9월 7일 결과를 재사용한 것이 아니라 최신 통합 소스에서 다시 얻었다.

| 검사 | 결과 | 증거 파일 |
| --- | --- | --- |
| C# 컴파일 / Edit Mode | 175/175 PASS, 실패·건너뜀 0 | `outputs/submission-20260908-editmode-final.xml`, `.log` |
| 실제 XRI/PhysX 높이 회귀 | 90 PASS | `outputs/submission-20260908-height-result.txt` |
| 실제 App Leak 단독 흡착재/물방울 | 26 PASS | `outputs/submission-20260908-leak-solo-result.txt` |
| 실제 App Full → PreEntry → Leak 인계 | 28 PASS | `outputs/submission-20260908-leak-full-result.txt` |
| App 전체/단독 시작·완료 흐름 | 33 PASS | `outputs/submission-20260908-app-smoke-result.txt` |
| Scene/직렬화/Build Settings | 7 Scenes, Failures=0, MissingScripts/BrokenReferences=0 | `Logs/AppIntegrationValidation.log` |
| 뚜껑 additive 대기/전환 보존 | 8 PASS, 301 FixedUpdate 대기 후 위치 오차 0 | `outputs/submission-20260908-preservation-result.txt` |
| 뚜껑 XRI 재시도·집게 잠금·카운트 방향 | 35 PASS | `outputs/submission-20260908-vr-interactions-result.txt` |
| Git/Unity GUID/LFS | 전체 fsck 포함 PASS, 통합 직후 3954 경로/2132 GUID | `scripts/ci/validate_unity_repository.py --require-clean` |
| PR 소유 범위 | Integration → develop PASS | `scripts/ci/validate_branch_scope.py` |

`outputs/`는 작업폴더와 같은 부모의 로컬 검증 산출물 폴더이며 Git 대상이 아니다. `Logs/`도 실행 산출물이며 저장소에 넣지 않는다. 코드/Scene/meta/테스트 코드는 Git에 포함한다. GitHub의 새 checkout 검사에서는 LFS를 실제 내려받고 무결성 검사를 별도로 수행한다. GitHub 검사는 Unity 컴파일/Play 검사의 대체가 아니다.

Play 검증에서 실제 프로젝트 C# 컴파일 오류와 NullReference/MissingReference/InvalidOperation/ArgumentNull 예외, 검사 실패가 관측되지 않았다. Unity 라이선스 서비스 초기 연결 재시도 로그와 종료 시 메모리 통계는 이 결과와 구분한다.

검사 실행은 `-batchmode -force-d3d11 -hclNoXR`를 사용했다. 높이 검사/장비 검사도 실제 렌더링을 사용하는 동일 설정으로 재실행했다. `IntegrationVrPlayValidation.Run`은 종료 코드 0/35 PASS였으나 실행 래퍼가 결과 파일을 `Logs/VrPlayValidation/result.txt`로 잘못 찾았다. 실제 새 결과 `Logs/VRRegression/runtime-validation.txt`와 생성 시각, 종료 코드, 전체 PASS 내용을 확인하고 보관했다. 프로젝트 동작 오류로 숨기거나 테스트를 다시 성공 처리한 것이 아니다.

## 적용 및 남아 있는 확인

1. 팀원 PC에서는 작업 변경을 먼저 커밋/보존한 뒤 최신 main을 정상 pull한다. 기존 폴더에 무조건 덮어쓰기하거나 reset하지 않는다. 새 clone은 Git LFS 포함으로 받는다.
2. Unity Play를 종료하고 `Assets/Scenes/01_App.unity`를 다시 열어 컴파일 완료 후 실행한다. 예전 Scene 메모리를 저장하여 새 App 정책을 덮어쓰지 않는다. setup/BuildAll을 실행할 필요가 없다.
3. 실제 Quest Link 착용, 런타임의 Floor 수락/경계 바닥 보정, 다른 PC의 GPU/드라이버 차이는 자동 검사로 확정하지 못한다. 제출 전 Link에서 책상 접근·숙이기·턱 왕복·패드 꺼내기·씬 전환을 확인해야 한다.
4. 이번 높이 수정은 `01_App` 공용 XR 대상이다. 개발용 `04/05/06` Scene의 단독 XR 리그는 변경하지 않았다.
5. 팀원 손 색상·풍향계 게이지 기능은 보존했고 통합 컴파일/자동 테스트를 수행했지만, 팀원의 실제 착용/시선 조작을 대신한 것은 아니다.

추가 Integration 요청: 실제 Link 최종 시연 확인. 서버 설정/수집 키/피드백 계약은 변경하지 않았다.

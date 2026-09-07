# Integration — PreEntry 대기 중 쓰레기통 뚜껑 낙하

## 실제 원인과 재현

- `06_LeakResponse.unity`의 뚜껑 Rigidbody `fileID: 1723608974`가 `Use Gravity=1`, `Is Kinematic=0`으로 저장되어 있었다. 뚜껑 transform의 사용자 작성 위치는 정상이다.
- App이 PreEntry의 환경으로 LeakResponse를 additive 로드하면 `P102LeakRuntimeBootstrap.Start()`는 Stage 활성화를 기다린다. 따라서 `P102WasteContainerRuntime.Configure()`의 중력 해제·닫힌 위치 저장은 아직 실행되지 않는다.
- 이 대기 중 뚜껑에 실제 중력이 적용된다. 뒤늦은 Configure는 현재 위치를 닫힘 위치로 저장하므로, 이미 떨어진 뚜껑을 바닥 위치에 고정하게 된다.
- 이전의 LeakResponse 즉시 시작/XRI 검사만으로는 이 대기 구간을 검증하지 못했다.
- 수정 전 App→PPE→PreEntry 실제 로딩 후 재현: 작성 위치 `(-3.88850, 1.02940, 4.02199)`, 실제 위치 `(-3.88850, 0.23965, 4.02199)`, **0.78975m 낙하**. 6초/FixedUpdate 300회 대기 후에도 바닥에 남아 새 회귀 검사가 실패했다.

## 변경 파일·동작

- `Assets/Scenes/06_LeakResponse.unity`: 뚜껑 Rigidbody의 `m_UseGravity: 1→0`, `m_IsKinematic: 0→1` **두 필드만 변경**. 뚜껑/용기/보관 ghost의 위치·회전·크기는 변경하지 않았다.
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102WasteContainerPhysics.cs`(+meta): 씬 로드 직후 뚜껑의 중력과 동적 물리를 해제하는 방어 처리. Legacy Scene 설정이 다시 들어오더라도 Stage 활성화 전 위치를 보존한다. 위치를 계산하거나 덮어쓰지 않는다.
- 같은 helper가 용기 몸체에 **둘레 벽 16개 + 바닥 1개**의 solid BoxCollider를 생성한다. 메시 bounds를 용기 좌표계로 변환해 맞추며, 전체 높이를 채우는 박스나 입구를 막는 천장은 만들지 않는다. 기존 작성 solid collider가 있는 몸체에는 중복 생성하지 않는다.
- 생성 위치: 실행 중 `INT_WasteContainer_01/P102_WasteBodyCollision_Runtime`. 이 충돌벽은 훈련 활성화 전에도 준비된다. 사용자 작성 ghost/mesh/material은 변경하지 않는다.
- `P102LeakRuntimeBootstrap.cs`: Awake에서 위 물리 준비 호출. 기존 TTS/Stage 활성화 시점은 변경하지 않는다.
- `P102WasteContainerRuntime.cs`: Configure에서도 몸체 충돌을 중복 없이 보장. 기존 손으로 들기/옆 ghost 보관/마지막 닫힘 계약은 유지한다.
- 검사: `Assets/3.SSH/Editor/P102WasteContainerPhysicsTests.cs`(+meta), `Assets/Editor/IntegrationWasteLidLifecycleValidation.cs`(+meta).

## 검증

- Unity **6000.4.8f1**, 격리된 `integration/vr-regression-20260907` 작업 폴더에서 검사.
- 새 대기 회귀: **수정 전 FAIL → 수정 후 PASS**. PreEntry 6초/물리 300회 후 위치 오차 **0.00000m**, 이미 로드된 LeakResponse로 handoff한 뒤에도 **0.00000m**.
- 16개 둘레 벽과 바닥이 dormant 상태에서 생성된 것 확인. 회전/스케일 3조건의 충돌 ray 검사에서 모든 벽·테두리가 충돌하고 중앙 입구는 막히지 않는다. 재호출 시 중복 생성 없음.
- Edit Mode **167/167 Passed**, failed/skipped **0**. 결과는 작업 폴더의 `outputs/lid-physics-editmode.xml`.
- App smoke **33개 검사 PASS**: 전체 과정/반복/처음으로/세 Stage 단독, 공용 XR/카메라/가방 유지, 측정기 표시·잡기 등 포함.
- 기존 XRI 회귀 **PASS**: 뚜껑 작성 pose, 놓친 뒤/선택 취소 복원, 집게 Grip/Trigger 20회 유지, 마지막 ghost에서만 반납 및 입력 복구, 물방울/카운트 방향 유지.
- 최종 Edit Mode·대기 회귀·XRI·App smoke의 프로젝트 C# 컴파일 오류 **0개**. 검사용 새 Editor 코드 작성 중 발생한 인수 오류는 수정했고 최종 검사에서는 재발하지 않았다.
- Unity Editor Search 초기화 내부의 기존 `ArgumentOutOfRangeException`은 Play Mode 시작 로그에 남는다. 이 문제와 프로젝트 컴파일/뚜껑 물리 문제를 혼동하지 않는다.
- 렌더 캡처: 검증 폴더 `Logs/WasteLidLifecycle/baseline-fallen.png`, `01-after-preentry-wait.png`, `02-after-leak-handoff.png`. 실제 씬 상태를 검증용 카메라로 촬영했으며 Quest 캡처는 아니다.

## 보존·수동 작업·미검증

- 원본 반영 후 변경 대상 11개 경로의 내용이 검증본과 일치함을 확인했다. Scene 전체 비교에서도 뚜껑 Rigidbody 두 설정 이외의 차이는 없다.
- PPE/PreEntry Scene, Prefab, TTS/음성, 피드백/서버/telemetry, 풍향계, 카메라, 집게 동작은 수정하지 않았다.
- 원본 대상 `E:\WorkSpace\HCl-Leak-Response-VR-Prototype-main`의 기존 수정 대상 파일을 비교한 후 제한된 패치만 반영한다. 사용자 원본의 다른 미커밋 변경은 보존한다.
- Inspector 위치 조정이나 setup 메뉴 재실행은 필요 없다. Play를 중지하고 컴파일 완료 후 App을 다시 실행해야 시작 전 물리 설정이 적용된다. 현재 Play 세션에서 이미 떨어진 상태를 편집 모드 Scene에 저장하지 않는다.
- 위 Play Mode 검사는 실제 물리 대기/XRI를 사용하지만 Stage 완료 cue는 자동 주입했다. Quest를 착용한 전체 수거 절차와 손/집게를 용기 경계로 밀어 넣는 동작은 미검증이다.
- Collider만으로 손에 붙은 kinematic 뚜껑의 모든 직접 위치 이동을 차단한다고 주장하지 않는다. 이번 낙하 원인은 시작 전 중력이고, 손에서 놓친 뒤 복원은 기존 pose 제어가 담당한다.
- 추가 Integration/서버 권한 요청 없음. 원격 push/merge는 하지 않는다.

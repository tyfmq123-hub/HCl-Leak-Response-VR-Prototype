# HCl 누출 초동대응 VR 시뮬레이터

Unity 기반 염산(HCl) 누출 초동대응 VR 팀 프로젝트입니다.

이 프로젝트는 여러 명이 동시에 작업할 때 발생할 수 있는 Scene 충돌, Prefab 충돌, `.meta` 누락, 작업 내용 유실, 버전 불일치를 줄이기 위해 Git 및 Unity 협업 규칙을 적용합니다.

모든 팀원은 작업 전에 이 문서를 확인합니다.

## 1. 기본 협업 원칙

- 각 팀원은 자신의 컴퓨터에 별도의 Unity 프로젝트 폴더를 둡니다.
- 팀 작업은 `develop` 브랜치를 통해 공유합니다.
- 실제 기능 개발은 개인 작업 브랜치에서만 진행합니다.
- `main` 과 `develop` 에 직접 Push 하지 않습니다.

기본 흐름:

```text
최신 develop 받기 → 새 작업 브랜치 생성 → 작업 → Push → Pull Request → develop 병합
```

## 2. 브랜치 구성

- `main`: 시연, 제출, 배포가 가능한 안정 버전
- `develop`: 팀 작업이 합쳐진 최신 통합 버전
- 개인 작업 브랜치: 기능 또는 작업 단위로 생성

예시:

```text
feature/ppe-room-layout
feature/ppe-item-selection
feature/gas-detector
feature/admin-monitor
fix/xr-grab-error
refactor/ppe-material
```

원칙:

- 한 브랜치에는 가능하면 하나의 목적만 담습니다.
- 기능이 끝나면 브랜치는 삭제하고, 다음 작업은 새 브랜치로 시작합니다.

## 3. 최초 작업 흐름

프로젝트 시작 시 역할을 나눕니다.

- 팀원 A: PPE 준비실
- 팀원 B: 염산 누출 현장
- 팀원 C: 관리자 모니터 및 UI

작업 시작 예시:

```bash
git switch develop
git pull origin develop
git switch -c feature/ppe-room
```

작업 완료 후:

```bash
git add .
git commit -m "feat: PPE 준비실 기본 구성 추가"
git push origin feature/ppe-room
```

GitHub에서 `feature/ppe-room -> develop` Pull Request를 생성합니다.

## 4. 작업이 한 번 합쳐진 이후의 흐름

첫 병합 이후에는 항상 최신 `develop` 을 다시 받은 뒤 새 브랜치에서 작업합니다.

```bash
git switch develop
git pull origin develop
git switch -c feature/ppe-item-selection
```

작업 중 `develop` 이 바뀌었다면 먼저 현재 작업을 커밋한 뒤 최신 `develop` 을 반영합니다.

```bash
git add .
git commit -m "feat: PPE 선택 기능 작업 중"
git fetch origin
git merge origin/develop
```

## 5. 같은 Scene을 작업할 때

- 같은 `.unity` 파일을 여러 명이 동시에 수정하면 충돌이 발생하기 쉽습니다.
- 따라서 하나의 Scene은 한 명의 담당자만 직접 수정합니다.
- 다른 팀원은 Scene을 직접 고치기보다 Prefab과 Script 단위로 작업합니다.

예시:

- `PPE_Room.unity`: PPE 씬 담당자
- `LeakSite.unity`: 누출 현장 담당자
- `MainMenu.unity`: 메인 UI 담당자

Scene 담당자가 아닌 팀원은 테스트를 위해 Scene을 열 수 있지만, 변경된 Scene 파일은 Commit하지 않습니다.

## 6. Scene에서 여러 명이 작업하는 방법

여러 명이 필요한 기능은 Scene 직접 수정 대신 Prefab과 Script로 분리합니다.

예시 구조:

```text
PPE_Room.unity
├─ Environment
├─ PPE_Storage_Prefab
├─ GasDetector_Prefab
├─ WearStatusUI_Prefab
├─ InteractionSystem_Prefab
└─ XR_Rig_Prefab
```

예시 역할 분담:

- 팀원 A: Scene 구조와 최종 배치
- 팀원 B: GasDetector Prefab 및 측정 Script
- 팀원 C: WearStatusUI Prefab 및 장착 상태 UI

## 7. Unity 프로젝트 필수 설정

모든 팀원은 동일한 Unity 버전을 사용합니다.

필수 설정:

- Version Control Mode: `Visible Meta Files`
- Asset Serialization Mode: `Force Text`

이 설정을 적용하면 `.meta` 파일을 정상 관리할 수 있고, Scene 및 Prefab 변경사항을 Git에서 비교하기 쉬워집니다.

## 8. Unity 파일 관리 규칙

- 에셋과 `.meta` 파일은 항상 함께 Commit합니다.
- `.meta` 파일을 임의로 삭제하지 않습니다.
- Windows 파일 탐색기에서 에셋을 이동하거나 이름을 바꾸지 않습니다.
- 반드시 Unity Project 창에서 이동 및 이름 변경을 합니다.

잘못된 예:

- Windows 탐색기에서 FBX 이동
- Windows 탐색기에서 Prefab 이름 변경
- `.meta` 파일만 삭제

올바른 예:

- Unity Project 창에서 이동
- Unity Project 창에서 이름 변경
- 에셋과 `.meta` 파일을 함께 Commit

## 9. 공용 파일 수정 규칙

아래 파일은 프로젝트 전체에 영향을 줄 수 있으므로 수정 전에 팀원에게 알립니다.

- `ProjectSettings`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `XR Origin` / `XR Rig` Prefab
- `Input Action Asset`
- `GameManager`
- `SceneManager`
- 공용 UI Manager
- 공용 Material
- 공용 ScriptableObject
- Build Settings
- Rendering Pipeline 설정

공용 파일은 가능하면 한 명의 담당자가 관리합니다.

## 10. 저장소에 포함할 파일

포함:

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.gitignore`
- `.gitattributes`
- `README.md`

제외:

- `Library/`
- `Temp/`
- `Logs/`
- `Obj/`
- `Build/`
- `Builds/`
- `UserSettings/`
- `MemoryCaptures/`

VR 프로젝트에서는 대용량 파일이 많으므로, 필요하면 Git LFS 사용을 권장합니다.

권장 확장자:

```text
*.fbx
*.blend
*.psd
*.tif
*.tiff
*.wav
*.mp4
*.mov
```

## 11. Commit 메시지 규칙

형식:

- `feat`: 새로운 기능 추가
- `fix`: 오류 수정
- `refactor`: 코드 구조 정리
- `art`: 모델, Material, Texture 수정
- `scene`: Scene 구성 변경
- `ui`: UI 추가 또는 수정
- `docs`: 문서 수정
- `chore`: 설정 및 기타 작업

예시:

```text
feat: PPE 보호구 선택 기능 추가
fix: VR 컨트롤러 선택 오류 수정
art: 내산 장갑 Material 수정
scene: PPE 준비실 장비 배치
ui: 보호구 장착 상태 패널 추가
docs: GitHub 협업 규칙 수정
```

## 12. Pull Request 작성 규칙

PR에는 아래 내용을 적습니다.

- 작업 내용
- 변경된 Scene
- 변경된 Prefab
- 변경된 Script
- 테스트 방법
- 테스트 결과
- 주의사항

예시:

- 작업 내용: 레이저 포인터로 PPE 보호구를 선택하면 윤곽선이 표시되도록 구현
- 변경된 Scene: 없음
- 변경된 Prefab: `PPE_Item.prefab`
- 변경된 Script: `PPESelectable.cs`, `OutlineController.cs`
- 테스트 방법: PPE 준비실에서 컨트롤러 레이를 보호구에 올려 확인
- 테스트 결과: 선택 시 윤곽선 표시 및 선택 해제 시 윤곽선 제거 정상 작동

## 13. 충돌 발생 시 대응

Unity Scene 또는 Prefab 충돌이 발생하면 임의로 덮어쓰지 않습니다.

대응 순서:

1. 현재 작업을 중지합니다.
2. 충돌 파일을 확인합니다.
3. 현재 작업 브랜치를 원격 저장소에 Push합니다.
4. Scene 또는 Prefab 담당자에게 알립니다.
5. 필요한 변경사항을 확인합니다.
6. Unity에서 기능을 다시 적용합니다.
7. 프로젝트 실행 테스트 후 Commit합니다.

Scene 파일은 자동 병합 결과만 믿지 말고 Unity에서 직접 열어 확인합니다.

## 14. 작업 종료 전 확인

작업 종료 전에는 다음을 확인합니다.

- Unity 프로젝트가 정상적으로 열리는가?
- Console에 새로운 Error가 없는가?
- 기존 기능이 정상 작동하는가?
- 수정하지 않아도 되는 Scene이 변경되지 않았는가?
- 수정하지 않아도 되는 Prefab이 변경되지 않았는가?
- 에셋과 `.meta` 파일이 함께 포함되어 있는가?
- Library, Temp, Build 폴더가 포함되지 않았는가?
- 브랜치에 하나의 작업 목적만 포함되어 있는가?
- 테스트 방법과 결과가 작성되어 있는가?

작업이 끝나면 커밋하고 브랜치를 Push합니다.

```bash
git status
git add .
git commit -m "feat: 작업 내용"
git push origin 현재브랜치명
```

## 15. 절대 하면 안 되는 행동

- `main` 브랜치에서 직접 작업하기
- `develop` 브랜치에서 직접 기능 개발하기
- `main` 또는 `develop` 에 직접 Push하기
- 공유 브랜치에 Force Push 사용하기
- 다른 사람의 브랜치를 허락 없이 수정하기
- 같은 Unity Scene을 여러 명이 동시에 수정하기
- 같은 Prefab을 여러 명이 동시에 수정하기
- Scene 담당자 허락 없이 Scene 파일 Commit하기
- Unity 외부에서 에셋 이름이나 위치 변경하기
- `.meta` 파일 임의 삭제하기
- 충돌 내용을 확인하지 않고 덮어쓰기
- Unity 버전을 혼자서 변경하기
- Package를 팀 공유 없이 추가하거나 삭제하기
- Library, Temp, Build 폴더 올리기
- 프로젝트 전체 ZIP 파일로 덮어쓰기
- API 키, 비밀번호, 토큰 Commit하기
- 오류가 발생한 상태로 `develop` 에 병합하기

## 16. 핵심 요약

- 모든 팀원은 각자 별도의 로컬 프로젝트 폴더에서 작업합니다.
- 최신 버전은 `develop` 에 보관합니다.
- 작업 시작 전에는 항상 최신 `develop` 을 받습니다.
- 기능 작업은 새 브랜치에서 진행합니다.
- 같은 Scene은 한 명의 담당자만 직접 수정합니다.
- 에셋과 `.meta` 파일은 항상 함께 관리합니다.
- 충돌이 발생하면 임의로 덮어쓰지 않습니다.

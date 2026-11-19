<p align="center">
  <img src="Assets/icon.png" width="128" alt="Nuggi 아이콘">
</p>

<h1 align="center">Nuggi</h1>

<p align="center">이미지 배경을 제거(누끼)하고 투명 PNG로 한 번에 저장하는 Windows 앱입니다.</p>

<p align="center">Author: <b>Manggdy</b></p>

<p align="center"><a href="README.md">English</a> | <b>한국어</b></p>

이미지를 추가하고, 실시간 미리보기로 인식된 피사체를 확인하고, 필요하면 영역을 직접 고친 뒤 투명 PNG로 한 번에 저장합니다.

## 다운로드

[Releases](https://github.com/mrkim0118/Nuggi/releases/latest)에서 최신 버전을 받습니다.

- **`Nuggi-Setup-<버전>.exe`** (추천): 설치 프로그램. 설치 폴더를 직접 고를 수 있습니다. 기본은 관리자 권한 없이 현재 사용자에게 설치되고, 모든 사용자용(Program Files, 관리자 권한 필요)으로도 설치할 수 있습니다. 시작 메뉴 바로가기(바탕화면 바로가기는 선택)를 만들고, Windows 설정 > 앱에서 제거할 수 있습니다.
- **`Nuggi-win-x64.zip`**: 포터블 버전. 압축을 풀고 `Nuggi.exe`를 실행합니다.

둘 다 Python과 모델이 들어 있습니다. .NET 8 Desktop Runtime (x64)이 필요하며, 없으면 설치 프로그램이 다운로드 페이지를 열어 줍니다.

## 주요 기능

- **파일·폴더 추가**: 이미지 파일을 하나씩 추가하거나, 폴더 안의 지원 이미지를 하위 폴더까지 한꺼번에 추가합니다.
- **나란히 보는 미리보기**: 원본 이미지와 인식된 피사체 영역을 함께 보여줍니다.
- **마스크 편집기**: 브러시로 영역 추가/지우기, 브러시 크기, 확대/축소, 실행 취소(`Ctrl+Z`), 선택 영역 반전, 영역 전체 삭제, 자동 마스크로 되돌리기를 지원합니다.
- **객체 클릭**: 마스크 편집기에서 객체를 클릭해 그 객체만 추가하거나 지웁니다 (Segment Anything).
- **마스크 저장** (`마스크 저장`): 선택한 이미지의 마스크를 흑백 PNG로 저장합니다 (흰색 = 남길 영역, 직접 편집한 내용 포함).
- **일괄 저장**: 투명 PNG로 한 번에 저장합니다. 한 이미지가 실패해도 나머지는 계속 진행되며, 실패 이유는 로그에 표시됩니다.
- **여백 자르기**: 피사체 주변의 빈 투명 여백을 선택적으로 자릅니다.
- **자동 재시도**: 첫 마스크가 이미지 거의 전체이거나 거의 비어 있으면, 이미 내려받은 다른 모델로 다시 시도합니다.
- **깔끔한 종료**: 창을 닫으면 실행 중인 백그라운드 작업도 중지합니다.

## 요구 사항

- Windows 10 또는 11 (x64)
- Microsoft .NET 8 Desktop Runtime (x64)
- 소스에서 실행할 때: Python (`run.bat`이 가상환경을 만듭니다). 배포 패키지에는 포터블 Python 런타임이 들어 있어 대상 PC에 Python이 없어도 됩니다.

## 사용법

1. `파일 추가` 또는 `폴더 추가` 클릭
2. 목록에서 이미지 선택
3. 오른쪽 미리보기 확인: 초록색이 남길 영역
4. 선택: `영역 편집`으로 영역을 고친 뒤 `편집 적용`
   - `브러시`: 마우스로 칠하기
   - `객체 클릭`: 객체 클릭 — `영역 추가`면 추가, `영역 지우기`면 제거
   - 객체 하나만 남기려면: `영역 전체 삭제` 후 `객체 클릭` + `영역 추가`로 그 객체를 클릭
5. 저장 폴더, 모델, 여백 자르기 옵션 선택
6. `누끼 추출 시작` 클릭

마스크만 필요하면 이미지를 선택하고 `마스크 저장`을 클릭합니다.

직접 편집한 마스크는 앱을 닫을 때까지 유지됩니다.

### 처음 실행할 때

처음 배경 제거를 실행하면 분할 모델을 자동으로 내려받습니다(`u2net` 약 170 MB). 객체 클릭은 처음 쓸 때 Segment Anything을 내려받습니다(약 113 MB). 배포 패키지에는 둘 다 들어 있습니다.

설치 후 처음 실행할 때는 엔진이 코드를 준비하느라 최대 1분 정도 걸립니다("Python 배경 제거 엔진을 확인하는 중..."). 다음부터는 더 빨리 시작됩니다.

자동 마스크 재시도는 이미 내려받은 대체 모델(`.u2net` 또는 `%USERPROFILE%\.u2net`)만 사용하므로, 일괄 처리 도중 큰 다운로드가 시작되지 않습니다.

### 지원 형식

`.png`, `.jpg`, `.jpeg`, `.bmp`, `.webp`, `.tif`, `.tiff`

### 출력 파일 이름

- `photo.jpg` -> `photo_nuggi.png`
- 같은 이름이 이미 있거나 입력 파일 이름이 겹치면 `photo_nuggi_1.png`, `photo_nuggi_2.png` 순으로 저장
- 마스크: `photo.jpg` -> `photo_mask.png` (원본과 같은 크기, 이미 있으면 `photo_mask_1.png`, …)

## 데이터와 설정

| 데이터 | 위치 |
|---|---|
| 설치된 앱(설치 프로그램 사용 시) | 설치할 때 고른 폴더. 기본값: `%LOCALAPPDATA%\Programs\Nuggi`(현재 사용자) 또는 `%ProgramFiles%\Nuggi`(모든 사용자) |
| 저장 결과(기본 저장 폴더) | `Nuggi.exe` 옆의 `output`. 그 폴더에 쓸 수 없으면(예: Program Files) `사진\Nuggi`. 앱을 제거해도 남음 |
| 마스크·미리보기 캐시 | `%TEMP%\Nuggi` (30일이 지난 파일은 앱 시작 시 삭제) |
| 분할 모델 | `%USERPROFILE%\.u2net` |

## 소스에서 빌드

### 빌드

`run.bat`을 실행합니다. 이 스크립트는 다음을 수행합니다.

1. 필요하면 Python 가상환경(`.venv`) 생성
2. 필요하면 Python 의존성 설치
3. WinForms 앱 빌드
4. `Nuggi.exe` 실행

앱이 Python을 찾을 수 없다고 하면 `.venv`가 더 이상 없는 Python 설치를 가리키는 것입니다. `.venv` 폴더를 지우고 `run.bat`을 다시 실행하세요.

### 테스트

```powershell
.venv\Scripts\python.exe -m unittest discover -s tests
```

### 배포

```powershell
.\publish.bat
```

`publish/Nuggi-win-x64`, zip 압축 파일, 설치 프로그램 `publish/Nuggi-Setup-<버전>.exe`가 만들어집니다. 설치 프로그램 단계에는 [Inno Setup 6](https://jrsoftware.org/isdl.php)이 필요하며, 없으면 이 단계는 건너뜁니다. 설치 스크립트는 `installer/Nuggi.iss`입니다.

패키지에는 배경 제거 엔진에 필요한 모든 것이 들어 있습니다: 모든 패키지가 설치된 포터블 Python 런타임(`python/`)과 `%USERPROFILE%\.u2net`에서 가져온 모델(`.u2net/`).

앱 크기를 줄이기 위해 앱 자체는 프레임워크 종속 방식이므로, 대상 PC에 .NET 8 Desktop Runtime (x64)이 있어야 합니다. 압축을 풀고 `Nuggi.exe`(또는 `Launch Nuggi.bat`)를 실행하세요. 배포 패키지에는 `.pdb` 디버그 심볼 파일이 포함되지 않습니다.

## 프로젝트 구조

| 경로 | 내용 |
|---|---|
| `MainForm.cs` | 메인 창 (파일 목록, 설정, 미리보기, 일괄 실행) |
| `MaskEditorForm.cs`, `Controls/MaskCanvasControl.cs` | 마스크 편집기 |
| `Theme.cs` | 공통 색상, 글꼴, 컨트롤 스타일 |
| `Services/NuggiPythonBridge.cs` | Python 워커 실행 |
| `scripts/nuggi_worker.py` | 워커 명령 (`mask`, `select`, `convert`, `batch`) |
| `nuggi/background_remover.py` | 마스크 생성과 품질 검사 |
| `tests/test_worker.py` | Python 테스트 |
| `Assets/` | 앱 아이콘(`icon.ico`)과 README용 아이콘(`icon.png`) |
| `installer/Nuggi.iss` | 설치 프로그램 스크립트 (Inno Setup 6) |
| `run.bat`, `publish.bat` | 실행·배포 스크립트 |

## 사용한 라이브러리

- C# WinForms (.NET 8): UI
- [rembg](https://github.com/danielgatis/rembg) (Pillow, onnxruntime): 배경 제거
- [Segment Anything](https://github.com/facebookresearch/segment-anything) ViT-B 양자화 모델 (`sam_vit_b_01ec64`, Apache-2.0): 객체 선택

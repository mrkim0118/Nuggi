<p align="center">
  <img src="Assets/icon.png" width="128" alt="Nuggi icon">
</p>

<h1 align="center">Nuggi</h1>

<p align="center">A Windows app that removes image backgrounds and saves transparent PNG files in one batch.</p>

<p align="center">Author: <b>Manggdy</b></p>

<p align="center"><b>English</b> | <a href="README.ko.md">한국어</a></p>

Add images, check the detected subject in a live preview, fix the area by hand if needed, and save transparent PNG files in one batch.

## Download

Get the latest version from [Releases](https://github.com/mrkim0118/Nuggi/releases/latest).

- **`Nuggi-Setup-<version>.exe`** (recommended): installer. You choose the install folder. By default it installs for the current user without admin rights; it can also install for all users (Program Files, needs admin rights). It adds a Start menu shortcut (desktop shortcut optional) and can be removed from Windows Settings > Apps.
- **`Nuggi-win-x64.zip`**: portable version. Unzip and run `Nuggi.exe`.

Both include Python and the models. The .NET 8 Desktop Runtime (x64) is required; the installer opens its download page if it is missing.

## Features

- **Add files or folders**: Add image files one by one, or every supported image in a folder (subfolders included).
- **Side-by-side preview**: The original image and the detected subject area.
- **Mask editor**: Paint to add or erase areas, brush size, zoom, undo (`Ctrl+Z`), invert the selection (`선택 영역 반전`), delete the whole area (`영역 전체 삭제`), reset to the automatic mask.
- **Object click**: In the mask editor, click an object to add or erase just that object (Segment Anything).
- **Save mask** (`마스크 저장`): Save the selected image's mask as a black-and-white PNG (white = kept area), hand edits included.
- **Batch export**: Export to transparent PNG; one failed image does not stop the rest, and the log shows why it failed.
- **Trim**: Optionally trim empty transparent margins around the subject.
- **Automatic retry**: When the first mask covers almost the whole image or almost nothing, Nuggi retries with another already-downloaded model.
- **Clean exit**: Closing the window stops any running background work.
- **Update check**: On launch, checks the latest GitHub release and asks whether to download and install the new version.

## Requirements

- Windows 10 or 11 (x64)
- Microsoft .NET 8 Desktop Runtime (x64)
- From source: Python (`run.bat` creates the virtual environment). Release packages include a portable Python runtime, so the target PC does not need Python.

## Usage

1. Click `파일 추가` or `폴더 추가`
2. Select an image in the list
3. Check the previews on the right: green is the area that will be kept
4. Optional: click `영역 편집` to change the area, then `편집 적용`
   - `브러시`: paint with the mouse
   - `객체 클릭`: click an object; `영역 추가` adds it, `영역 지우기` removes it
   - To keep only one object: `영역 전체 삭제`, then `객체 클릭` + `영역 추가` and click that object
5. Choose the save folder, model, and trim option
6. Click `누끼 추출 시작`

To get only the mask, select an image and click `마스크 저장`.

Hand-edited masks are kept until the app closes.

### First run

The first background-removal run downloads the segmentation model automatically (about 170 MB for `u2net`). Object click downloads Segment Anything on first use (about 113 MB). Release packages already include both.

The first start after installing takes up to about a minute while the engine prepares its code ("Python 배경 제거 엔진을 확인하는 중..."); later starts are faster.

The automatic mask retry only uses fallback models that are already downloaded (in `.u2net` or `%USERPROFILE%\.u2net`), so it never starts a large download in the middle of a batch.

### Supported formats

`.png`, `.jpg`, `.jpeg`, `.bmp`, `.webp`, `.tif`, `.tiff`

### Output naming

- `photo.jpg` -> `photo_nuggi.png`
- If the name already exists (or two inputs share a name), Nuggi saves `photo_nuggi_1.png`, `photo_nuggi_2.png`, and so on.
- Mask: `photo.jpg` -> `photo_mask.png` (same size as the original image; `photo_mask_1.png`, … if it already exists)

## Data & Settings

| Data | Location |
|---|---|
| Installed app (installer) | The folder chosen in setup. Default: `%LOCALAPPDATA%\Programs\Nuggi` (current user) or `%ProgramFiles%\Nuggi` (all users) |
| Saved results (default save folder) | `output` next to `Nuggi.exe`; if that folder is read-only (for example Program Files), `Pictures\Nuggi`. Kept when uninstalling |
| Mask and preview cache | `%TEMP%\Nuggi` (files older than 30 days are deleted at startup) |
| Segmentation models | `%USERPROFILE%\.u2net` |

## Build from Source

### Build

Use `run.bat`. It will:

1. Create the Python virtual environment (`.venv`) if needed
2. Install Python dependencies if needed
3. Build the WinForms app
4. Launch `Nuggi.exe`

If the app reports that Python could not be found, the `.venv` folder points to a Python install that no longer exists. Delete `.venv` and run `run.bat` again.

### Test

```powershell
.venv\Scripts\python.exe -m unittest discover -s tests
```

### Release

```powershell
.\publish.bat
```

This creates `publish/Nuggi-win-x64`, its zip archive, and the installer `publish/Nuggi-Setup-<version>.exe`. The installer step needs [Inno Setup 6](https://jrsoftware.org/isdl.php) and is skipped if it is not installed; the script is `installer/Nuggi.iss`.

The package includes everything the background-removal engine needs: a portable Python runtime (`python/`) with all packages, and the downloaded models from `%USERPROFILE%\.u2net` (`.u2net/`).

The app itself is framework-dependent to keep its size smaller, so the target PC must have the .NET 8 Desktop Runtime (x64). Unzip the package and run `Nuggi.exe` (or `Launch Nuggi.bat`). Release packages do not include `.pdb` debug-symbol files.

## Project Layout

| Path | Contents |
|---|---|
| `MainForm.cs` | Main window (file list, settings, preview, batch run) |
| `MaskEditorForm.cs`, `Controls/MaskCanvasControl.cs` | Mask editor |
| `Theme.cs` | Shared colors, fonts, and control styles |
| `Services/NuggiPythonBridge.cs` | Runs the Python worker |
| `scripts/nuggi_worker.py` | Worker commands (`mask`, `select`, `convert`, `batch`) |
| `nuggi/background_remover.py` | Mask generation and quality check |
| `tests/test_worker.py` | Python tests |
| `Assets/` | App icon (`icon.ico`) and README icon (`icon.png`) |
| `installer/Nuggi.iss` | Installer script (Inno Setup 6) |
| `run.bat`, `publish.bat` | Run and release scripts |
| `GitHubUpdater.cs` | GitHub release update check |

## Libraries

- C# WinForms (.NET 8): UI
- [rembg](https://github.com/danielgatis/rembg) (Pillow, onnxruntime): background removal
- [Segment Anything](https://github.com/facebookresearch/segment-anything) ViT-B, quantized (`sam_vit_b_01ec64`, Apache-2.0): object selection

# Nuggi

`Nuggi` is a Windows app for removing image backgrounds (누끼). Add images, check the detected subject in a live preview, fix the area by hand if needed, and save transparent PNG files in one batch.

Author: **Manggdy**

## Features

- Add image files one by one, or every supported image in a folder (subfolders included)
- Side-by-side preview: the original image and the detected subject area
- Mask editor: paint to add or erase areas, brush size, zoom, undo (`Ctrl+Z`), clear all, reset to the automatic mask
- **Object click** in the mask editor: click an object to add or erase just that object (Segment Anything)
- Batch export to transparent PNG; one failed image does not stop the rest, and the log shows why it failed
- Three save modes:
  - **한 장으로 저장**: one PNG with everything the mask keeps
  - **떨어진 덩어리별로 따로 저장**: one PNG per separate region of the mask (regions that touch stay together)
  - **자동 인식한 사물별로 따로 저장**: finds objects (person, bottle, cup, bowl, … 80 kinds) and saves each as its own PNG, even when they overlap
- Optional trimming of empty transparent margins around the subject
- Automatic retry with another already-downloaded model when the first mask covers almost the whole image or almost nothing
- Closing the window stops any running background work

## Stack

- UI: C# WinForms (.NET 8)
- Background removal engine: Python `rembg` (Pillow, onnxruntime)
- Object selection: Segment Anything ViT-B, quantized (`sam_vit_b_01ec64`, Apache-2.0)
- Object detection: Mask R-CNN from the ONNX Model Zoo (`MaskRCNN-12.onnx`, MIT, COCO classes)
- Output: transparent PNG

## Run

Use `run.bat`. It will:

1. Create the Python virtual environment (`.venv`) if needed
2. Install Python dependencies if needed
3. Build the WinForms app
4. Launch `Nuggi.exe`

If the app reports that Python could not be found, the `.venv` folder points to a Python install that no longer exists. Delete `.venv` and run `run.bat` again.

## First run

The first background-removal run downloads the segmentation model automatically (about 170 MB for `u2net`). Object click downloads Segment Anything on first use (about 113 MB), and the object save mode downloads Mask R-CNN (about 170 MB). Release packages already include all of them.

The automatic mask retry only uses fallback models that are already downloaded (in `.u2net` or `%USERPROFILE%\.u2net`), so it never starts a large download in the middle of a batch.

## Usage

1. Click `파일 추가` or `폴더 추가`
2. Select an image in the list
3. Check the previews on the right: green is the area that will be kept
4. Optional: click `영역 편집` to change the area, then `편집 적용`
   - `브러시`: paint with the mouse
   - `객체 클릭`: click an object; `영역 추가` adds it, `영역 지우기` removes it
   - To keep only one object: `모두 지우기`, then `객체 클릭` + `영역 추가` and click that object
5. Choose the save folder, model, save mode, and trim option
6. Click `누끼 추출 시작`

Hand-edited masks are kept until the app closes.

## Supported formats

- `.png`
- `.jpg`, `.jpeg`
- `.bmp`
- `.webp`
- `.tif`, `.tiff`

## Output naming

- `photo.jpg` -> `photo_nuggi.png`
- If the name already exists (or two inputs share a name), Nuggi saves `photo_nuggi_1.png`, `photo_nuggi_2.png`, and so on.
- Region mode: `photo_nuggi_1.png`, `photo_nuggi_2.png`, … (largest region first)
- Object mode: `photo_nuggi_1_person.png`, `photo_nuggi_2_bottle.png`, … (most confident first)
- Region and object files never overwrite existing files; a `-1`, `-2` suffix is added instead.

## Temporary files

Masks and preview copies are cached in `%TEMP%\Nuggi`. Files older than 30 days are deleted when the app starts.

## Tests

```bat
.venv\Scripts\python.exe -m unittest discover -s tests
```

## Portable distribution

Run `publish.bat` to create `publish/Nuggi-win-x64` and its zip archive.

The package includes everything the background-removal engine needs: a portable Python runtime (`python/`) with all packages, and the downloaded models from `%USERPROFILE%\.u2net` (`.u2net/`). The target PC does not need Python.

The app itself is framework-dependent to keep its size smaller, so the target PC must have the Microsoft .NET 8 Desktop Runtime (x64) installed. Unzip the package and run `Nuggi.exe` (or `Launch Nuggi.bat`). Release packages do not include `.pdb` debug-symbol files.

## Main files

- `MainForm.cs`: main window (file list, settings, preview, batch run)
- `MaskEditorForm.cs`, `Controls/MaskCanvasControl.cs`: mask editor
- `Theme.cs`: shared colors, fonts, and control styles
- `Assets/icon.ico`: app icon
- `Services/NuggiPythonBridge.cs`: runs the Python worker
- `scripts/nuggi_worker.py`: worker commands (`mask`, `convert`, `batch`)
- `nuggi/background_remover.py`: mask generation and quality check
- `tests/test_worker.py`: Python tests
- `run.bat`, `publish.bat`

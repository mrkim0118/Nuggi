# Nuggi

`Nuggi` is a Windows app for removing image backgrounds (누끼). Add images, check the detected subject in a live preview, fix the area by hand if needed, and save transparent PNG files in one batch.

Author: **Manggdy**

## Features

- Add image files one by one, or every supported image in a folder (subfolders included)
- Side-by-side preview: the original image and the detected subject area
- Mask editor: paint to add or erase areas, brush size, zoom, undo (`Ctrl+Z`), reset to the automatic mask
- Batch export to transparent PNG; one failed image does not stop the rest, and the log shows why it failed
- Optional trimming of empty transparent margins around the subject
- Automatic retry with another already-downloaded model when the first mask covers almost the whole image or almost nothing
- Closing the window stops any running background work

## Stack

- UI: C# WinForms (.NET 8)
- Background removal engine: Python `rembg` (Pillow, onnxruntime)
- Output: transparent PNG

## Run

Use `run.bat`. It will:

1. Create the Python virtual environment (`.venv`) if needed
2. Install Python dependencies if needed
3. Build the WinForms app
4. Launch `Nuggi.exe`

If the app reports that Python could not be found, the `.venv` folder points to a Python install that no longer exists. Delete `.venv` and run `run.bat` again.

## First run

The first background-removal run downloads the segmentation model automatically (about 170 MB for `u2net`).

The automatic mask retry only uses fallback models that are already downloaded (in `.u2net` or `%USERPROFILE%\.u2net`), so it never starts a large download in the middle of a batch.

## Usage

1. Click `파일 추가` or `폴더 추가`
2. Select an image in the list
3. Check the previews on the right: green is the area that will be kept
4. Optional: click `영역 편집` to add or erase areas by hand, then `편집 적용`
5. Choose the save folder, model, and trim option
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

## Temporary files

Masks and preview copies are cached in `%TEMP%\Nuggi`. Files older than 30 days are deleted when the app starts.

## Tests

```bat
.venv\Scripts\python.exe -m unittest discover -s tests
```

## Portable distribution

Run `publish.bat` to create `publish/Nuggi-win-x64` and its zip archive.

The distribution is framework-dependent to keep its size smaller. The target PC must have the Microsoft .NET 8 Desktop Runtime (x64) installed. Release packages do not include `.pdb` debug-symbol files.

## Main files

- `MainForm.cs`: main window (file list, settings, preview, batch run)
- `MaskEditorForm.cs`, `Controls/MaskCanvasControl.cs`: mask editor
- `Theme.cs`: shared colors, fonts, and control styles
- `Services/NuggiPythonBridge.cs`: runs the Python worker
- `scripts/nuggi_worker.py`: worker commands (`mask`, `convert`, `batch`)
- `nuggi/background_remover.py`: mask generation and quality check
- `tests/test_worker.py`: Python tests
- `run.bat`, `publish.bat`

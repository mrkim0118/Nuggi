# Nuggi

`Nuggi` is a C# WinForms app for removing image backgrounds.

## Stack

- UI: C# WinForms
- Background removal engine: Python `rembg`
- Preview: original image + cutout preview
- Output: transparent PNG

## Features

- Add individual image files
- Add all supported images from a folder
- Preview the original image without clipping
- Preview the expected cutout result without clipping
- Batch export transparent PNG files
- Optional transparent-edge trimming
- Automatic mask retry when the first result incorrectly covers almost the whole image

## Run

Use `run.bat`.

It will:

1. Create the Python virtual environment if needed
2. Install Python dependencies if needed
3. Build the WinForms app
4. Launch `Nuggi.exe`

## First run

The first background-removal run may download the segmentation model automatically.

## Portable distribution

Run `publish.bat` to create `publish/Nuggi-win-x64` and its zip archive.

The distribution is framework-dependent to keep its size smaller. The target PC must have the Microsoft .NET 8 Desktop Runtime (x64) installed. Release packages do not include `.pdb` debug-symbol files.

## Usage

1. Click `이미지 추가` or `폴더 추가`
2. Select a file in the list
3. Check the previews on the right
4. Adjust output folder, model, and trim option if needed
5. Click `누끼 추출 시작`

## Supported formats

- `.png`
- `.jpg`, `.jpeg`
- `.bmp`
- `.webp`
- `.tif`, `.tiff`

## Output naming

- `photo.jpg` -> `photo_nuggi.png`
- If the name already exists, Nuggi saves `photo_nuggi_1.png`, `photo_nuggi_2.png`, and so on.

## Main files

- `Nuggi.csproj`
- `MainForm.cs`
- `Services/NuggiPythonBridge.cs`
- `scripts/nuggi_worker.py`
- `nuggi/background_remover.py`
- `run.bat`

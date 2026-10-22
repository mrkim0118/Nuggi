using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Nuggi.Services;

internal static class MaskPreviewRenderer
{
    public static Bitmap LoadSourceBitmap(string imagePath)
    {
        using var sourceImage = Image.FromFile(imagePath);
        ApplyExifOrientation(sourceImage);

        var bitmap = new Bitmap(sourceImage.Width, sourceImage.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(sourceImage, 0, 0, sourceImage.Width, sourceImage.Height);
        return bitmap;
    }

    public static Bitmap LoadPreviewBitmap(string imagePath, int maxDimension)
    {
        using var source = LoadSourceBitmap(imagePath);
        return ResizeToMaxDimension(source, maxDimension);
    }

    public static Bitmap LoadMaskBitmap(string maskPath)
    {
        using var sourceMask = Image.FromFile(maskPath);

        var bitmap = new Bitmap(sourceMask.Width, sourceMask.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(sourceMask, 0, 0, sourceMask.Width, sourceMask.Height);

        NormalizeMaskInPlace(bitmap);
        return bitmap;
    }

    public static Bitmap BuildCutoutPreview(string imagePath, string maskPath, int maxDimension)
    {
        using var original = LoadSourceBitmap(imagePath);
        using var mask = LoadMaskBitmap(maskPath);
        return BuildCutoutPreview(original, mask, maxDimension);
    }

    public static Bitmap BuildCutoutPreview(Bitmap original, Bitmap mask, int maxDimension)
    {
        using var scaledOriginal = ResizeToMaxDimension(original, maxDimension);
        using var scaledMask = ResizeBitmap(mask, scaledOriginal.Width, scaledOriginal.Height);
        NormalizeMaskInPlace(scaledMask);

        var checkerboard = CreateCheckerboard(scaledOriginal.Width, scaledOriginal.Height);
        CompositeCutout(checkerboard, scaledOriginal, scaledMask);
        DrawMaskOutline(checkerboard, scaledMask, Color.FromArgb(34, 197, 94));
        return checkerboard;
    }

    public static Bitmap BuildMaskOverlayPreview(
        string imagePath,
        string currentMaskPath,
        string? baselineMaskPath,
        int maxDimension)
    {
        using var original = LoadSourceBitmap(imagePath);
        using var currentMask = LoadMaskBitmap(currentMaskPath);
        using var baselineMask = string.IsNullOrWhiteSpace(baselineMaskPath)
            ? null
            : LoadMaskBitmap(baselineMaskPath);

        var scale = Math.Min(1.0, maxDimension / (double)Math.Max(original.Width, original.Height));
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));

        return BuildMaskOverlayPreview(original, currentMask, baselineMask, width, height);
    }

    public static Bitmap BuildMaskOverlayPreview(
        Bitmap original,
        Bitmap currentMask,
        Bitmap? baselineMask,
        int maxDimension)
    {
        var scale = Math.Min(1.0, maxDimension / (double)Math.Max(original.Width, original.Height));
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));

        return BuildMaskOverlayPreview(original, currentMask, baselineMask, width, height);
    }

    public static Bitmap BuildMaskOverlayPreview(
        Bitmap original,
        Bitmap currentMask,
        Bitmap? baselineMask,
        int targetWidth,
        int targetHeight)
    {
        var width = Math.Max(1, targetWidth);
        var height = Math.Max(1, targetHeight);

        using var scaledOriginal = ResizeBitmap(original, width, height);
        using var scaledCurrentMask = ResizeBitmap(currentMask, width, height);
        NormalizeMaskInPlace(scaledCurrentMask);

        using var scaledBaselineMask = baselineMask is null
            ? null
            : ResizeBitmap(baselineMask, width, height);
        if (scaledBaselineMask is not null)
        {
            NormalizeMaskInPlace(scaledBaselineMask);
        }

        var preview = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(preview);
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(scaledOriginal, 0, 0, width, height);

        CompositeMaskOverlay(preview, scaledCurrentMask, scaledBaselineMask);
        DrawMaskOutline(preview, scaledCurrentMask, Color.FromArgb(16, 185, 129));
        return preview;
    }

    public static RectangleF GetImageRectangle(Rectangle bounds, Size imageSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return RectangleF.Empty;
        }

        var scale = Math.Min(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
        var width = imageSize.Width * scale;
        var height = imageSize.Height * scale;
        var x = bounds.X + (bounds.Width - width) / 2f;
        var y = bounds.Y + (bounds.Height - height) / 2f;
        return new RectangleF(x, y, width, height);
    }

    public static bool AreMasksEqual(Bitmap left, Bitmap right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return false;
        }

        using var normalizedLeft = (Bitmap)left.Clone();
        using var normalizedRight = (Bitmap)right.Clone();
        NormalizeMaskInPlace(normalizedLeft);
        NormalizeMaskInPlace(normalizedRight);

        var rect = new Rectangle(0, 0, normalizedLeft.Width, normalizedLeft.Height);
        var leftData = normalizedLeft.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var rightData = normalizedRight.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var leftBuffer = new byte[Math.Abs(leftData.Stride) * normalizedLeft.Height];
            var rightBuffer = new byte[Math.Abs(rightData.Stride) * normalizedRight.Height];
            Marshal.Copy(leftData.Scan0, leftBuffer, 0, leftBuffer.Length);
            Marshal.Copy(rightData.Scan0, rightBuffer, 0, rightBuffer.Length);

            for (var index = 0; index < leftBuffer.Length; index += 4)
            {
                if (leftBuffer[index + 2] != rightBuffer[index + 2])
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            normalizedLeft.UnlockBits(leftData);
            normalizedRight.UnlockBits(rightData);
        }
    }

    public static void SaveMask(Bitmap mask, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var normalized = (Bitmap)mask.Clone();
        NormalizeMaskInPlace(normalized);
        normalized.Save(outputPath, ImageFormat.Png);
    }

    public static void NormalizeMaskInPlace(Bitmap mask)
    {
        var rect = new Rectangle(0, 0, mask.Width, mask.Height);
        var data = mask.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            var buffer = new byte[Math.Abs(data.Stride) * mask.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            for (var index = 0; index < buffer.Length; index += 4)
            {
                var blue = buffer[index];
                var green = buffer[index + 1];
                var red = buffer[index + 2];
                var alpha = buffer[index + 3];
                var colorValue = Math.Max(red, Math.Max(green, blue));
                var gray = (byte)Math.Clamp(
                    Math.Round(colorValue * (alpha / 255d)),
                    0,
                    255);

                buffer[index] = gray;
                buffer[index + 1] = gray;
                buffer[index + 2] = gray;
                buffer[index + 3] = 255;
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            mask.UnlockBits(data);
        }
    }

    private static Bitmap ResizeToMaxDimension(Bitmap source, int maxDimension)
    {
        var scale = Math.Min(1.0, maxDimension / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return ResizeBitmap(source, width, height);
    }

    private static Bitmap ResizeBitmap(Image source, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        return bitmap;
    }

    private static Bitmap CreateCheckerboard(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(236, 239, 244));

        const int blockSize = 18;
        using var brush = new SolidBrush(Color.FromArgb(213, 219, 230));

        for (var top = 0; top < height; top += blockSize)
        {
            for (var left = 0; left < width; left += blockSize)
            {
                if (((left / blockSize) + (top / blockSize)) % 2 == 0)
                {
                    continue;
                }

                graphics.FillRectangle(brush, left, top, blockSize, blockSize);
            }
        }

        return bitmap;
    }

    private static void DrawMaskOutline(Bitmap destination, Bitmap mask, Color outlineColor)
    {
        var rect = new Rectangle(0, 0, destination.Width, destination.Height);
        var destData = destination.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var maskData = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var destBuffer = new byte[Math.Abs(destData.Stride) * destination.Height];
            var maskBuffer = new byte[Math.Abs(maskData.Stride) * mask.Height];

            Marshal.Copy(destData.Scan0, destBuffer, 0, destBuffer.Length);
            Marshal.Copy(maskData.Scan0, maskBuffer, 0, maskBuffer.Length);

            for (var y = 1; y < destination.Height - 1; y++)
            {
                for (var x = 1; x < destination.Width - 1; x++)
                {
                    var index = y * destData.Stride + x * 4;
                    var center = maskBuffer[index + 2];
                    if (center < 24)
                    {
                        continue;
                    }

                    var hasEdgeNeighbor =
                        maskBuffer[index - 4 + 2] < 24 ||
                        maskBuffer[index + 4 + 2] < 24 ||
                        maskBuffer[index - maskData.Stride + 2] < 24 ||
                        maskBuffer[index + maskData.Stride + 2] < 24;
                    if (!hasEdgeNeighbor)
                    {
                        continue;
                    }

                    BlendPixel(destBuffer, index, outlineColor, 0.9f);
                }
            }

            Marshal.Copy(destBuffer, 0, destData.Scan0, destBuffer.Length);
        }
        finally
        {
            destination.UnlockBits(destData);
            mask.UnlockBits(maskData);
        }
    }

    private static void CompositeMaskOverlay(Bitmap destination, Bitmap currentMask, Bitmap? baselineMask)
    {
        var rect = new Rectangle(0, 0, destination.Width, destination.Height);
        var destData = destination.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var currentMaskData = currentMask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData? baselineMaskData = null;

        try
        {
            if (baselineMask is not null)
            {
                baselineMaskData = baselineMask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            }

            var destBuffer = new byte[Math.Abs(destData.Stride) * destination.Height];
            var currentMaskBuffer = new byte[Math.Abs(currentMaskData.Stride) * currentMask.Height];
            byte[]? baselineMaskBuffer = baselineMaskData is null
                ? null
                : new byte[Math.Abs(baselineMaskData.Stride) * baselineMask!.Height];

            Marshal.Copy(destData.Scan0, destBuffer, 0, destBuffer.Length);
            Marshal.Copy(currentMaskData.Scan0, currentMaskBuffer, 0, currentMaskBuffer.Length);
            if (baselineMaskData is not null && baselineMaskBuffer is not null)
            {
                Marshal.Copy(baselineMaskData.Scan0, baselineMaskBuffer, 0, baselineMaskBuffer.Length);
            }

            for (var index = 0; index < destBuffer.Length; index += 4)
            {
                var currentAlpha = currentMaskBuffer[index + 2] / 255f;
                var baselineAlpha = baselineMaskBuffer is null ? 0f : baselineMaskBuffer[index + 2] / 255f;
                var addedAlpha = Math.Max(0f, currentAlpha - baselineAlpha);
                var removedAlpha = Math.Max(0f, baselineAlpha - currentAlpha);

                if (currentAlpha > 0f)
                {
                    BlendPixel(destBuffer, index, Color.FromArgb(34, 197, 94), Math.Min(0.35f, currentAlpha * 0.35f));
                }

                if (addedAlpha > 0f)
                {
                    BlendPixel(destBuffer, index, Color.FromArgb(22, 163, 74), Math.Min(0.72f, addedAlpha * 0.95f));
                }

                if (removedAlpha > 0f)
                {
                    BlendPixel(destBuffer, index, Color.FromArgb(220, 38, 38), Math.Min(0.68f, removedAlpha * 0.9f));
                }
            }

            Marshal.Copy(destBuffer, 0, destData.Scan0, destBuffer.Length);
        }
        finally
        {
            destination.UnlockBits(destData);
            currentMask.UnlockBits(currentMaskData);
            if (baselineMask is not null && baselineMaskData is not null)
            {
                baselineMask.UnlockBits(baselineMaskData);
            }
        }
    }

    private static void BlendPixel(byte[] destinationBuffer, int index, Color overlayColor, float overlayAlpha)
    {
        if (overlayAlpha <= 0f)
        {
            return;
        }

        var alpha = Math.Clamp(overlayAlpha, 0f, 1f);
        var inverseAlpha = 1f - alpha;

        destinationBuffer[index] = (byte)Math.Clamp(
            Math.Round(destinationBuffer[index] * inverseAlpha + overlayColor.B * alpha),
            0,
            255);
        destinationBuffer[index + 1] = (byte)Math.Clamp(
            Math.Round(destinationBuffer[index + 1] * inverseAlpha + overlayColor.G * alpha),
            0,
            255);
        destinationBuffer[index + 2] = (byte)Math.Clamp(
            Math.Round(destinationBuffer[index + 2] * inverseAlpha + overlayColor.R * alpha),
            0,
            255);
        destinationBuffer[index + 3] = 255;
    }

    private static void CompositeCutout(Bitmap destination, Bitmap original, Bitmap mask)
    {
        var rect = new Rectangle(0, 0, destination.Width, destination.Height);
        var destData = destination.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var originalData = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var maskData = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var destBuffer = new byte[Math.Abs(destData.Stride) * destination.Height];
            var originalBuffer = new byte[Math.Abs(originalData.Stride) * original.Height];
            var maskBuffer = new byte[Math.Abs(maskData.Stride) * mask.Height];

            Marshal.Copy(destData.Scan0, destBuffer, 0, destBuffer.Length);
            Marshal.Copy(originalData.Scan0, originalBuffer, 0, originalBuffer.Length);
            Marshal.Copy(maskData.Scan0, maskBuffer, 0, maskBuffer.Length);

            for (var index = 0; index < destBuffer.Length; index += 4)
            {
                var alpha = maskBuffer[index + 2] / 255f;
                if (alpha <= 0f)
                {
                    continue;
                }

                var inverseAlpha = 1f - alpha;
                destBuffer[index] = (byte)Math.Clamp(
                    Math.Round(originalBuffer[index] * alpha + destBuffer[index] * inverseAlpha),
                    0,
                    255);
                destBuffer[index + 1] = (byte)Math.Clamp(
                    Math.Round(originalBuffer[index + 1] * alpha + destBuffer[index + 1] * inverseAlpha),
                    0,
                    255);
                destBuffer[index + 2] = (byte)Math.Clamp(
                    Math.Round(originalBuffer[index + 2] * alpha + destBuffer[index + 2] * inverseAlpha),
                    0,
                    255);
                destBuffer[index + 3] = 255;
            }

            Marshal.Copy(destBuffer, 0, destData.Scan0, destBuffer.Length);
        }
        finally
        {
            destination.UnlockBits(destData);
            original.UnlockBits(originalData);
            mask.UnlockBits(maskData);
        }
    }

    private static void ApplyExifOrientation(Image image)
    {
        const int orientationPropertyId = 0x0112;

        if (!image.PropertyIdList.Contains(orientationPropertyId))
        {
            return;
        }

        try
        {
            var property = image.GetPropertyItem(orientationPropertyId);
            if (property is null)
            {
                return;
            }

            var propertyValue = property.Value;
            if (propertyValue is null || propertyValue.Length < 2)
            {
                return;
            }

            var orientation = BitConverter.ToUInt16(propertyValue, 0);
            var rotateFlip = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone,
            };

            if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
            {
                image.RotateFlip(rotateFlip);
            }
        }
        catch
        {
            // Ignore malformed EXIF orientation data and keep the original image.
        }
    }
}

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Nuggi.Services;

namespace Nuggi.Controls;

internal sealed class MaskCanvasControl : Control
{
    private const int OverlayAlpha = 118;
    private const float MinimumZoom = 0.1f;
    private const float MaximumZoom = 16f;

    private Bitmap? _originalImage;
    private Bitmap? _maskImage;
    private Bitmap? _baselineMaskImage;
    private Bitmap? _displayOriginalImage;
    private Bitmap? _displayMaskImage;
    private Bitmap? _displayOverlayImage;
    private Rectangle _displayImageBounds;
    private bool _displayMaskDirty = true;
    private bool _isDrawing;
    private Point? _hoverImagePoint;
    private Point? _lastStrokeImagePoint;
    private float _zoom = 1f;

    public MaskCanvasControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(238, 242, 255);
        Cursor = Cursors.Cross;
        TabStop = true;
        BrushSize = 36;
        IsAddMode = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public int BrushSize { get; set; }

    public bool IsAddMode { get; set; }

    public float Zoom => _zoom;

    public bool HasImage => _originalImage is not null;

    public event EventHandler? ZoomChanged;

    public void LoadImages(Bitmap originalImage, Bitmap initialMaskImage, Bitmap baselineMaskImage)
    {
        DisposeImages();

        _originalImage = (Bitmap)originalImage.Clone();
        _maskImage = (Bitmap)initialMaskImage.Clone();
        _baselineMaskImage = (Bitmap)baselineMaskImage.Clone();
        MaskPreviewRenderer.NormalizeMaskInPlace(_maskImage);
        MaskPreviewRenderer.NormalizeMaskInPlace(_baselineMaskImage);
        _zoom = 1f;
        UpdateCanvasSize();
        InvalidateDisplayCache();
        Invalidate();
    }

    public void ResetToBaseline()
    {
        if (_maskImage is null || _baselineMaskImage is null)
        {
            return;
        }

        using var graphics = Graphics.FromImage(_maskImage);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(_baselineMaskImage, 0, 0, _maskImage.Width, _maskImage.Height);

        _displayMaskDirty = true;
        Invalidate();
    }

    public Bitmap ExportMask()
    {
        if (_maskImage is null)
        {
            throw new InvalidOperationException("Mask image is not loaded.");
        }

        return (Bitmap)_maskImage.Clone();
    }

    public void SetZoom(float zoom)
    {
        var normalizedZoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        if (Math.Abs(_zoom - normalizedZoom) < 0.0001f)
        {
            return;
        }

        _zoom = normalizedZoom;
        UpdateCanvasSize();
        InvalidateDisplayCache();
        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomBy(float factor)
    {
        if (factor <= 0f)
        {
            return;
        }

        SetZoom(_zoom * factor);
    }

    public void ZoomToFit(Size viewportSize)
    {
        if (_originalImage is null)
        {
            return;
        }

        var availableWidth = Math.Max(1, viewportSize.Width - 24);
        var availableHeight = Math.Max(1, viewportSize.Height - 24);
        var fitZoom = Math.Min(
            availableWidth / (float)_originalImage.Width,
            availableHeight / (float)_originalImage.Height);

        SetZoom(fitZoom);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.Clear(BackColor);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (_originalImage is null || _maskImage is null)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "편집할 이미지를 준비하는 중입니다.",
                Font,
                ClientRectangle,
                Color.FromArgb(71, 85, 105),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (!EnsureDisplayCache())
        {
            return;
        }

        e.Graphics.DrawImage(_displayOriginalImage!, _displayImageBounds);
        e.Graphics.DrawImage(_displayOverlayImage!, _displayImageBounds);

        using var borderPen = new Pen(Color.FromArgb(148, 163, 184), 1.5f);
        e.Graphics.DrawRectangle(borderPen, _displayImageBounds);

        DrawBrushPreview(e.Graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (!TryMapClientPointToImage(e.Location, out var imagePoint))
        {
            return;
        }

        Focus();
        _isDrawing = true;
        _hoverImagePoint = imagePoint;
        _lastStrokeImagePoint = imagePoint;
        ApplyBrushStroke(imagePoint, imagePoint);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (TryMapClientPointToImage(e.Location, out var imagePoint))
        {
            _hoverImagePoint = imagePoint;

            if (_isDrawing)
            {
                var strokeStart = _lastStrokeImagePoint ?? imagePoint;
                ApplyBrushStroke(strokeStart, imagePoint);
                _lastStrokeImagePoint = imagePoint;
            }
        }
        else
        {
            _hoverImagePoint = null;
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isDrawing)
        {
            _displayMaskDirty = true;
            Invalidate();
        }

        _isDrawing = false;
        _lastStrokeImagePoint = null;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_isDrawing)
        {
            _displayMaskDirty = true;
        }

        _isDrawing = false;
        _hoverImagePoint = null;
        _lastStrokeImagePoint = null;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        InvalidateDisplayCache();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeImages();
        }

        base.Dispose(disposing);
    }

    private bool EnsureDisplayCache()
    {
        if (_originalImage is null || _maskImage is null)
        {
            return false;
        }

        var imageRectangle = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        if (imageRectangle.Width <= 0 || imageRectangle.Height <= 0)
        {
            return false;
        }

        _displayImageBounds = imageRectangle;

        if (_displayOriginalImage is not null &&
            _displayOriginalImage.Width == imageRectangle.Width &&
            _displayOriginalImage.Height == imageRectangle.Height)
        {
            if (_displayMaskDirty || _displayMaskImage is null || _displayOverlayImage is null)
            {
                RebuildDisplayMaskAndOverlay();
            }

            return _displayMaskImage is not null && _displayOverlayImage is not null;
        }

        _displayOriginalImage?.Dispose();

        _displayOriginalImage = ResizeBitmap(_originalImage, imageRectangle.Size);
        RebuildDisplayMaskAndOverlay();
        return _displayMaskImage is not null && _displayOverlayImage is not null;
    }

    private void RebuildDisplayMaskAndOverlay()
    {
        if (_maskImage is null || _displayImageBounds.Width <= 0 || _displayImageBounds.Height <= 0)
        {
            InvalidateDisplayCache();
            return;
        }

        _displayMaskImage?.Dispose();
        _displayOverlayImage?.Dispose();
        _displayMaskImage = ResizeMaskBitmap(_maskImage, _displayImageBounds.Size);
        MaskPreviewRenderer.NormalizeMaskInPlace(_displayMaskImage);
        _displayOverlayImage = BuildOverlayFromMask(_displayMaskImage);
        _displayMaskDirty = false;
    }

    private void DrawBrushPreview(Graphics graphics)
    {
        if (_originalImage is null || _hoverImagePoint is null || _displayImageBounds.IsEmpty)
        {
            return;
        }

        var scale = Math.Min(
            _displayImageBounds.Width / (float)_originalImage.Width,
            _displayImageBounds.Height / (float)_originalImage.Height);

        var previewDiameter = Math.Max(4f, BrushSize * scale);
        var centerX = _displayImageBounds.X + (_hoverImagePoint.Value.X / (float)_originalImage.Width) * _displayImageBounds.Width;
        var centerY = _displayImageBounds.Y + (_hoverImagePoint.Value.Y / (float)_originalImage.Height) * _displayImageBounds.Height;
        var brushRectangle = new RectangleF(
            centerX - previewDiameter / 2f,
            centerY - previewDiameter / 2f,
            previewDiameter,
            previewDiameter);

        using var fillBrush = new SolidBrush(IsAddMode ? Color.FromArgb(70, 34, 197, 94) : Color.FromArgb(70, 239, 68, 68));
        using var borderPen = new Pen(IsAddMode ? Color.FromArgb(22, 163, 74) : Color.FromArgb(220, 38, 38), 1.5f);
        graphics.FillEllipse(fillBrush, brushRectangle);
        graphics.DrawEllipse(borderPen, brushRectangle);
    }

    private bool TryMapClientPointToImage(Point point, out Point imagePoint)
    {
        imagePoint = Point.Empty;

        if (_originalImage is null)
        {
            return false;
        }

        var imageRectangle = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);
        if (imageRectangle.IsEmpty || !imageRectangle.Contains(point.X, point.Y))
        {
            return false;
        }

        var normalizedX = (point.X - imageRectangle.X) / imageRectangle.Width;
        var normalizedY = (point.Y - imageRectangle.Y) / imageRectangle.Height;
        imagePoint = new Point(
            Math.Clamp((int)Math.Round(normalizedX * (_originalImage.Width - 1)), 0, _originalImage.Width - 1),
            Math.Clamp((int)Math.Round(normalizedY * (_originalImage.Height - 1)), 0, _originalImage.Height - 1));

        return true;
    }

    private void ApplyBrushStroke(Point startImagePoint, Point endImagePoint)
    {
        if (_maskImage is null)
        {
            return;
        }

        var maskColor = IsAddMode ? Color.White : Color.Black;
        DrawStroke(_maskImage, startImagePoint, endImagePoint, BrushSize, maskColor, useSourceCopy: true);
        _displayMaskDirty = true;
    }

    private static void DrawStroke(Bitmap target, Point startPoint, Point endPoint, float width, Color color, bool useSourceCopy)
    {
        DrawStroke(target, new PointF(startPoint.X, startPoint.Y), new PointF(endPoint.X, endPoint.Y), width, color, useSourceCopy);
    }

    private static void DrawStroke(Bitmap target, PointF startPoint, PointF endPoint, float width, Color color, bool useSourceCopy)
    {
        using var graphics = Graphics.FromImage(target);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.CompositingMode = useSourceCopy ? CompositingMode.SourceCopy : CompositingMode.SourceOver;

        using var pen = new Pen(color, Math.Max(1f, width))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        graphics.DrawLine(pen, startPoint, endPoint);
    }

    private static Bitmap ResizeBitmap(Image source, Size size)
    {
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, bitmap.Width, bitmap.Height);
        return bitmap;
    }

    private static Bitmap ResizeMaskBitmap(Image source, Size size)
    {
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.DrawImage(source, 0, 0, bitmap.Width, bitmap.Height);
        return bitmap;
    }

    private static Bitmap BuildOverlayFromMask(Bitmap mask)
    {
        var overlay = new Bitmap(mask.Width, mask.Height, PixelFormat.Format32bppArgb);
        UpdateOverlayFromMask(mask, overlay, new Rectangle(0, 0, mask.Width, mask.Height));
        return overlay;
    }

    private static void UpdateOverlayFromMask(Bitmap mask, Bitmap overlay, Rectangle dirtyBounds)
    {
        if (dirtyBounds.Width <= 0 || dirtyBounds.Height <= 0)
        {
            return;
        }

        var maskData = mask.LockBits(dirtyBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var overlayData = overlay.LockBits(dirtyBounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var maskBuffer = new byte[Math.Abs(maskData.Stride) * dirtyBounds.Height];
            var overlayBuffer = new byte[Math.Abs(overlayData.Stride) * dirtyBounds.Height];

            Marshal.Copy(maskData.Scan0, maskBuffer, 0, maskBuffer.Length);

            for (var y = 0; y < dirtyBounds.Height; y++)
            {
                var rowStart = y * Math.Abs(maskData.Stride);
                for (var x = 0; x < dirtyBounds.Width; x++)
                {
                    var index = rowStart + x * 4;
                    var strength = maskBuffer[index + 2] / 255f;
                    if (strength <= 0f)
                    {
                        overlayBuffer[index] = 0;
                        overlayBuffer[index + 1] = 0;
                        overlayBuffer[index + 2] = 0;
                        overlayBuffer[index + 3] = 0;
                        continue;
                    }

                    overlayBuffer[index] = 94;
                    overlayBuffer[index + 1] = 197;
                    overlayBuffer[index + 2] = 34;
                    overlayBuffer[index + 3] = (byte)Math.Clamp(
                        Math.Round(OverlayAlpha * strength),
                        0,
                        255);
                }
            }

            Marshal.Copy(overlayBuffer, 0, overlayData.Scan0, overlayBuffer.Length);
        }
        finally
        {
            mask.UnlockBits(maskData);
            overlay.UnlockBits(overlayData);
        }
    }

    private void InvalidateDisplayCache()
    {
        _displayOriginalImage?.Dispose();
        _displayMaskImage?.Dispose();
        _displayOverlayImage?.Dispose();
        _displayOriginalImage = null;
        _displayMaskImage = null;
        _displayOverlayImage = null;
        _displayImageBounds = Rectangle.Empty;
        _displayMaskDirty = true;
    }

    private void UpdateCanvasSize()
    {
        if (_originalImage is null)
        {
            Size = new Size(1, 1);
            return;
        }

        var width = Math.Max(1, (int)Math.Round(_originalImage.Width * _zoom));
        var height = Math.Max(1, (int)Math.Round(_originalImage.Height * _zoom));
        Size = new Size(width, height);
    }

    private void DisposeImages()
    {
        _originalImage?.Dispose();
        _maskImage?.Dispose();
        _baselineMaskImage?.Dispose();
        InvalidateDisplayCache();
        _originalImage = null;
        _maskImage = null;
        _baselineMaskImage = null;
        _hoverImagePoint = null;
        _lastStrokeImagePoint = null;
        _zoom = 1f;
    }
}

using Nuggi.Controls;

namespace Nuggi;

internal sealed class MaskEditorForm : Form
{
    private const string DefaultHint = "브러시: 마우스 왼쪽 버튼으로 칠합니다.  객체 클릭: 사물을 클릭하면 그 사물 영역만 추가하거나 지웁니다.  Ctrl+휠 = 확대/축소  ·  Ctrl+Z = 실행 취소";

    private readonly MaskCanvasControl _canvas = new();
    private readonly Panel _canvasHostPanel = new();
    private readonly RadioButton _brushToolRadioButton = new();
    private readonly RadioButton _objectToolRadioButton = new();
    private readonly RadioButton _addRadioButton = new();
    private readonly RadioButton _removeRadioButton = new();
    private readonly TrackBar _brushSizeTrackBar = new();
    private readonly Label _brushSizeLabel = new();
    private readonly Label _zoomLabel = new();
    private readonly Label _hintLabel = new();
    private readonly Func<Point, Task<Bitmap>> _selectObjectAsync;
    private bool _fitMode = true;

    // selectObjectAsync returns the mask of the object at an image point (Segment Anything in the Python worker).
    public MaskEditorForm(
        string fileName,
        Bitmap originalImage,
        Bitmap initialMask,
        Bitmap baselineMask,
        Func<Point, Task<Bitmap>> selectObjectAsync)
    {
        _selectObjectAsync = selectObjectAsync;
        Text = $"영역 편집 - {fileName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1200, 840);
        ClientSize = new Size(1360, 920);
        Theme.ApplyToForm(this);

        BuildUi();
        HookEvents();

        _canvas.LoadImages(originalImage, initialMask, baselineMask);
        UpdateBrushSizeLabel();
        UpdateZoomLabel();
    }

    public Bitmap? ResultMask { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ZoomToFitCanvas();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            _canvas.Undo();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ResultMask?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            BackColor = Theme.Background,
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(rootLayout);

        var toolbar = Theme.Card(new Padding(16, 12, 16, 12));
        toolbar.Margin = new Padding(0, 0, 0, 12);
        toolbar.AutoSize = true;
        toolbar.Dock = DockStyle.Fill;
        rootLayout.Controls.Add(toolbar, 0, 0);

        var toolbarLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        toolbar.Controls.Add(toolbarLayout);

        // Each toggle pair needs its own container: radio buttons in one parent form a single group.
        toolbarLayout.Controls.Add(CreateToggleGroup("도구", _brushToolRadioButton, "브러시", _objectToolRadioButton, "객체 클릭"));
        toolbarLayout.Controls.Add(CreateToggleGroup("동작", _addRadioButton, "영역 추가", _removeRadioButton, "영역 지우기"));

        toolbarLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "크기",
            Font = Theme.LabelFont,
            ForeColor = Theme.MutedText,
            Margin = new Padding(0, 9, 8, 0),
        });

        _brushSizeTrackBar.Minimum = 4;
        _brushSizeTrackBar.Maximum = 200;
        _brushSizeTrackBar.TickFrequency = 12;
        _brushSizeTrackBar.Value = 36;
        _brushSizeTrackBar.AutoSize = false;
        _brushSizeTrackBar.Width = 240;
        _brushSizeTrackBar.Height = 32;
        _brushSizeTrackBar.TickStyle = TickStyle.None;
        _brushSizeTrackBar.Margin = new Padding(0, 0, 8, 0);
        toolbarLayout.Controls.Add(_brushSizeTrackBar);

        _brushSizeLabel.AutoSize = true;
        _brushSizeLabel.Margin = new Padding(0, 9, 20, 0);
        toolbarLayout.Controls.Add(_brushSizeLabel);

        toolbarLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "확대",
            Font = Theme.LabelFont,
            ForeColor = Theme.MutedText,
            Margin = new Padding(0, 9, 8, 0),
        });

        var zoomOutButton = Theme.SecondaryButton("-", (_, _) => ChangeZoom(0.8f));
        zoomOutButton.Margin = new Padding(0, 0, 6, 0);
        toolbarLayout.Controls.Add(zoomOutButton);

        _zoomLabel.AutoSize = true;
        _zoomLabel.Margin = new Padding(0, 9, 6, 0);
        toolbarLayout.Controls.Add(_zoomLabel);

        var zoomInButton = Theme.SecondaryButton("+", (_, _) => ChangeZoom(1.25f));
        zoomInButton.Margin = new Padding(0, 0, 6, 0);
        toolbarLayout.Controls.Add(zoomInButton);

        var fitButton = Theme.SecondaryButton("맞춤", (_, _) => ZoomToFitCanvas());
        fitButton.Margin = new Padding(0, 0, 20, 0);
        toolbarLayout.Controls.Add(fitButton);

        var undoButton = Theme.SecondaryButton("실행 취소 (Ctrl+Z)", (_, _) => _canvas.Undo());
        undoButton.Margin = new Padding(0, 0, 6, 0);
        toolbarLayout.Controls.Add(undoButton);

        var clearButton = Theme.SecondaryButton("모두 지우기", (_, _) => _canvas.ClearMask());
        clearButton.Margin = new Padding(0, 0, 6, 0);
        toolbarLayout.Controls.Add(clearButton);

        var resetButton = Theme.SecondaryButton("자동 마스크로 되돌리기", (_, _) => _canvas.ResetToBaseline());
        resetButton.Margin = new Padding(0, 0, 12, 0);
        toolbarLayout.Controls.Add(resetButton);

        var canvasCard = Theme.Card(new Padding(1));
        canvasCard.Margin = new Padding(0, 0, 0, 12);
        canvasCard.Dock = DockStyle.Fill;
        rootLayout.Controls.Add(canvasCard, 0, 1);

        _canvasHostPanel.Dock = DockStyle.Fill;
        _canvasHostPanel.AutoScroll = true;
        _canvasHostPanel.BackColor = Theme.PreviewBackground;
        _canvasHostPanel.Padding = new Padding(8);
        canvasCard.Controls.Add(_canvasHostPanel);

        _canvas.Location = new Point(8, 8);
        _canvasHostPanel.Controls.Add(_canvas);

        var footer = Theme.Card(new Padding(16, 12, 16, 12));
        footer.AutoSize = true;
        footer.Dock = DockStyle.Fill;
        rootLayout.Controls.Add(footer, 0, 2);

        var footerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(footerLayout);

        _hintLabel.AutoSize = true;
        _hintLabel.Text = DefaultHint;
        _hintLabel.ForeColor = Theme.MutedText;
        _hintLabel.Font = Theme.SmallFont;
        _hintLabel.Margin = new Padding(0, 8, 0, 0);
        footerLayout.Controls.Add(_hintLabel, 0, 0);

        var actionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };

        var cancelButton = Theme.SecondaryButton("취소", (_, _) => Close());
        cancelButton.Margin = new Padding(0, 0, 8, 0);
        var applyButton = Theme.PrimaryButton("편집 적용", (_, _) => ApplyAndClose());
        applyButton.Margin = Padding.Empty;
        actionButtons.Controls.Add(cancelButton);
        actionButtons.Controls.Add(applyButton);
        footerLayout.Controls.Add(actionButtons, 1, 0);
    }

    private void HookEvents()
    {
        _brushSizeTrackBar.ValueChanged += (_, _) =>
        {
            _canvas.BrushSize = _brushSizeTrackBar.Value;
            UpdateBrushSizeLabel();
        };

        _addRadioButton.CheckedChanged += (_, _) => _canvas.IsAddMode = _addRadioButton.Checked;
        _removeRadioButton.CheckedChanged += (_, _) => _canvas.IsAddMode = !_removeRadioButton.Checked;
        _objectToolRadioButton.CheckedChanged += (_, _) =>
        {
            _canvas.IsObjectMode = _objectToolRadioButton.Checked;
            _brushSizeTrackBar.Enabled = !_objectToolRadioButton.Checked;
        };
        _canvas.ObjectClicked += async (_, imagePoint) => await SelectObjectAtAsync(imagePoint);
        _canvas.ZoomChanged += (_, _) => UpdateZoomLabel();
        _canvas.MouseWheel += (_, e) => HandleCanvasMouseWheel(e);
        _canvasHostPanel.MouseWheel += (_, e) => HandleCanvasMouseWheel(e);
        _canvasHostPanel.Resize += (_, _) =>
        {
            if (_fitMode)
            {
                ZoomToFitCanvas();
            }
        };
    }

    private void UpdateBrushSizeLabel()
    {
        _brushSizeLabel.Text = $"{_brushSizeTrackBar.Value}px";
    }

    private void UpdateZoomLabel()
    {
        _zoomLabel.Text = $"{Math.Round(_canvas.Zoom * 100)}%";
    }

    private void ChangeZoom(float factor)
    {
        ApplyZoom(_canvas.Zoom * factor, fitMode: false);
    }

    private void ZoomToFitCanvas()
    {
        if (!_canvas.HasImage)
        {
            return;
        }

        _fitMode = true;
        _canvas.ZoomToFit(_canvasHostPanel.ClientSize);
        _canvasHostPanel.AutoScrollPosition = Point.Empty;
        UpdateZoomLabel();
    }

    private void ApplyZoom(float newZoom, bool fitMode)
    {
        if (!_canvas.HasImage)
        {
            return;
        }

        _fitMode = fitMode;

        var viewportSize = _canvasHostPanel.ClientSize;
        var previousZoom = Math.Max(0.0001f, _canvas.Zoom);
        var previousScroll = _canvasHostPanel.AutoScrollPosition;
        var scrollX = -previousScroll.X;
        var scrollY = -previousScroll.Y;
        var centerX = (scrollX + viewportSize.Width / 2f) / previousZoom;
        var centerY = (scrollY + viewportSize.Height / 2f) / previousZoom;

        _canvas.SetZoom(newZoom);

        var targetScrollX = Math.Max(0, (int)Math.Round(centerX * _canvas.Zoom - viewportSize.Width / 2f));
        var targetScrollY = Math.Max(0, (int)Math.Round(centerY * _canvas.Zoom - viewportSize.Height / 2f));
        _canvasHostPanel.AutoScrollPosition = new Point(targetScrollX, targetScrollY);
    }

    private void HandleCanvasMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != Keys.Control || !_canvas.HasImage)
        {
            return;
        }

        ApplyZoom(_canvas.Zoom * (e.Delta > 0 ? 1.1f : 0.9f), fitMode: false);

        if (e is HandledMouseEventArgs handledEventArgs)
        {
            handledEventArgs.Handled = true;
        }
    }

    private void ApplyAndClose()
    {
        ResultMask?.Dispose();
        ResultMask = _canvas.ExportMask();
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task SelectObjectAtAsync(Point imagePoint)
    {
        _canvas.Enabled = false;
        UseWaitCursor = true;
        _hintLabel.Text = "클릭한 사물의 영역을 찾는 중입니다... (처음 한 번은 조금 더 걸립니다)";

        try
        {
            using var objectMask = await _selectObjectAsync(imagePoint);
            if (!IsDisposed)
            {
                _canvas.ApplyObjectMask(objectMask);
                _hintLabel.Text = DefaultHint;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _hintLabel.Text = DefaultHint;
                MessageBox.Show(this, "사물 영역을 찾지 못했습니다.\n\n" + ex.Message, "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                _canvas.Enabled = true;
            }
        }
    }

    private static Control CreateToggleGroup(string title, RadioButton first, string firstText, RadioButton second, string secondText)
    {
        var group = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 24, 0) };
        group.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            Font = Theme.LabelFont,
            ForeColor = Theme.MutedText,
            Margin = new Padding(0, 9, 8, 0),
        });

        first.Text = firstText;
        first.Checked = true;
        Theme.StyleToggle(first);
        first.Margin = new Padding(0, 2, 0, 0);
        group.Controls.Add(first);

        second.Text = secondText;
        Theme.StyleToggle(second);
        second.Margin = new Padding(0, 2, 0, 0);
        group.Controls.Add(second);
        return group;
    }

}

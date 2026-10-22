using Nuggi.Controls;

namespace Nuggi;

internal sealed class MaskEditorForm : Form
{
    private readonly MaskCanvasControl _canvas = new();
    private readonly Panel _canvasHostPanel = new();
    private readonly RadioButton _addRadioButton = new();
    private readonly RadioButton _removeRadioButton = new();
    private readonly TrackBar _brushSizeTrackBar = new();
    private readonly Label _brushSizeLabel = new();
    private readonly Label _zoomLabel = new();
    private bool _fitMode = true;

    public MaskEditorForm(string fileName, Bitmap originalImage, Bitmap initialMask, Bitmap baselineMask)
    {
        Text = $"영역 편집 - {fileName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1200, 840);
        ClientSize = new Size(1360, 920);
        BackColor = Color.FromArgb(243, 244, 246);

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
            BackColor = BackColor,
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(rootLayout);

        var toolbar = BuildCardPanel(new Padding(16));
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

        toolbarLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "초록색이 유지 영역입니다.",
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
            Margin = new Padding(0, 6, 20, 0),
        });

        _addRadioButton.Text = "추가 브러시";
        _addRadioButton.Checked = true;
        _addRadioButton.AutoSize = true;
        _addRadioButton.Margin = new Padding(0, 8, 12, 0);
        toolbarLayout.Controls.Add(_addRadioButton);

        _removeRadioButton.Text = "제거 브러시";
        _removeRadioButton.AutoSize = true;
        _removeRadioButton.Margin = new Padding(0, 8, 20, 0);
        toolbarLayout.Controls.Add(_removeRadioButton);

        toolbarLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "브러시 크기",
            Margin = new Padding(0, 9, 12, 0),
        });

        _brushSizeTrackBar.Minimum = 4;
        _brushSizeTrackBar.Maximum = 200;
        _brushSizeTrackBar.TickFrequency = 12;
        _brushSizeTrackBar.Value = 36;
        _brushSizeTrackBar.AutoSize = false;
        _brushSizeTrackBar.Width = 240;
        _brushSizeTrackBar.Height = 36;
        _brushSizeTrackBar.Margin = new Padding(0, 0, 8, 0);
        toolbarLayout.Controls.Add(_brushSizeTrackBar);

        _brushSizeLabel.AutoSize = true;
        _brushSizeLabel.Margin = new Padding(0, 9, 20, 0);
        toolbarLayout.Controls.Add(_brushSizeLabel);

        toolbarLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "줌",
            Margin = new Padding(0, 9, 12, 0),
        });

        var zoomOutButton = CreateButton("-", (_, _) => ChangeZoom(0.8f));
        zoomOutButton.Margin = new Padding(0, 0, 6, 0);
        toolbarLayout.Controls.Add(zoomOutButton);

        _zoomLabel.AutoSize = true;
        _zoomLabel.Margin = new Padding(0, 9, 6, 0);
        toolbarLayout.Controls.Add(_zoomLabel);

        var zoomInButton = CreateButton("+", (_, _) => ChangeZoom(1.25f));
        zoomInButton.Margin = new Padding(0, 0, 6, 0);
        toolbarLayout.Controls.Add(zoomInButton);

        var fitButton = CreateButton("맞춤", (_, _) => ZoomToFitCanvas());
        fitButton.Margin = new Padding(0, 0, 20, 0);
        toolbarLayout.Controls.Add(fitButton);

        var resetButton = CreateButton("자동 마스크로 되돌리기", (_, _) => _canvas.ResetToBaseline());
        resetButton.Margin = new Padding(0, 0, 12, 0);
        toolbarLayout.Controls.Add(resetButton);

        var canvasCard = BuildCardPanel(new Padding(12));
        canvasCard.Dock = DockStyle.Fill;
        rootLayout.Controls.Add(canvasCard, 0, 1);

        _canvasHostPanel.Dock = DockStyle.Fill;
        _canvasHostPanel.AutoScroll = true;
        _canvasHostPanel.BackColor = Color.FromArgb(238, 242, 255);
        _canvasHostPanel.Padding = new Padding(8);
        canvasCard.Controls.Add(_canvasHostPanel);

        _canvas.Location = new Point(8, 8);
        _canvasHostPanel.Controls.Add(_canvas);

        var footer = BuildCardPanel(new Padding(16));
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

        footerLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "왼쪽 마우스로 칠하면 바로 반영됩니다. Ctrl+마우스 휠로도 확대/축소할 수 있습니다.",
            ForeColor = Color.FromArgb(71, 85, 105),
            Font = new Font("Malgun Gothic", 9),
            Margin = new Padding(0, 8, 0, 0),
        }, 0, 0);

        var actionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };

        var cancelButton = CreateButton("취소", (_, _) => Close());
        var applyButton = CreatePrimaryButton("적용", (_, _) => ApplyAndClose());
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

    private static Panel BuildCardPanel(Padding padding)
    {
        return new Panel
        {
            BackColor = Color.White,
            Padding = padding,
            Margin = new Padding(0, 0, 0, 12),
        };
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(12, 8, 12, 8),
        };
        button.Click += onClick;
        return button;
    }

    private static Button CreatePrimaryButton(string text, EventHandler onClick)
    {
        var button = CreateButton(text, onClick);
        button.BackColor = Color.FromArgb(37, 99, 235);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}

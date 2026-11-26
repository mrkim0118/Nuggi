using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nuggi.Models;
using Nuggi.Services;

namespace Nuggi;

internal sealed class MainForm : Form
{
    private static readonly string[] SupportedExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".webp",
        ".tif",
        ".tiff",
    ];

    private readonly AppPaths _appPaths;
    private readonly NuggiPythonBridge _pythonBridge;
    private readonly HashSet<string> _selectedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _sessionEditedMasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _maskGenerationLock = new(1, 1);
    private readonly CancellationTokenSource _closingCts = new();
    private readonly string _sessionMaskOutputDirectory;

    private readonly ListBox _fileListBox = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly ComboBox _modelComboBox = new();
    private readonly CheckBox _trimCheckBox = new();
    private readonly Button _startButton = new();
    private readonly Button _openOutputButton = new();
    private readonly Button _editMaskButton = new();
    private readonly Button _saveMaskButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new();
    private readonly Label _selectionSummaryLabel = new();
    private readonly Label _previewStatusLabel = new();
    private readonly Label _fileListEmptyLabel = new();
    private readonly PictureBox _originalPictureBox = new();
    private readonly PictureBox _cutoutPictureBox = new();
    private readonly TextBox _logTextBox = new();
    private readonly SplitContainer _bodySplit = new();

    private bool _isProcessing;
    private int _previewRequestId;
    private Image? _originalImage;
    private Image? _cutoutImage;

    public MainForm()
    {
        _appPaths = AppPathLocator.Locate();
        _pythonBridge = new NuggiPythonBridge(_appPaths);
        _sessionMaskOutputDirectory = Path.Combine(Path.GetTempPath(), "Nuggi", "session-mask-output", Guid.NewGuid().ToString("N"));

        Text = "Nuggi";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        ClientSize = new Size(1380, 880);
        Theme.ApplyToForm(this);

        BuildUi();
        HookEvents();

        _outputDirectoryTextBox.Text = GetDefaultOutputDirectory(_appPaths.RootDirectory);
        _modelComboBox.SelectedIndex = 0;
        _trimCheckBox.Checked = true;

        TryDeleteStaleTempFiles();
        Directory.CreateDirectory(_appPaths.PreviewCacheDirectory);
        Directory.CreateDirectory(_appPaths.MaskCacheDirectory);
        Directory.CreateDirectory(_sessionMaskOutputDirectory);

        UpdateSelectionSummary();
        UpdateUiState();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _bodySplit.Panel1MinSize = 360;
        _bodySplit.Panel2MinSize = 600;
        SetSplitDistanceSafely(400);
        CheckForUpdateOnStartup();

        try
        {
            SetStatus("Python 배경 제거 엔진을 확인하는 중...");
            await _pythonBridge.EnsureRuntimeReadyAsync(_closingCts.Token);
            SetStatus("이미지를 추가하면 오른쪽에 원본과 선택 영역이 표시됩니다.");
            AppendLog("Python 배경 제거 엔진을 확인했습니다.");
        }
        catch (OperationCanceledException) when (_closingCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppendLog($"환경 확인 실패: {ex.Message}");
            SetStatus("환경 확인 실패");
            MessageBox.Show(
                this,
                "Python 배경 제거 엔진을 준비하지 못했습니다.\n\n" + ex.Message,
                "Nuggi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // Asks once per launch when GitHub has a newer release, then runs its setup and closes.
    private async void CheckForUpdateOnStartup()
    {
        var release = await GitHubUpdater.FindNewerReleaseAsync();
        if (release is null || IsDisposed)
        {
            return;
        }

        var message = $"새 버전 v{release.Version}을 사용할 수 있습니다.\n현재 버전: {GitHubUpdater.CurrentVersion}\n\n지금 다운로드하고 설치할까요?";
        if (MessageBox.Show(this, message, "업데이트 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        var originalTitle = Text;
        UseWaitCursor = true;
        try
        {
            await GitHubUpdater.DownloadAndStartSetupAsync(release, new Progress<int>(percent => Text = $"업데이트 다운로드 중... {percent}%"));
            Close();
        }
        catch (Exception ex)
        {
            Text = originalTitle;
            MessageBox.Show(this, $"업데이트 다운로드에 실패했습니다.\n{ex.Message}", "업데이트 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel)
        {
            // Stops running Python workers so they do not outlive the window.
            _closingCts.Cancel();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        DisposeSessionEditedMasks();
        TryDeleteDirectory(_sessionMaskOutputDirectory);
        ReplacePreviewImage(_originalPictureBox, ref _originalImage, null);
        ReplacePreviewImage(_cutoutPictureBox, ref _cutoutImage, null);
    }

    private void BuildUi()
    {
        Padding = new Padding(16);

        _bodySplit.Dock = DockStyle.Fill;
        _bodySplit.Orientation = Orientation.Vertical;
        _bodySplit.FixedPanel = FixedPanel.Panel1;
        _bodySplit.SplitterWidth = 16;
        _bodySplit.BackColor = Theme.Background;
        Controls.Add(_bodySplit);

        BuildLeftPanel(_bodySplit.Panel1);
        BuildRightPanel(_bodySplit.Panel2);
    }

    private void BuildLeftPanel(Control parent)
    {
        var card = Theme.Card(new Padding(20));
        card.Dock = DockStyle.Fill;
        parent.Controls.Add(card);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Theme.Surface,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        void AddRow(Control control, SizeType sizeType = SizeType.AutoSize, float height = 0)
        {
            layout.RowStyles.Add(new RowStyle(sizeType, height));
            layout.Controls.Add(control, 0, layout.RowStyles.Count - 1);
        }

        // Image list
        var listHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listHeader.Controls.Add(Theme.Title("이미지"), 0, 0);
        _selectionSummaryLabel.AutoSize = true;
        _selectionSummaryLabel.Font = Theme.SmallFont;
        _selectionSummaryLabel.ForeColor = Theme.MutedText;
        _selectionSummaryLabel.Anchor = AnchorStyles.Right;
        listHeader.Controls.Add(_selectionSummaryLabel, 1, 0);
        AddRow(listHeader);

        _fileListBox.Dock = DockStyle.Fill;
        _fileListBox.IntegralHeight = false;
        _fileListBox.BorderStyle = BorderStyle.None;
        _fileListBox.DrawMode = DrawMode.OwnerDrawFixed;
        _fileListBox.ItemHeight = 44;
        _fileListBox.DrawItem += DrawFileListItem;

        _fileListEmptyLabel.Dock = DockStyle.Fill;
        _fileListEmptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        _fileListEmptyLabel.ForeColor = Theme.MutedText;
        _fileListEmptyLabel.BackColor = Theme.Surface;
        _fileListEmptyLabel.Text = "아직 추가한 이미지가 없습니다.\n아래 [파일 추가] 또는 [폴더 추가]를 눌러 주세요.";

        var listFrame = Theme.Card(new Padding(1));
        listFrame.Dock = DockStyle.Fill;
        listFrame.Margin = Padding.Empty;
        listFrame.Controls.Add(_fileListBox);
        listFrame.Controls.Add(_fileListEmptyLabel);
        _fileListEmptyLabel.BringToFront();
        AddRow(listFrame, SizeType.Percent, 100);

        var fileButtons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
        fileButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        foreach (var (text, handler) in new (string, EventHandler)[]
                 {
                     ("파일 추가", (_, _) => AddFiles()),
                     ("폴더 추가", (_, _) => AddFolder()),
                     ("선택 제거", (_, _) => RemoveSelectedFiles()),
                     ("비우기", (_, _) => ClearFiles()),
                 })
        {
            fileButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var button = Theme.SecondaryButton(text, handler);
            button.AutoSize = false;
            button.Padding = Padding.Empty;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(fileButtons.Controls.Count == 0 ? 0 : 3, 0, fileButtons.Controls.Count == 3 ? 0 : 3, 0);
            fileButtons.Controls.Add(button);
        }

        AddRow(fileButtons);

        // Output settings
        var settingsTitle = Theme.Title("저장 설정");
        settingsTitle.Margin = new Padding(0, 20, 0, 0);
        AddRow(settingsTitle);
        AddRow(Theme.FieldLabel("저장 폴더"));

        var outputPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _outputDirectoryTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _outputDirectoryTextBox.Margin = new Padding(0, 0, 6, 0);
        outputPanel.Controls.Add(_outputDirectoryTextBox, 0, 0);
        var browseButton = Theme.SecondaryButton("변경", (_, _) => ChooseOutputDirectory());
        browseButton.Margin = Padding.Empty;
        outputPanel.Controls.Add(browseButton, 1, 0);
        AddRow(outputPanel);

        AddRow(Theme.FieldLabel("모델"));
        _modelComboBox.Dock = DockStyle.Fill;
        _modelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelComboBox.Items.AddRange(["u2net · 기본 (자동 보정 포함)", "u2netp · 가벼움 (속도 우선)"]);
        AddRow(_modelComboBox);

        _trimCheckBox.Text = "피사체에 맞춰 빈 여백 잘라내기";
        _trimCheckBox.AutoSize = true;
        _trimCheckBox.Margin = new Padding(0, 12, 0, 0);
        AddRow(_trimCheckBox);

        // Actions
        var actionButtons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 20, 0, 0) };
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        Theme.StylePrimary(_startButton);
        _startButton.Text = "누끼 추출 시작";
        _startButton.Font = new Font(Theme.BaseFont, FontStyle.Bold);
        _startButton.AutoSize = false;
        _startButton.Height = 42;
        _startButton.Dock = DockStyle.Fill;
        _startButton.Margin = new Padding(0, 0, 4, 0);
        Theme.StyleSecondary(_openOutputButton);
        _openOutputButton.Text = "결과 폴더 열기";
        _openOutputButton.AutoSize = false;
        _openOutputButton.Height = 42;
        _openOutputButton.Dock = DockStyle.Fill;
        _openOutputButton.Margin = new Padding(4, 0, 0, 0);
        actionButtons.Controls.Add(_startButton, 0, 0);
        actionButtons.Controls.Add(_openOutputButton, 1, 0);
        AddRow(actionButtons);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Height = 6;
        _progressBar.Margin = new Padding(0, 14, 0, 0);
        AddRow(_progressBar);

        _statusLabel.AutoSize = true;
        _statusLabel.Text = "준비됨";
        _statusLabel.Font = Theme.SmallFont;
        _statusLabel.ForeColor = Theme.MutedText;
        _statusLabel.Margin = new Padding(0, 6, 0, 6);
        AddRow(_statusLabel);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.ReadOnly = true;
        _logTextBox.BorderStyle = BorderStyle.None;
        _logTextBox.BackColor = Theme.PreviewBackground;
        _logTextBox.ForeColor = Theme.MutedText;
        _logTextBox.Font = Theme.SmallFont;
        AddRow(_logTextBox, SizeType.Absolute, 110);
    }

    private void BuildRightPanel(Control parent)
    {
        var card = Theme.Card(new Padding(20));
        card.Dock = DockStyle.Fill;
        parent.Controls.Add(card);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(Theme.Title("미리보기"), 0, 0);

        _previewStatusLabel.AutoSize = true;
        _previewStatusLabel.Font = Theme.SmallFont;
        _previewStatusLabel.ForeColor = Theme.MutedText;
        _previewStatusLabel.Text = "왼쪽 목록에서 이미지를 고르면 원본과 누끼 영역이 표시됩니다.";
        header.Controls.Add(_previewStatusLabel, 0, 1);

        var headerButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        Theme.StyleSecondary(_saveMaskButton);
        _saveMaskButton.Text = "마스크 저장";
        _saveMaskButton.Margin = new Padding(0, 0, 8, 0);
        headerButtons.Controls.Add(_saveMaskButton);
        Theme.StylePrimary(_editMaskButton);
        _editMaskButton.Text = "영역 편집";
        _editMaskButton.Margin = Padding.Empty;
        headerButtons.Controls.Add(_editMaskButton);
        header.Controls.Add(headerButtons, 1, 0);
        header.SetRowSpan(headerButtons, 2);
        layout.Controls.Add(header, 0, 0);

        var previewGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        previewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previewGrid.Controls.Add(CreatePreviewPane("원본", _originalPictureBox, "선택한 이미지가 여기에 표시됩니다.", new Padding(0, 0, 6, 0)), 0, 0);
        previewGrid.Controls.Add(CreatePreviewPane("누끼 영역", _cutoutPictureBox, "자동으로 찾은 피사체 영역이 여기에 표시됩니다.", new Padding(6, 0, 0, 0)), 1, 0);
        layout.Controls.Add(previewGrid, 0, 1);

        var legend = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 10, 0, 0) };
        legend.Controls.Add(CreateLegendItem(Theme.KeepArea, "유지되는 영역"));
        legend.Controls.Add(CreateLegendItem(Theme.AddedArea, "직접 추가한 영역"));
        legend.Controls.Add(CreateLegendItem(Theme.RemovedArea, "직접 지운 영역"));
        layout.Controls.Add(legend, 0, 2);
    }

    private static Control CreatePreviewPane(string title, PictureBox pictureBox, string emptyText, Padding margin)
    {
        var pane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = margin };
        pane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var caption = Theme.FieldLabel(title);
        caption.Margin = new Padding(0, 0, 0, 6);
        pane.Controls.Add(caption, 0, 0);

        pictureBox.Dock = DockStyle.Fill;
        pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        pictureBox.BackColor = Theme.PreviewBackground;
        pictureBox.Margin = Padding.Empty;
        pictureBox.Padding = new Padding(8);
        pictureBox.Paint += (_, e) =>
        {
            if (pictureBox.Image is null)
            {
                TextRenderer.DrawText(e.Graphics, emptyText, Theme.SmallFont, pictureBox.ClientRectangle, Theme.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
        };
        pane.Controls.Add(pictureBox, 0, 1);
        return pane;
    }

    private static Control CreateLegendItem(Color color, string text)
    {
        var item = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 18, 0) };
        item.Controls.Add(new Label { AutoSize = true, Text = "●", ForeColor = color, Margin = new Padding(0, 0, 2, 0) });
        var label = Theme.MutedLabel(text);
        label.Margin = new Padding(0, 1, 0, 0);
        item.Controls.Add(label);
        return item;
    }

    private void DrawFileListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _fileListBox.Items.Count)
        {
            return;
        }

        var path = (string)_fileListBox.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(selected ? Theme.Selection : Theme.Surface))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var textLeft = e.Bounds.X + 12;
        var textWidth = e.Bounds.Width - 24;
        TextRenderer.DrawText(e.Graphics, Path.GetFileName(path), Theme.BaseFont,
            new Rectangle(textLeft, e.Bounds.Y + 4, textWidth, 20), Theme.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, Path.GetDirectoryName(path), Theme.SmallFont,
            new Rectangle(textLeft, e.Bounds.Y + 23, textWidth, 18), Theme.MutedText,
            TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);

        using var divider = new Pen(Theme.Border);
        e.Graphics.DrawLine(divider, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void HookEvents()
    {
        _fileListBox.SelectedIndexChanged += async (_, _) => await LoadSelectedPreviewAsync();
        _modelComboBox.SelectedIndexChanged += async (_, _) => await LoadSelectedPreviewAsync();
        _startButton.Click += async (_, _) => await StartProcessingAsync();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();
        _editMaskButton.Click += async (_, _) => await EditCurrentMaskAsync();
        _saveMaskButton.Click += async (_, _) => await SaveCurrentMaskAsync();
    }

    private void SetSplitDistanceSafely(int preferredDistance)
    {
        if (_bodySplit.Width <= 0)
        {
            return;
        }

        var maxDistance = _bodySplit.Width - _bodySplit.Panel2MinSize - _bodySplit.SplitterWidth;
        if (maxDistance < _bodySplit.Panel1MinSize)
        {
            return;
        }

        _bodySplit.SplitterDistance = Math.Max(
            _bodySplit.Panel1MinSize,
            Math.Min(preferredDistance, maxDistance));
    }
    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff",
            Multiselect = true,
            Title = "이미지 파일 선택",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var added = MergeFiles(dialog.FileNames);
        AppendLog($"{added}개 파일을 추가했습니다.");
    }

    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "이미지가 들어 있는 폴더 선택",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var files = Directory.EnumerateFiles(dialog.SelectedPath, "*.*", SearchOption.AllDirectories)
            .Where(static path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var added = MergeFiles(files);
        AppendLog($"{added}개 파일을 폴더에서 추가했습니다.");
    }

    private int MergeFiles(IEnumerable<string> filePaths)
    {
        var addedCount = 0;

        foreach (var filePath in filePaths)
        {
            var fullPath = Path.GetFullPath(filePath);
            if (_selectedFiles.Add(fullPath))
            {
                _fileListBox.Items.Add(fullPath);
                addedCount++;
            }
        }

        UpdateSelectionSummary();
        UpdateUiState();

        if (_fileListBox.Items.Count > 0 && _fileListBox.SelectedIndex < 0)
        {
            _fileListBox.SelectedIndex = 0;
        }

        return addedCount;
    }

    private void RemoveSelectedFiles()
    {
        var selectedItems = _fileListBox.SelectedItems.Cast<string>().ToArray();
        if (selectedItems.Length == 0)
        {
            return;
        }

        foreach (var item in selectedItems)
        {
            _selectedFiles.Remove(item);
            ClearEditedMasksForFile(item);
            _fileListBox.Items.Remove(item);
        }

        UpdateSelectionSummary();
        UpdateUiState();
        AppendLog($"{selectedItems.Length}개 파일을 목록에서 제거했습니다.");

        if (_fileListBox.Items.Count > 0)
        {
            _fileListBox.SelectedIndex = 0;
        }
        else
        {
            ClearPreview();
        }
    }

    private void ClearFiles()
    {
        foreach (var filePath in _selectedFiles)
        {
            ClearEditedMasksForFile(filePath);
        }

        _selectedFiles.Clear();
        _fileListBox.Items.Clear();
        UpdateSelectionSummary();
        UpdateUiState();
        ClearPreview();
        AppendLog("파일 목록을 비웠습니다.");
    }

    private void UpdateSelectionSummary()
    {
        _selectionSummaryLabel.Text = $"{_fileListBox.Items.Count}개";
        _fileListEmptyLabel.Visible = _fileListBox.Items.Count == 0;
    }

    private void UpdateUiState()
    {
        var hasFiles = _fileListBox.Items.Count > 0;
        var hasSelection = _fileListBox.SelectedItem is string;

        _startButton.Enabled = !_isProcessing && hasFiles;
        _editMaskButton.Enabled = !_isProcessing && hasSelection;
        _saveMaskButton.Enabled = !_isProcessing && hasSelection;
        _openOutputButton.Enabled = !_isProcessing;
        _fileListBox.Enabled = !_isProcessing;
        _modelComboBox.Enabled = !_isProcessing;
        _trimCheckBox.Enabled = !_isProcessing;
        _outputDirectoryTextBox.Enabled = !_isProcessing;
    }

    private void ChooseOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "출력 폴더 선택",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_outputDirectoryTextBox.Text)
                ? _outputDirectoryTextBox.Text
                : _appPaths.RootDirectory,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private async Task LoadSelectedPreviewAsync()
    {
        if (_fileListBox.SelectedItem is not string selectedFile)
        {
            ClearPreview();
            return;
        }

        UpdateUiState();
        var requestId = ++_previewRequestId;
        var modelName = SelectedModelName;

        try
        {
            SetPreviewStatus("원본 이미지를 불러오는 중...");
            ReplacePreviewImage(_cutoutPictureBox, ref _cutoutImage, null);

            var displayPath = await GetDisplayableSourcePathAsync(selectedFile);
            if (requestId != _previewRequestId)
            {
                return;
            }

            var originalPreview = await Task.Run(() => MaskPreviewRenderer.LoadPreviewBitmap(displayPath, 1800));
            if (requestId != _previewRequestId)
            {
                originalPreview.Dispose();
                return;
            }

            ReplacePreviewImage(_originalPictureBox, ref _originalImage, originalPreview);

            var editedMaskExists = HasEditedMask(selectedFile, modelName);
            SetPreviewStatus(editedMaskExists
                ? "현재 세션에서 수정한 마스크를 불러오는 중..."
                : "자동 마스크를 준비하는 중...");

            await EnsureMaskReadyAsync(selectedFile, modelName);
            if (requestId != _previewRequestId)
            {
                return;
            }

            var selectionPreview = await Task.Run(() => BuildMainPreviewImage(selectedFile, displayPath, modelName));
            if (requestId != _previewRequestId)
            {
                selectionPreview.Dispose();
                return;
            }

            ReplacePreviewImage(_cutoutPictureBox, ref _cutoutImage, selectionPreview);
            SetPreviewStatus(editedMaskExists
                ? "초록색은 현재 유지 영역이고, 빨간색은 이번 실행에서 직접 제거한 부분입니다."
                : "초록색으로 보이는 영역이 현재 누끼로 유지될 부분입니다.");
        }
        catch (OperationCanceledException) when (_closingCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestId != _previewRequestId)
            {
                return;
            }

            ReplacePreviewImage(_cutoutPictureBox, ref _cutoutImage, null);
            SetPreviewStatus("미리보기를 만들지 못했습니다. 왼쪽 아래 로그에서 원인을 확인해 주세요.");
            AppendLog($"미리보기 오류: {Path.GetFileName(selectedFile)} - {ex.Message}");
        }
        finally
        {
            if (requestId == _previewRequestId && !IsDisposed)
            {
                UpdateUiState();
            }
        }
    }

    private Image BuildMainPreviewImage(string inputFilePath, string displayPath, string modelName)
    {
        using var original = MaskPreviewRenderer.LoadSourceBitmap(displayPath);
        using var currentMask = LoadEffectiveMaskBitmap(inputFilePath, modelName);
        Bitmap? baselineMask = null;

        try
        {
            if (HasEditedMask(inputFilePath, modelName))
            {
                baselineMask = MaskPreviewRenderer.LoadMaskBitmap(GetAutoMaskPath(inputFilePath, modelName));
            }

            return MaskPreviewRenderer.BuildMaskOverlayPreview(original, currentMask, baselineMask, 1800);
        }
        finally
        {
            baselineMask?.Dispose();
        }
    }

    private async Task EnsureMaskReadyAsync(string inputFilePath, string modelName)
    {
        var autoMaskPath = GetAutoMaskPath(inputFilePath, modelName);
        if (File.Exists(autoMaskPath))
        {
            return;
        }

        await _maskGenerationLock.WaitAsync(_closingCts.Token);
        try
        {
            if (File.Exists(autoMaskPath))
            {
                return;
            }

            Directory.CreateDirectory(_appPaths.MaskCacheDirectory);
            await _pythonBridge.GenerateMaskAsync(inputFilePath, modelName, autoMaskPath, _closingCts.Token);
        }
        finally
        {
            _maskGenerationLock.Release();
        }
    }
    private async Task EditCurrentMaskAsync()
    {
        if (_isProcessing)
        {
            return;
        }

        if (_fileListBox.SelectedItem is not string selectedFile)
        {
            MessageBox.Show(this, "먼저 편집할 이미지를 선택해 주세요.", "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var modelName = SelectedModelName;
        var autoMaskPath = GetAutoMaskPath(selectedFile, modelName);

        try
        {
            SetPreviewStatus("영역 편집용 마스크를 준비하는 중...");
            await EnsureMaskReadyAsync(selectedFile, modelName);

            var displayPath = await GetDisplayableSourcePathAsync(selectedFile);
            using var originalImage = await Task.Run(() => MaskPreviewRenderer.LoadSourceBitmap(displayPath));
            using var baselineMask = await Task.Run(() => MaskPreviewRenderer.LoadMaskBitmap(autoMaskPath));
            using var initialMask = GetEditedMaskClone(selectedFile, modelName) ?? (Bitmap)baselineMask.Clone();

            using var editor = new MaskEditorForm(
                Path.GetFileName(selectedFile),
                originalImage,
                initialMask,
                baselineMask,
                imagePoint => SelectObjectMaskAsync(selectedFile, imagePoint));
            if (editor.ShowDialog(this) != DialogResult.OK || editor.ResultMask is null)
            {
                SetPreviewStatus("영역 편집을 취소했습니다.");
                return;
            }

            if (MaskPreviewRenderer.AreMasksEqual(editor.ResultMask, baselineMask))
            {
                ClearEditedMask(selectedFile, modelName);
                AppendLog($"영역 편집 초기화: {Path.GetFileName(selectedFile)}");
            }
            else
            {
                SetEditedMask(selectedFile, modelName, editor.ResultMask);
                AppendLog($"영역 편집 저장: {Path.GetFileName(selectedFile)}");
            }

            await LoadSelectedPreviewAsync();
        }
        catch (OperationCanceledException) when (_closingCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppendLog($"영역 편집 오류: {Path.GetFileName(selectedFile)} - {ex.Message}");
            MessageBox.Show(this, ex.Message, "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await LoadSelectedPreviewAsync();
        }
    }

    private async Task StartProcessingAsync()
    {
        if (_isProcessing)
        {
            return;
        }

        var files = _fileListBox.Items.Cast<string>().ToArray();
        if (files.Length == 0)
        {
            MessageBox.Show(this, "먼저 처리할 이미지를 추가해 주세요.", "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var outputDirectory = _outputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            MessageBox.Show(this, "출력 폴더를 지정해 주세요.", "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _isProcessing = true;
        _progressBar.Value = 0;
        UpdateUiState();

        var modelName = SelectedModelName;
        var successCount = 0;
        var failureCount = 0;
        var jobsFilePath = Path.Combine(_sessionMaskOutputDirectory, "batch-jobs.json");

        try
        {
            Directory.CreateDirectory(outputDirectory);

            var reservedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outputPaths = files.Select(file => BuildUniqueOutputPath(outputDirectory, file, reservedOutputPaths)).ToArray();
            var jobs = files.Select((file, index) => new
            {
                input = file,
                output = outputPaths[index],
                mask = GetEffectiveMaskPathForProcessing(file, modelName),
                trim = _trimCheckBox.Checked,
            }).ToArray();
            File.WriteAllText(jobsFilePath, JsonSerializer.Serialize(jobs)); // UTF-8 without BOM

            SetStatus($"[0/{files.Length}] 모델을 불러오는 중...");
            AppendLog($"{files.Length}개 파일 처리 시작 (모델: {modelName})");

            // Progress<T> posts each line to the UI thread in order, before the await below resumes.
            IProgress<string> progress = new Progress<string>(line =>
            {
                var parts = line.Split('\t', 3);
                if (parts.Length < 2 || !int.TryParse(parts[1], out var index) || index < 0 || index >= files.Length)
                {
                    return;
                }

                if (parts[0] == "DONE")
                {
                    successCount++;
                    AppendLog($"저장 완료: {outputPaths[index]}");
                }
                else if (parts[0] == "FAIL")
                {
                    failureCount++;
                    AppendLog($"처리 실패: {files[index]} - {(parts.Length > 2 ? parts[2] : "알 수 없는 오류")}");
                }
                else
                {
                    return;
                }

                var finished = successCount + failureCount;
                _progressBar.Value = (int)Math.Round(finished * 100.0 / files.Length);
                SetStatus($"[{finished}/{files.Length}] {Path.GetFileName(files[index])} 처리 완료");
            });

            await _pythonBridge.RunBatchAsync(jobsFilePath, modelName, progress.Report, _closingCts.Token);

            SetStatus($"완료: 성공 {successCount}개, 실패 {failureCount}개");
            MessageBox.Show(
                this,
                $"누끼 추출이 완료되었습니다.\n\n성공: {successCount}/{files.Length}" +
                (failureCount > 0 ? $"\n실패: {failureCount}개 (로그 확인)" : string.Empty),
                "Nuggi",
                MessageBoxButtons.OK,
                failureCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) when (_closingCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus("처리 중 오류 발생");
            AppendLog($"처리 오류: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            TryDeleteFile(jobsFilePath);
            _isProcessing = false;
            if (!IsDisposed)
            {
                UpdateUiState();
            }
        }
    }

    private static string BuildUniqueOutputPath(string outputDirectory, string inputFilePath, ISet<string> reservedPaths, string suffix = "_nuggi")
    {
        var stem = Path.GetFileNameWithoutExtension(inputFilePath);
        var candidate = Path.Combine(outputDirectory, $"{stem}{suffix}.png");
        for (var counter = 1; File.Exists(candidate) || !reservedPaths.Add(candidate); counter++)
        {
            candidate = Path.Combine(outputDirectory, $"{stem}{suffix}_{counter}.png");
        }

        return candidate;
    }

    // Saves the selected image's current mask (hand edits included) as black background + white kept area,
    // at the original image size so it lines up with the source.
    private async Task SaveCurrentMaskAsync()
    {
        if (_isProcessing || _fileListBox.SelectedItem is not string selectedFile)
        {
            return;
        }

        var outputDirectory = _outputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            MessageBox.Show(this, "출력 폴더를 지정해 주세요.", "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var modelName = SelectedModelName;
        _saveMaskButton.Enabled = false;
        try
        {
            await EnsureMaskReadyAsync(selectedFile, modelName);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = BuildUniqueOutputPath(outputDirectory, selectedFile, new HashSet<string>(), "_mask");
            await Task.Run(() =>
            {
                using var mask = LoadEffectiveMaskBitmap(selectedFile, modelName);
                MaskPreviewRenderer.SaveMask(mask, outputPath);
            });
            AppendLog($"마스크 저장 완료: {outputPath}");
            SetStatus($"마스크를 저장했습니다: {Path.GetFileName(outputPath)}");
        }
        catch (OperationCanceledException) when (_closingCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppendLog($"마스크 저장 오류: {Path.GetFileName(selectedFile)} - {ex.Message}");
            MessageBox.Show(this, "마스크를 저장하지 못했습니다.\n\n" + ex.Message, "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                UpdateUiState();
            }
        }
    }

    private async Task<Bitmap> SelectObjectMaskAsync(string inputFilePath, Point imagePoint)
    {
        var maskPath = Path.Combine(_sessionMaskOutputDirectory, $"select-{Guid.NewGuid():N}.png");
        try
        {
            await _pythonBridge.SelectObjectAsync(inputFilePath, imagePoint, maskPath, _closingCts.Token);
            return await Task.Run(() => MaskPreviewRenderer.LoadMaskBitmap(maskPath));
        }
        finally
        {
            TryDeleteFile(maskPath);
        }
    }

    // GDI+ cannot open WebP, so the UI previews a PNG copy converted by Python.
    private async Task<string> GetDisplayableSourcePathAsync(string inputFilePath)
    {
        if (!string.Equals(Path.GetExtension(inputFilePath), ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return inputFilePath;
        }

        var convertedPath = Path.Combine(_appPaths.PreviewCacheDirectory, "sources", $"{GetCacheKey(inputFilePath, "source")}.png");
        if (!File.Exists(convertedPath))
        {
            await _pythonBridge.ConvertToPngAsync(inputFilePath, convertedPath, _closingCts.Token);
        }

        return convertedPath;
    }

    // Results go next to the app, unless the app folder is read-only (an all-users install in Program Files).
    private static string GetDefaultOutputDirectory(string rootDirectory)
    {
        var besideApp = Path.Combine(rootDirectory, "output");
        try
        {
            Directory.CreateDirectory(besideApp);
            var probe = Path.Combine(besideApp, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return besideApp;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Nuggi");
        }
    }

    private void OpenOutputDirectory()
    {
        var outputDirectory = _outputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = GetDefaultOutputDirectory(_appPaths.RootDirectory);
            _outputDirectoryTextBox.Text = outputDirectory;
        }

        Directory.CreateDirectory(outputDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = outputDirectory,
            UseShellExecute = true,
        });
    }

    private string GetAutoMaskPath(string inputFilePath, string modelName)
    {
        Directory.CreateDirectory(_appPaths.MaskCacheDirectory);
        return Path.Combine(_appPaths.MaskCacheDirectory, $"{GetCacheKey(inputFilePath, modelName)}.png");
    }

    private string GetEffectiveMaskPathForProcessing(string inputFilePath, string modelName)
    {
        var editedMask = GetEditedMaskClone(inputFilePath, modelName);
        if (editedMask is null)
        {
            return GetAutoMaskPath(inputFilePath, modelName);
        }

        using (editedMask)
        {
            Directory.CreateDirectory(_sessionMaskOutputDirectory);
            var maskPath = Path.Combine(_sessionMaskOutputDirectory, $"{GetCacheKey(inputFilePath, modelName)}.png");
            MaskPreviewRenderer.SaveMask(editedMask, maskPath);
            return maskPath;
        }
    }

    private Bitmap LoadEffectiveMaskBitmap(string inputFilePath, string modelName)
    {
        return GetEditedMaskClone(inputFilePath, modelName)
            ?? MaskPreviewRenderer.LoadMaskBitmap(GetAutoMaskPath(inputFilePath, modelName));
    }
    private bool HasEditedMask(string inputFilePath, string modelName)
    {
        return _sessionEditedMasks.ContainsKey(GetSessionMaskKey(inputFilePath, modelName));
    }

    private Bitmap? GetEditedMaskClone(string inputFilePath, string modelName)
    {
        return _sessionEditedMasks.TryGetValue(GetSessionMaskKey(inputFilePath, modelName), out var mask)
            ? (Bitmap)mask.Clone()
            : null;
    }

    private void SetEditedMask(string inputFilePath, string modelName, Bitmap editedMask)
    {
        var key = GetSessionMaskKey(inputFilePath, modelName);
        if (_sessionEditedMasks.TryGetValue(key, out var existingMask))
        {
            existingMask.Dispose();
        }

        _sessionEditedMasks[key] = (Bitmap)editedMask.Clone();
    }

    private void ClearEditedMask(string inputFilePath, string modelName)
    {
        var key = GetSessionMaskKey(inputFilePath, modelName);
        if (_sessionEditedMasks.Remove(key, out var existingMask))
        {
            existingMask.Dispose();
        }
    }

    private void ClearEditedMasksForFile(string inputFilePath)
    {
        ClearEditedMask(inputFilePath, "u2net");
        ClearEditedMask(inputFilePath, "u2netp");
    }

    private static void ReplacePreviewImage(PictureBox pictureBox, ref Image? field, Image? newImage)
    {
        var oldImage = field;
        field = newImage;
        pictureBox.Image = newImage;
        oldImage?.Dispose();
    }

    private void ClearPreview()
    {
        _previewRequestId++;
        ReplacePreviewImage(_originalPictureBox, ref _originalImage, null);
        ReplacePreviewImage(_cutoutPictureBox, ref _cutoutImage, null);
        SetPreviewStatus("왼쪽 목록에서 이미지를 고르면 원본과 누끼 영역이 표시됩니다.");
        UpdateUiState();
    }

    private string SelectedModelName => _modelComboBox.SelectedIndex == 1 ? "u2netp" : "u2net";

    private static string GetCacheKey(string inputFilePath, string modelName)
    {
        var fileInfo = new FileInfo(inputFilePath);
        var cacheKey = $"mask-strategy-v2|{modelName}|{fileInfo.FullName}|{fileInfo.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
    }

    private static string GetSessionMaskKey(string inputFilePath, string modelName)
    {
        var fileInfo = new FileInfo(inputFilePath);
        return $"{modelName}|{fileInfo.FullName}|{fileInfo.LastWriteTimeUtc.Ticks}";
    }

    // Cache files are keyed by path and timestamp, so old ones are never reused; drop anything older than 30 days.
    private void TryDeleteStaleTempFiles()
    {
        var tempRoot = Path.GetDirectoryName(_appPaths.PreviewCacheDirectory)!;
        if (!Directory.Exists(tempRoot))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-30);
        try
        {
            foreach (var file in Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    TryDeleteFile(file);
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"오래된 임시 파일 정리 실패: {ex.Message}");
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private void DisposeSessionEditedMasks()
    {
        foreach (var mask in _sessionEditedMasks.Values)
        {
            mask.Dispose();
        }

        _sessionEditedMasks.Clear();
    }

    private void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
            // Ignore best-effort temp cleanup failures.
        }
    }

    private void AppendLog(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private void SetPreviewStatus(string message)
    {
        _previewStatusLabel.Text = message;
    }
}

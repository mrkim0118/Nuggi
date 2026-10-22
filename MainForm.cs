using System.Security.Cryptography;
using System.Text;
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
    private readonly string _sessionMaskOutputDirectory;

    private readonly ListBox _fileListBox = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly ComboBox _modelComboBox = new();
    private readonly CheckBox _trimCheckBox = new();
    private readonly Button _startButton = new();
    private readonly Button _openOutputButton = new();
    private readonly Button _editMaskButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new();
    private readonly Label _selectionSummaryLabel = new();
    private readonly Label _previewStatusLabel = new();
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

        Text = "Nuggi - WinForms";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1240, 780);
        ClientSize = new Size(1380, 900);

        BuildUi();
        HookEvents();

        _outputDirectoryTextBox.Text = Path.Combine(_appPaths.RootDirectory, "output");
        _modelComboBox.SelectedIndex = 0;
        _trimCheckBox.Checked = true;

        TryClearLegacyEditedMaskCache();
        Directory.CreateDirectory(_appPaths.PreviewCacheDirectory);
        Directory.CreateDirectory(_appPaths.MaskCacheDirectory);
        Directory.CreateDirectory(_sessionMaskOutputDirectory);

        UpdateSelectionSummary();
        UpdateUiState();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _bodySplit.Panel1MinSize = 380;
        _bodySplit.Panel2MinSize = 720;
        SetSplitDistanceSafely(410);

        try
        {
            SetStatus("Python 배경 제거 엔진을 확인하는 중...");
            await _pythonBridge.EnsureRuntimeReadyAsync();
            SetStatus("이미지를 추가하면 오른쪽에 원본과 선택 영역이 표시됩니다.");
            AppendLog("Python 배경 제거 엔진을 확인했습니다.");
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _maskGenerationLock.Dispose();
        DisposeSessionEditedMasks();
        TryDeleteDirectory(_sessionMaskOutputDirectory);
        ReplacePreviewImage(_originalPictureBox, ref _originalImage, null);
        ReplacePreviewImage(_cutoutPictureBox, ref _cutoutImage, null);
    }

    private void BuildUi()
    {
        BackColor = Color.FromArgb(243, 244, 246);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16),
            BackColor = BackColor,
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(rootLayout);

        var headerPanel = BuildCardPanel(new Padding(18));
        headerPanel.Height = 92;
        headerPanel.Dock = DockStyle.Top;

        headerPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Nuggi",
            Font = new Font("Malgun Gothic", 20, FontStyle.Bold),
            Location = new Point(18, 12),
        });
        headerPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "C# WinForms에서 미리보기 확인, 영역 편집, 배경 제거 저장까지 한 번에 처리합니다.",
            Font = new Font("Malgun Gothic", 10),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(20, 52),
        });
        rootLayout.Controls.Add(headerPanel, 0, 0);

        _bodySplit.Dock = DockStyle.Fill;
        _bodySplit.Orientation = Orientation.Vertical;
        _bodySplit.FixedPanel = FixedPanel.Panel1;
        rootLayout.Controls.Add(_bodySplit, 0, 1);

        BuildLeftPanel(_bodySplit.Panel1);
        BuildRightPanel(_bodySplit.Panel2);
    }

    private void BuildLeftPanel(Control parent)
    {
        var leftCard = BuildCardPanel(new Padding(16));
        leftCard.Dock = DockStyle.Fill;
        parent.Controls.Add(leftCard);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            BackColor = leftCard.BackColor,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        leftCard.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "파일 목록",
            Font = new Font("Malgun Gothic", 12, FontStyle.Bold),
        }, 0, 0);

        _selectionSummaryLabel.AutoSize = true;
        _selectionSummaryLabel.Font = new Font("Malgun Gothic", 9);
        _selectionSummaryLabel.ForeColor = Color.FromArgb(71, 85, 105);
        layout.Controls.Add(_selectionSummaryLabel, 0, 1);

        _fileListBox.Dock = DockStyle.Fill;
        _fileListBox.HorizontalScrollbar = true;
        _fileListBox.IntegralHeight = false;
        _fileListBox.Font = new Font("Consolas", 10);
        layout.Controls.Add(_fileListBox, 0, 2);

        var fileButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 12, 0, 0),
        };
        fileButtons.Controls.Add(CreateButton("이미지 추가", (_, _) => AddFiles()));
        fileButtons.Controls.Add(CreateButton("폴더 추가", (_, _) => AddFolder()));
        fileButtons.Controls.Add(CreateButton("선택 제거", (_, _) => RemoveSelectedFiles()));
        fileButtons.Controls.Add(CreateButton("전체 비우기", (_, _) => ClearFiles()));
        layout.Controls.Add(fileButtons, 0, 3);
        layout.Controls.Add(CreateFieldLabel("출력 폴더"), 0, 4);

        var outputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        };
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _outputDirectoryTextBox.Dock = DockStyle.Fill;
        outputPanel.Controls.Add(_outputDirectoryTextBox, 0, 0);
        outputPanel.Controls.Add(CreateButton("찾아보기", (_, _) => ChooseOutputDirectory()), 1, 0);
        layout.Controls.Add(outputPanel, 0, 5);

        layout.Controls.Add(CreateFieldLabel("모델"), 0, 6);

        _modelComboBox.Dock = DockStyle.Top;
        _modelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelComboBox.Items.AddRange(["u2net (기본 / 자동 보정 포함)", "u2netp (가벼움 / 속도 우선)"]);
        layout.Controls.Add(_modelComboBox, 0, 7);

        _trimCheckBox.Text = "피사체 기준으로 여백 자르기";
        _trimCheckBox.AutoSize = true;
        _trimCheckBox.Margin = new Padding(0, 12, 0, 0);
        layout.Controls.Add(_trimCheckBox, 0, 8);

        var actionButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 12, 0, 0),
        };

        ConfigurePrimaryButton(_startButton, "누끼 추출 시작");
        _openOutputButton.Text = "결과 폴더 열기";
        _openOutputButton.AutoSize = true;
        _openOutputButton.Padding = new Padding(10, 8, 10, 8);

        actionButtons.Controls.Add(_startButton);
        actionButtons.Controls.Add(_openOutputButton);
        layout.Controls.Add(actionButtons, 0, 9);

        var bottomPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 12, 0, 0),
        };
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _progressBar.Dock = DockStyle.Top;
        bottomPanel.Controls.Add(_progressBar, 0, 0);

        _statusLabel.AutoSize = true;
        _statusLabel.Text = "준비됨";
        _statusLabel.Font = new Font("Malgun Gothic", 9);
        _statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        _statusLabel.Margin = new Padding(0, 8, 0, 8);
        bottomPanel.Controls.Add(_statusLabel, 0, 1);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.ReadOnly = true;
        _logTextBox.BackColor = Color.White;
        _logTextBox.Font = new Font("Consolas", 9);
        bottomPanel.Controls.Add(_logTextBox, 0, 2);
        layout.Controls.Add(bottomPanel, 0, 10);
    }

    private void BuildRightPanel(Control parent)
    {
        var rightCard = BuildCardPanel(new Padding(16));
        rightCard.Dock = DockStyle.Fill;
        parent.Controls.Add(rightCard);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = rightCard.BackColor,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightCard.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "미리보기",
            Font = new Font("Malgun Gothic", 12, FontStyle.Bold),
        }, 0, 0);

        _previewStatusLabel.AutoSize = true;
        _previewStatusLabel.Text = "파일을 선택하면 원본과 누끼 선택 영역이 표시됩니다.";
        _previewStatusLabel.Font = new Font("Malgun Gothic", 9);
        _previewStatusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        _previewStatusLabel.Margin = new Padding(0, 6, 0, 10);
        layout.Controls.Add(_previewStatusLabel, 0, 1);

        var previewToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 12),
        };
        ConfigurePrimaryButton(_editMaskButton, "영역 편집");
        previewToolbar.Controls.Add(_editMaskButton);
        previewToolbar.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "초록색은 유지 영역이고, 편집 후 빨간색은 제거한 부분입니다.",
            ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(8, 10, 0, 0),
        });
        layout.Controls.Add(previewToolbar, 0, 2);

        var previewGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        previewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.Controls.Add(previewGrid, 0, 3);

        previewGrid.Controls.Add(CreatePreviewGroup("원본 이미지", _originalPictureBox), 0, 0);
        previewGrid.Controls.Add(CreatePreviewGroup("선택 영역 표시", _cutoutPictureBox), 1, 0);
    }

    private void HookEvents()
    {
        _fileListBox.SelectedIndexChanged += async (_, _) => await LoadSelectedPreviewAsync();
        _modelComboBox.SelectedIndexChanged += async (_, _) => await ReloadPreviewForCurrentSelectionAsync();
        _startButton.Click += async (_, _) => await StartProcessingAsync();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();
        _editMaskButton.Click += async (_, _) => await EditCurrentMaskAsync();
    }

    private GroupBox CreatePreviewGroup(string title, PictureBox pictureBox)
    {
        var groupBox = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
        };

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(238, 242, 255),
            Padding = new Padding(8),
        };

        pictureBox.Dock = DockStyle.Fill;
        pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        pictureBox.BackColor = panel.BackColor;

        panel.Controls.Add(pictureBox);
        groupBox.Controls.Add(panel);
        return groupBox;
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

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            Font = new Font("Malgun Gothic", 9, FontStyle.Bold),
            Margin = new Padding(0, 12, 0, 0),
        };
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(10, 8, 10, 8),
        };
        button.Click += onClick;
        return button;
    }

    private static void ConfigurePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.BackColor = Color.FromArgb(37, 99, 235);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Padding = new Padding(12, 8, 12, 8);
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
        _selectionSummaryLabel.Text = $"준비한 파일: {_fileListBox.Items.Count}개";
    }

    private void UpdateUiState()
    {
        var hasFiles = _fileListBox.Items.Count > 0;
        var hasSelection = _fileListBox.SelectedItem is string;

        _startButton.Enabled = !_isProcessing && hasFiles;
        _editMaskButton.Enabled = !_isProcessing && hasSelection;
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

    private async Task ReloadPreviewForCurrentSelectionAsync()
    {
        await LoadSelectedPreviewAsync();
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

            var originalPreview = await Task.Run(() => MaskPreviewRenderer.LoadPreviewBitmap(selectedFile, 1800));
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

            var selectionPreview = await Task.Run(() => BuildMainPreviewImage(selectedFile, modelName));
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
        catch (Exception ex)
        {
            if (requestId != _previewRequestId)
            {
                return;
            }

            ReplacePreviewImage(_cutoutPictureBox, ref _cutoutImage, null);
            SetPreviewStatus("미리보기 생성 실패");
            AppendLog($"미리보기 오류: {Path.GetFileName(selectedFile)} - {ex.Message}");
        }
        finally
        {
            if (requestId == _previewRequestId)
            {
                UpdateUiState();
            }
        }
    }

    private Image BuildMainPreviewImage(string inputFilePath, string modelName)
    {
        using var original = MaskPreviewRenderer.LoadSourceBitmap(inputFilePath);
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

        await _maskGenerationLock.WaitAsync();
        try
        {
            if (File.Exists(autoMaskPath))
            {
                return;
            }

            Directory.CreateDirectory(_appPaths.MaskCacheDirectory);
            await _pythonBridge.GenerateMaskAsync(inputFilePath, modelName, autoMaskPath);
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

            using var originalImage = await Task.Run(() => MaskPreviewRenderer.LoadSourceBitmap(selectedFile));
            using var baselineMask = await Task.Run(() => MaskPreviewRenderer.LoadMaskBitmap(autoMaskPath));
            using var initialMask = GetEditedMaskClone(selectedFile, modelName) ?? (Bitmap)baselineMask.Clone();

            using var editor = new MaskEditorForm(Path.GetFileName(selectedFile), originalImage, initialMask, baselineMask);
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

        Directory.CreateDirectory(outputDirectory);

        _isProcessing = true;
        _progressBar.Value = 0;
        UpdateUiState();

        var successCount = 0;
        var modelName = SelectedModelName;

        try
        {
            for (var index = 0; index < files.Length; index++)
            {
                var filePath = files[index];
                var outputPath = BuildUniqueOutputPath(outputDirectory, filePath);

                SetStatus($"[{index + 1}/{files.Length}] {Path.GetFileName(filePath)} 처리 중...");
                AppendLog($"처리 시작: {filePath}");

                await EnsureMaskReadyAsync(filePath, modelName);

                await _pythonBridge.RemoveBackgroundAsync(
                    filePath,
                    outputPath,
                    modelName,
                    _trimCheckBox.Checked,
                    GetEffectiveMaskPathForProcessing(filePath, modelName));

                successCount++;
                _progressBar.Value = (int)Math.Round((index + 1) * 100.0 / files.Length);
                AppendLog($"저장 완료: {outputPath}");
            }

            SetStatus($"완료: {successCount}/{files.Length}개 성공");
            MessageBox.Show(
                this,
                $"누끼 추출이 완료되었습니다.\n\n성공: {successCount}/{files.Length}",
                "Nuggi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus("처리 중 오류 발생");
            AppendLog($"처리 오류: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Nuggi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isProcessing = false;
            UpdateUiState();
        }
    }

    private string BuildUniqueOutputPath(string outputDirectory, string inputFilePath)
    {
        var stem = Path.GetFileNameWithoutExtension(inputFilePath);
        var candidate = Path.Combine(outputDirectory, $"{stem}_nuggi.png");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var counter = 1;
        while (true)
        {
            candidate = Path.Combine(outputDirectory, $"{stem}_nuggi_{counter}.png");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }

    private void OpenOutputDirectory()
    {
        var outputDirectory = _outputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Path.Combine(_appPaths.RootDirectory, "output");
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
        SetPreviewStatus("파일을 선택하면 원본과 누끼 선택 영역이 표시됩니다.");
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

    private void TryClearLegacyEditedMaskCache()
    {
        try
        {
            if (Directory.Exists(_appPaths.MaskEditCacheDirectory))
            {
                Directory.Delete(_appPaths.MaskEditCacheDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"이전 영역 편집 캐시 정리 실패: {ex.Message}");
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

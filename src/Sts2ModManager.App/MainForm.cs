using System.Diagnostics;
using System.Text.Json;
using Sts2ModManager.Core;

namespace Sts2ModManager.App;

public sealed class MainForm : Form
{
    private readonly ModScanner _scanner = new();
    private readonly ModSelectionService _selectionService = new();
    private readonly SettingsSaveStore _settingsStore = new();
    private readonly StartupProfileStore _startupProfileStore = new();
    private readonly SettingsPathResolver _settingsPathResolver = new();
    private readonly GameLauncher _gameLauncher = new();

    private readonly ListView _modList = new();
    private readonly Label _modCountLabel = new();
    private readonly Label _nameLabel = new();
    private readonly Label _idLabel = new();
    private readonly Label _versionLabel = new();
    private readonly Label _authorLabel = new();
    private readonly Label _sourceLabel = new();
    private readonly TextBox _descriptionBox = new();
    private readonly Label _gamePathLabel = new();
    private readonly Label _settingsPathLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Button _saveButton = new();
    private readonly Button _launchButton = new();

    private List<ModEntry> _mods = new();
    private string? _gameDirectory;
    private string? _modsDirectory;
    private string? _settingsPath;
    private bool _updatingList;

    public MainForm()
    {
        Text = "ModTheSpire 2 - Slay the Spire 2 Mod Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1120, 720);
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        Shown += (_, _) => InitializeFromDisk();
    }

    private void BuildUi()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 540,
            Panel1MinSize = 390,
            Panel2MinSize = 340
        };
        Controls.Add(split);

        BuildListPanel(split.Panel1);
        BuildDetailsPanel(split.Panel2);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(10, 8, 10, 8),
            ColumnCount = 3,
            RowCount = 3
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(bottom);

        bottom.Controls.Add(new Label { Text = "游戏目录", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        _gamePathLabel.AutoEllipsis = true;
        _gamePathLabel.Dock = DockStyle.Fill;
        bottom.Controls.Add(_gamePathLabel, 1, 0);
        var chooseGameButton = new Button { Text = "选择游戏目录", AutoSize = true };
        chooseGameButton.Click += (_, _) => ChooseGameDirectory();
        bottom.Controls.Add(chooseGameButton, 2, 0);

        bottom.Controls.Add(new Label { Text = "设置文件", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);
        _settingsPathLabel.AutoEllipsis = true;
        _settingsPathLabel.Dock = DockStyle.Fill;
        bottom.Controls.Add(_settingsPathLabel, 1, 1);
        var chooseSettingsButton = new Button { Text = "选择 settings.save", AutoSize = true };
        chooseSettingsButton.Click += (_, _) => ChooseSettingsFile();
        bottom.Controls.Add(chooseSettingsButton, 2, 1);

        _statusLabel.AutoEllipsis = true;
        _statusLabel.Dock = DockStyle.Fill;
        bottom.Controls.Add(_statusLabel, 0, 2);
        bottom.SetColumnSpan(_statusLabel, 1);
        var actionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Right
        };
        var refreshButton = new Button { Text = "刷新", AutoSize = true };
        refreshButton.Click += (_, _) => ReloadMods();
        _saveButton.Text = "保存配置";
        _saveButton.AutoSize = true;
        _saveButton.Click += (_, _) => SaveConfiguration(showSuccessMessage: true);
        _launchButton.Text = "保存并启动游戏";
        _launchButton.AutoSize = true;
        _launchButton.Click += (_, _) => SaveAndLaunch();
        actionPanel.Controls.Add(refreshButton);
        actionPanel.Controls.Add(_saveButton);
        actionPanel.Controls.Add(_launchButton);
        bottom.Controls.Add(actionPanel, 2, 2);
        bottom.SetColumnSpan(actionPanel, 1);

        UpdatePathLabels();
    }

    private void BuildListPanel(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            RowCount = 3,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parent.Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "已安装模组", Font = new Font(Font, FontStyle.Bold), AutoSize = true }, 0, 0);
        _modCountLabel.Text = "0 个模组";
        _modCountLabel.AutoSize = true;
        _modCountLabel.Anchor = AnchorStyles.Right;
        header.Controls.Add(_modCountLabel, 1, 0);
        layout.Controls.Add(header, 0, 0);

        _modList.Dock = DockStyle.Fill;
        _modList.View = View.Details;
        _modList.CheckBoxes = true;
        _modList.FullRowSelect = true;
        _modList.HideSelection = false;
        _modList.MultiSelect = false;
        _modList.AllowDrop = true;
        _modList.GridLines = true;
        _modList.Columns.Add("模组", 250);
        _modList.Columns.Add("顺序", 55);
        _modList.Columns.Add("版本", 90);
        _modList.Columns.Add("来源", 125);
        _modList.ItemChecked += ModListItemChecked;
        _modList.SelectedIndexChanged += (_, _) => UpdateDetails();
        _modList.ItemDrag += ModListItemDrag;
        _modList.DragEnter += ModListDragEnter;
        _modList.DragOver += ModListDragOver;
        _modList.DragDrop += ModListDragDrop;
        layout.Controls.Add(_modList, 0, 1);

        var listActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        AddActionButton(listActions, "全选", (_, _) => SetAllEnabled(true));
        AddActionButton(listActions, "全不选", (_, _) => SetAllEnabled(false));
        AddActionButton(listActions, "上移", (_, _) => MoveSelected(-1));
        AddActionButton(listActions, "下移", (_, _) => MoveSelected(1));
        AddActionButton(listActions, "置顶", (_, _) => MoveSelectedTo(0));
        AddActionButton(listActions, "置底", (_, _) => MoveSelectedTo(_mods.Count - 1));
        AddActionButton(listActions, "打开 mods", (_, _) => OpenModsDirectory());
        layout.Controls.Add(listActions, 0, 2);
    }

    private void BuildDetailsPanel(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 2,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parent.Controls.Add(layout);

        var title = new Label { Text = "模组详情", Font = new Font(Font, FontStyle.Bold), AutoSize = true };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        AddDetailRow(layout, "名称", _nameLabel, 1);
        AddDetailRow(layout, "ID", _idLabel, 2);
        AddDetailRow(layout, "版本", _versionLabel, 3);
        AddDetailRow(layout, "作者", _authorLabel, 4);
        AddDetailRow(layout, "来源", _sourceLabel, 5);

        _descriptionBox.Multiline = true;
        _descriptionBox.ReadOnly = true;
        _descriptionBox.ScrollBars = ScrollBars.Vertical;
        _descriptionBox.Dock = DockStyle.Fill;
        _descriptionBox.BackColor = SystemColors.Window;
        layout.Controls.Add(new Label { Text = "描述", Anchor = AnchorStyles.Top | AnchorStyles.Left, AutoSize = true }, 0, 6);
        layout.Controls.Add(_descriptionBox, 1, 6);
    }

    private static void AddDetailRow(TableLayoutPanel layout, string caption, Label value, int row)
    {
        value.AutoEllipsis = true;
        value.Dock = DockStyle.Fill;
        value.MinimumSize = new Size(100, 24);
        layout.Controls.Add(new Label { Text = caption, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private static void AddActionButton(FlowLayoutPanel panel, string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += handler;
        panel.Controls.Add(button);
    }

    private void InitializeFromDisk()
    {
        _gameDirectory = FindGameDirectory(AppContext.BaseDirectory);
        _settingsPath = _settingsPathResolver.FindExisting(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _modsDirectory = _gameDirectory is null ? null : Path.Combine(_gameDirectory, "mods");
        ReloadMods();
    }

    private void ReloadMods()
    {
        if (_modsDirectory is null)
        {
            _mods = new List<ModEntry>();
            RenderModList();
            _statusLabel.Text = "请选择游戏目录。";
            UpdatePathLabels();
            return;
        }

        try
        {
            var scan = _scanner.Scan(_modsDirectory);
            var saved = _settingsPath is null ? Array.Empty<SavedModState>() : _settingsStore.Read(_settingsPath);
            _mods = _selectionService.Merge(scan.Mods, saved).ToList();
            RenderModList();
            var issueText = scan.Issues.Count == 0 ? string.Empty : $"，{scan.Issues.Count} 个 manifest 问题";
            _statusLabel.Text = $"已扫描 {_mods.Count} 个模组{issueText}。拖拽列表项可调整加载顺序。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _statusLabel.Text = $"读取配置失败：{ex.Message}";
        }

        UpdatePathLabels();
    }

    private void RenderModList(string? selectedKey = null)
    {
        _updatingList = true;
        try
        {
            _modList.BeginUpdate();
            _modList.Items.Clear();
            foreach (var (mod, index) in _mods.Select((mod, index) => (mod, index)))
            {
                var item = new ListViewItem(mod.Name)
                {
                    Checked = mod.IsEnabled,
                    Tag = mod,
                    ToolTipText = string.IsNullOrWhiteSpace(mod.Description) ? mod.Id : mod.Description
                };
                item.SubItems.Add((index + 1).ToString());
                item.SubItems.Add(mod.Version);
                item.SubItems.Add(mod.Source);
                _modList.Items.Add(item);
                if (selectedKey == mod.Key)
                {
                    item.Selected = true;
                }
            }

            _modCountLabel.Text = $"{_mods.Count} 个模组";
        }
        finally
        {
            _modList.EndUpdate();
            _updatingList = false;
        }

        UpdateDetails();
    }

    private void ModListItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_updatingList || e.Item.Tag is not ModEntry mod)
        {
            return;
        }

        var index = _mods.FindIndex(candidate => ReferenceEquals(candidate, mod));
        if (index >= 0)
        {
            _mods[index] = mod.WithEnabled(e.Item.Checked);
            e.Item.Tag = _mods[index];
        }
    }

    private void ModListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is ListViewItem item)
        {
            _modList.DoDragDrop(item, DragDropEffects.Move);
        }
    }

    private static void ModListDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(ListViewItem)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private static void ModListDragOver(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(ListViewItem)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void ModListDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(ListViewItem)) is not ListViewItem dragged)
        {
            return;
        }

        var oldIndex = dragged.Index;
        var point = _modList.PointToClient(new Point(e.X, e.Y));
        var target = _modList.GetItemAt(point.X, point.Y);
        var insertionIndex = target?.Index ?? _modList.Items.Count;
        if (target is not null && point.Y > target.Bounds.Top + target.Bounds.Height / 2)
        {
            insertionIndex++;
        }

        insertionIndex = Math.Clamp(insertionIndex, 0, _mods.Count);
        var newIndex = insertionIndex > oldIndex ? insertionIndex - 1 : insertionIndex;
        if (oldIndex == newIndex)
        {
            return;
        }

        var moved = _mods[oldIndex];
        _mods = ModOrderService.MoveToInsertionIndex(_mods, oldIndex, insertionIndex).ToList();
        RenderModList(moved.Key);
    }

    private void SetAllEnabled(bool enabled)
    {
        _mods = _mods.Select(mod => mod.WithEnabled(enabled)).ToList();
        RenderModList(_modList.SelectedItems.Count == 0 ? null : ((ModEntry)_modList.SelectedItems[0].Tag!).Key);
    }

    private void MoveSelected(int delta)
    {
        if (_modList.SelectedItems.Count == 0)
        {
            return;
        }

        var index = _modList.SelectedItems[0].Index;
        MoveSelectedTo(Math.Clamp(index + delta, 0, _mods.Count - 1));
    }

    private void MoveSelectedTo(int targetIndex)
    {
        if (_modList.SelectedItems.Count == 0 || _mods.Count == 0)
        {
            return;
        }

        var currentIndex = _modList.SelectedItems[0].Index;
        targetIndex = Math.Clamp(targetIndex, 0, _mods.Count - 1);
        if (currentIndex == targetIndex)
        {
            return;
        }

        var moved = _mods[currentIndex];
        _mods.RemoveAt(currentIndex);
        _mods.Insert(targetIndex, moved);
        RenderModList(moved.Key);
    }

    private void UpdateDetails()
    {
        if (_modList.SelectedItems.Count == 0 || _modList.SelectedItems[0].Tag is not ModEntry mod)
        {
            _nameLabel.Text = "—";
            _idLabel.Text = "—";
            _versionLabel.Text = "—";
            _authorLabel.Text = "—";
            _sourceLabel.Text = "—";
            _descriptionBox.Text = "选择一个模组查看 manifest 详情。";
            return;
        }

        _nameLabel.Text = mod.Name;
        _idLabel.Text = mod.Id;
        _versionLabel.Text = mod.Version;
        _authorLabel.Text = mod.Author;
        _sourceLabel.Text = mod.Source;
        _descriptionBox.Text = string.IsNullOrWhiteSpace(mod.Description) ? "（没有描述）" : mod.Description;
    }

    private bool SaveConfiguration(bool showSuccessMessage)
    {
        if (_settingsPath is null)
        {
            MessageBox.Show(
                this,
                "尚未找到 settings.save。请先运行过一次游戏，或使用“选择 settings.save”指定当前 Steam 账号的设置文件。",
                "无法保存",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        try
        {
            _settingsStore.Save(_settingsPath, _mods);
            var profilePath = _startupProfileStore.Save(_settingsPath, _mods);
            _statusLabel.Text = $"配置已保存，下次启动游戏时生效（Bootstrap：{Path.GetFileName(profilePath)}）。";
            if (showSuccessMessage)
            {
                MessageBox.Show(this, "Mod 启停状态和加载顺序已保存。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void SaveAndLaunch()
    {
        if (_gameDirectory is null)
        {
            MessageBox.Show(this, "请先选择包含 SlayTheSpire2.exe 的游戏目录。", "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!SaveConfiguration(showSuccessMessage: false))
        {
            return;
        }

        try
        {
            _gameLauncher.Launch(_gameDirectory);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, $"启动游戏失败：{ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChooseGameDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 SlayTheSpire2.exe 的游戏目录",
            UseDescriptionForTitle = true,
            SelectedPath = _gameDirectory ?? string.Empty
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!File.Exists(Path.Combine(dialog.SelectedPath, "SlayTheSpire2.exe")))
        {
            MessageBox.Show(this, "所选目录中没有找到 SlayTheSpire2.exe。", "目录无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _gameDirectory = dialog.SelectedPath;
        _modsDirectory = Path.Combine(_gameDirectory, "mods");
        ReloadMods();
    }

    private void ChooseSettingsFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择当前 Steam 账号的 settings.save",
            Filter = "Slay the Spire 2 设置|settings.save|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settingsPath = dialog.FileName;
        ReloadMods();
    }

    private void OpenModsDirectory()
    {
        if (_modsDirectory is null)
        {
            return;
        }

        Directory.CreateDirectory(_modsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_modsDirectory}\"",
            UseShellExecute = true
        });
    }

    private void UpdatePathLabels()
    {
        _gamePathLabel.Text = _gameDirectory ?? "未找到，请选择游戏目录";
        _settingsPathLabel.Text = _settingsPath ?? "未找到，请选择 settings.save";
        var canSave = _settingsPath is not null;
        _saveButton.Enabled = canSave;
        _launchButton.Enabled = canSave && _gameDirectory is not null;
    }

    private static string? FindGameDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SlayTheSpire2.exe")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

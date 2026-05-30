using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;

namespace duk.App;

record TabModel(string Title, string Path, string Content, string Language, bool IsModified);

public partial class MainWindow : Window
{
    private bool _sidebarVisible = true;
    private bool _bottomVisible  = false;
    private string? _currentFolderPath;

    private readonly List<TabModel> _tabs = [];
    private int _activeTab = -1;

    private readonly List<TerminalSession> _terminals = [];
    private int _activeTerminal = -1;

    private readonly List<(string Label, Action Action)> _commands = [];

    internal AppSettings Settings = AppSettings.Load();
    private FoldingManager? _foldingManager;
    private ExtensionLoader? _extLoader;
    internal LspManager? Lsp;
    private CompletionPopup? _completionPopup;
    private readonly Dictionary<string, List<duk.Core.Lsp.Diagnostic>> _diagnostics = [];
    private System.Windows.Threading.DispatcherTimer? _changeTimer;

    // 外部公開プロパティ（拡張機能API用）
    public string? CurrentFilePath   => _activeTab >= 0 ? _tabs[_activeTab].Path  : null;
    public string? CurrentFolderPath => _currentFolderPath;

    public MainWindow()
    {
        InitializeComponent();
        SearchPanel.Install(Editor);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCursorPosition();
        Editor.TextArea.TextEntering += Editor_TextEntering;
        Editor.TextChanged += Editor_TextChanged;
        ApplySettings();
        BuildCommandList();
        InitLsp();
        InitCompletionPopup();
        OpenNewTab("Untitled", "", "", "Plain Text");
        LoadExtensions();
        Closed += (_, _) => { Lsp?.Dispose(); _extLoader?.UnloadAll(); };
    }

    private void InitLsp()
    {
        Lsp = new LspManager(this);
        Lsp.DiagnosticsUpdated += (_, e) => Dispatcher.Invoke(() => UpdateDiagnostics(e.FilePath, e.Diagnostics));

        // テキスト変更後300msでLSPに通知（タイプ中は待つ）
        _changeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _changeTimer.Tick += async (_, _) =>
        {
            _changeTimer.Stop();
            var path = CurrentFilePath;
            if (path != null && path != "")
                await Lsp.NotifyChangedAsync(path, Editor.Text);
        };
    }

    private void InitCompletionPopup()
    {
        _completionPopup = new CompletionPopup { PlacementTarget = Editor };
        _completionPopup.ItemAccepted += OnCompletionAccepted;

        // 文字入力でLSPにchangeを通知＋補完トリガー
        Editor.TextArea.TextEntered += async (_, e) =>
        {
            _changeTimer?.Stop();
            _changeTimer?.Start();

            // . や ( 等のトリガー文字で補完を表示
            if (e.Text is "." or "(" or "<" or " ")
                await ShowCompletionsAsync();
        };

        // Ctrl+Space で手動補完
        Editor.TextArea.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            {
                await ShowCompletionsAsync();
                e.Handled = true;
            }
            else if (_completionPopup.IsVisible)
            {
                if (e.Key == Key.Down)  { _completionPopup.SelectNext(); e.Handled = true; }
                if (e.Key == Key.Up)    { _completionPopup.SelectPrev(); e.Handled = true; }
                if (e.Key == Key.Enter || e.Key == Key.Tab)
                    { _completionPopup.AcceptSelected(); e.Handled = true; }
                if (e.Key == Key.Escape){ _completionPopup.Hide(); e.Handled = true; }
            }
        };

        // Hover（マウスオーバーで型情報表示）
        Editor.MouseHover += async (_, e) =>
        {
            var pos = e.GetPosition(Editor.TextArea);
            var hit = Editor.TextArea.TextView.GetVisualPosition(
                Editor.TextArea.Caret.Position,
                ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineTop);
            var caret = Editor.TextArea.Caret;
            await ShowHoverAsync(caret.Line - 1, caret.Column - 1);
        };
    }

    private async Task ShowCompletionsAsync()
    {
        var path = CurrentFilePath;
        if (path == null || path == "" || Lsp == null) return;
        if (!Lsp.HasServer(path)) return;

        var caret = Editor.TextArea.Caret;
        var list  = await Lsp.GetCompletionsAsync(path, caret.Line - 1, caret.Column - 1);
        if (list == null || list.Items.Length == 0) return;

        // カーソル位置をスクリーン座標に変換
        var rect = Editor.TextArea.Caret.CalculateCaretRectangle();
        var pt   = Editor.PointToScreen(new Point(rect.Left, rect.Bottom));

        _completionPopup?.Show(list.Items, pt.X, pt.Y);
    }

    private void OnCompletionAccepted(object? sender, duk.Core.Lsp.CompletionItem item)
    {
        var text = item.InsertText ?? item.Label;
        // 現在の単語を置換
        var offset = Editor.TextArea.Caret.Offset;
        var doc    = Editor.Document;
        var line   = doc.GetLineByOffset(offset);
        var lineText = doc.GetText(line.Offset, offset - line.Offset);
        var wordStart = lineText.LastIndexOfAny([' ', '.', '(', '<', '\t']) + 1;
        var replaceStart = line.Offset + wordStart;
        var replaceLen   = offset - replaceStart;
        doc.Replace(replaceStart, replaceLen, text);
        Editor.Focus();
    }

    private async Task ShowHoverAsync(int line, int col)
    {
        var path = CurrentFilePath;
        if (path == null || path == "" || Lsp == null) return;
        var hover = await Lsp.GetHoverAsync(path, line, col);
        if (hover == null) return;
        var text = hover.GetText();
        if (!string.IsNullOrEmpty(text))
            ShowLspInfo(text);
    }

    // ========== 診断（エラー・警告）==========

    private void UpdateDiagnostics(string filePath, duk.Core.Lsp.Diagnostic[] diags)
    {
        _diagnostics[filePath] = [.. diags];
        UpdateStatusErrors();
        UpdateProblemsPanel();
    }

    private void UpdateStatusErrors()
    {
        var allDiags = _diagnostics.Values.SelectMany(d => d).ToList();
        var errors   = allDiags.Count(d => d.IsError);
        var warnings = allDiags.Count(d => d.IsWarning);
        // ステータスバーのエラー数更新
        Dispatcher.Invoke(() =>
        {
            // StatusBar の ✕ と ⚠ を更新（XAMLのTextBlockをコードから更新）
        });
    }

    private void UpdateProblemsPanel()
    {
        if (ProblemsPanel == null) return;
        ProblemsPanel.Child = BuildProblemsView();
    }

    private UIElement BuildProblemsView()
    {
        var all = _diagnostics
            .SelectMany(kv => kv.Value.Select(d => (kv.Key, d)))
            .OrderByDescending(x => x.d.IsError)
            .ToList();

        if (all.Count == 0)
            return new TextBlock
            {
                Text = "問題は検出されていません",
                Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
                FontSize = 13, FontFamily = new FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };

        var panel = new StackPanel { Margin = new Thickness(4) };
        foreach (var (filePath, d) in all)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text      = d.IsError ? "✕" : "⚠",
                Foreground = d.IsError
                    ? new SolidColorBrush(Color.FromRgb(0xf1, 0x4c, 0x4c))
                    : new SolidColorBrush(Color.FromRgb(0xe5, 0xc0, 0x7b)),
                FontSize  = 13, Width = 20
            });
            row.Children.Add(new TextBlock
            {
                Text      = $"{d.Message}  ({Path.GetFileName(filePath)}:{d.Range.Start.Line + 1})",
                Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
                FontSize   = 12, FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            });
            // クリックで該当行に移動
            var captured = (filePath, d);
            row.MouseLeftButtonDown += (_, _) =>
            {
                if (captured.filePath == CurrentFilePath)
                {
                    Editor.TextArea.Caret.Line   = captured.d.Range.Start.Line + 1;
                    Editor.TextArea.Caret.Column = captured.d.Range.Start.Character + 1;
                    Editor.ScrollToLine(captured.d.Range.Start.Line + 1);
                    Editor.Focus();
                }
                else OpenFileByPath(captured.filePath);
            };
            panel.Children.Add(row);
        }
        return panel;
    }

    // ========== LSP通知ヘルパー ==========

    public IEnumerable<duk.Core.Lsp.Diagnostic> GetDiagnosticsForFile(string path) =>
        _diagnostics.GetValueOrDefault(path) ?? [];

    public void ShowLspWarning(string msg) =>
        Dispatcher.Invoke(() => StatusSession.Text = $"⚠ {msg.Split('\n')[0]}");

    public void ShowLspInfo(string msg) =>
        Dispatcher.Invoke(() => StatusSession.Text = msg.Length > 60 ? msg[..60] + "..." : msg);

    private void LoadExtensions()
    {
        _extLoader = new ExtensionLoader();
        var ctx = new ExtensionContext(this);
        _extLoader.LoadAll(ctx);
    }

    // 拡張機能からコマンド・メニューを登録するAPI
    public void RegisterExtensionCommand(string label, Action handler) =>
        _commands.Add(($"[拡張] {label}", handler));

    public void RegisterExtensionMenuItem(string menu, string label, Action handler)
    {
        // 将来: メニューに動的追加
    }

    // ========== 設定適用 ==========

    internal void ApplySettings()
    {
        Editor.FontFamily      = new FontFamily(Settings.FontFamily);
        Editor.FontSize        = Settings.FontSize;
        Editor.ShowLineNumbers = Settings.ShowLineNumbers;
        Editor.WordWrap        = Settings.WordWrap;

        // コード折りたたみ
        if (Settings.CodeFolding && _foldingManager == null)
        {
            _foldingManager = FoldingManager.Install(Editor.TextArea);
            UpdateFolding();
        }
        else if (!Settings.CodeFolding && _foldingManager != null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        // ターミナルフォント
        TerminalOutput.FontFamily = new FontFamily(Settings.TerminalFontFamily);
        TerminalOutput.FontSize   = Settings.TerminalFontSize;
        TerminalInput.FontFamily  = new FontFamily(Settings.TerminalFontFamily);
        TerminalInput.FontSize    = Settings.TerminalFontSize;

        StatusEol.Text = Settings.DefaultEol;
    }

    private void UpdateFolding()
    {
        if (_foldingManager == null) return;
        try
        {
            var strategy = new BraceFoldingStrategy();
            strategy.UpdateFoldings(_foldingManager, Editor.Document);
        }
        catch { }
    }

    // ========== 自動括弧補完 ==========

    private static readonly Dictionary<char, char> BracketPairs = new()
    {
        { '(', ')' }, { '{', '}' }, { '[', ']' }, { '"', '"' }, { '\'', '\'' }
    };

    private void Editor_TextEntering(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (!Settings.AutoCloseBrackets || e.Text.Length != 1) return;
        var ch = e.Text[0];
        if (!BracketPairs.TryGetValue(ch, out var close)) return;

        var offset = Editor.TextArea.Caret.Offset;
        Editor.Document.Insert(offset, close.ToString());
        Editor.TextArea.Caret.Offset = offset;
    }

    private void Editor_TextChanged(object sender, EventArgs e)
    {
        if (_activeTab >= 0 && _activeTab < _tabs.Count)
        {
            _tabs[_activeTab] = _tabs[_activeTab] with { IsModified = true };
            RenderTabs();
        }
        if (Settings.AutoSave && _activeTab >= 0)
        {
            var t = _tabs[_activeTab];
            if (t.Path != "") SaveToPath(t.Path);
        }
        UpdateFolding();
    }

    // ========== タブ管理 ==========

    private void OpenNewTab(string title, string path, string content, string lang)
    {
        if (path != "" && _tabs.FindIndex(t => t.Path == path) is int idx && idx >= 0)
        {
            SwitchTab(idx);
            return;
        }

        SaveCurrentTabContent();
        var tab = new TabModel(title, path, content, lang, false);
        _tabs.Add(tab);
        _activeTab = _tabs.Count - 1;
        RenderTabs();
        LoadTabContent(_activeTab);
    }

    private void SwitchTab(int index)
    {
        SaveCurrentTabContent();
        _activeTab = index;
        RenderTabs();
        LoadTabContent(_activeTab);
    }

    private void SaveCurrentTabContent()
    {
        if (_activeTab < 0 || _activeTab >= _tabs.Count) return;
        var t = _tabs[_activeTab];
        _tabs[_activeTab] = t with { Content = Editor.Text };
    }

    private void LoadTabContent(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        var t = _tabs[index];
        Editor.Text = t.Content;
        Editor.SyntaxHighlighting = GetHighlighting(t.Language);
        StatusLang.Text = t.Language;
        Title = $"duk - {t.Title}";
        Editor.TextArea.Caret.Offset = 0;
        _completionPopup?.Hide();

        // LSPにドキュメントオープンを通知
        if (t.Path != "" && Lsp != null)
            _ = Lsp.NotifyOpenedAsync(t.Path, t.Content);
    }

    private void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _tabs.RemoveAt(index);
        if (_tabs.Count == 0)
        {
            OpenNewTab("Untitled", "", "", "Plain Text");
            return;
        }
        _activeTab = Math.Min(index, _tabs.Count - 1);
        RenderTabs();
        LoadTabContent(_activeTab);
    }

    private void RenderTabs()
    {
        TabBar.Children.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            int captured = i;
            var tab = _tabs[i];
            bool isActive = i == _activeTab;

            var border = new Border
            {
                Background = isActive ? new SolidColorBrush(Color.FromRgb(0x0d, 0x0d, 0x0d))
                                      : new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xd4))
                                       : Brushes.Transparent,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            border.MouseLeftButtonDown += (_, _) => SwitchTab(captured);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var title = new TextBlock
            {
                Text = tab.IsModified ? $"● {tab.Title}" : tab.Title,
                Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var close = new TextBlock
            {
                Text = "  ×",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0)
            };
            close.MouseLeftButtonDown += (s, e) => { e.Handled = true; CloseTab(captured); };

            panel.Children.Add(title);
            panel.Children.Add(close);
            border.Child = panel;
            TabBar.Children.Add(border);
        }
    }

    // ========== ファイル操作 ==========

    private void NewFile_Click(object sender, RoutedEventArgs e) =>
        OpenNewTab("Untitled", "", "", "Plain Text");

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "すべてのファイル (*.*)|*.*|C# (*.cs)|*.cs|XAML (*.xaml)|*.xaml|テキスト (*.txt)|*.txt|Markdown (*.md)|*.md",
            FilterIndex = 1
        };
        if (dlg.ShowDialog() != true) return;
        OpenFileByPath(dlg.FileName);
    }

    public void OpenFileByPath(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var ext = Path.GetExtension(path).ToLower();
            var lang = ExtToLang(ext);
            OpenNewTab(Path.GetFileName(path), path, content, lang);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ファイルを開けませんでした:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab < 0) return;
        SaveCurrentTabContent();
        var t = _tabs[_activeTab];
        if (t.Path != "")
            SaveToPath(t.Path);
        else
            SaveFileAs_Click(sender, e);
    }

    private void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "すべてのファイル (*.*)|*.*|C# (*.cs)|*.cs|テキスト (*.txt)|*.txt",
            FilterIndex = 1
        };
        if (dlg.ShowDialog() != true) return;
        SaveToPath(dlg.FileName);
    }

    private void SaveToPath(string path)
    {
        try
        {
            SaveCurrentTabContent();
            File.WriteAllText(path, _tabs[_activeTab].Content);
            var ext = Path.GetExtension(path).ToLower();
            _tabs[_activeTab] = _tabs[_activeTab] with
            {
                Path = path,
                Title = Path.GetFileName(path),
                Language = ExtToLang(ext),
                IsModified = false
            };
            RenderTabs();
            StatusLang.Text = _tabs[_activeTab].Language;
            Title = $"duk - {_tabs[_activeTab].Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存できませんでした:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "フォルダを選択",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
        LoadFolder(dlg.SelectedPath);
    }

    private void LoadFolder(string folderPath)
    {
        _currentFolderPath = folderPath;
        FolderName.Text = Path.GetFileName(folderPath).ToUpper();
        FolderName.Foreground = new SolidColorBrush(Color.FromRgb(0xbb, 0xbb, 0xbb));
        FileTree.Children.Clear();
        BuildFileTree(folderPath, 0);
        if (_terminals.Count == 0) AddTerminal(folderPath);
    }

    private readonly HashSet<string> _expandedFolders = [];

    private void BuildFileTree(string folder, int depth)
    {
        var indent = depth * 16 + 8;

        foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d))
        {
            string dirName = Path.GetFileName(dir);
            bool expanded = _expandedFolders.Contains(dir);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand };
            row.Children.Add(new TextBlock { Width = indent });
            row.Children.Add(new TextBlock
            {
                Text = expanded ? "▾" : "▸",
                Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
                FontSize = 11, Width = 14, VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = "📁 ",
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = dirName,
                Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
                FontSize = 13, FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var btn = new Button { Style = (Style)FindResource("FileBtn"), Tag = dir, Content = row };
            btn.Click += (_, _) =>
            {
                if (_expandedFolders.Contains(dir)) _expandedFolders.Remove(dir);
                else _expandedFolders.Add(dir);
                FileTree.Children.Clear();
                BuildFileTree(_currentFolderPath!, 0);
            };
            btn.ContextMenu = BuildFolderContextMenu(dir);
            FileTree.Children.Add(btn);

            if (expanded)
                BuildFileTree(dir, depth + 1);
        }

        foreach (var file in Directory.GetFiles(folder).OrderBy(f => f))
        {
            var ext = Path.GetExtension(file).ToLower();
            var (icon, iconColor) = ExtToIcon(ext);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Width = indent + 14 });
            row.Children.Add(new TextBlock
            {
                Text = icon + " ",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconColor)),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(file),
                Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
                FontSize = 13, FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var btn = new Button { Style = (Style)FindResource("FileBtn"), Tag = file, Content = row };
            btn.Click += (_, _) => OpenFileByPath((string)btn.Tag);
            btn.ContextMenu = BuildFileContextMenu(file);
            FileTree.Children.Add(btn);
        }
    }

    // ========== ファイルツリー右クリックメニュー ==========

    private ContextMenu BuildFileContextMenu(string filePath)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, string gesture = "")
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("開く",                     () => OpenFileByPath(filePath));
        Add("横に分割して開く",         () => OpenFileByPath(filePath));
        menu.Items.Add(new Separator());
        Add("新しいファイル...",        () => NewFileInFolder(Path.GetDirectoryName(filePath)!));
        Add("新しいフォルダー...",      () => NewFolderInFolder(Path.GetDirectoryName(filePath)!));
        menu.Items.Add(new Separator());
        Add("名前の変更...",            () => RenameItem(filePath),         "F2");
        Add("削除",                     () => DeleteItem(filePath),          "Del");
        menu.Items.Add(new Separator());
        Add("パスのコピー",             () => Clipboard.SetText(filePath),   "Ctrl+Shift+C");
        Add("相対パスのコピー",         () => Clipboard.SetText(GetRelativePath(filePath)));
        menu.Items.Add(new Separator());
        Add("エクスプローラーで表示",   () => Process.Start("explorer", $"/select,\"{filePath}\""));
        Add("ターミナルで開く",         () => OpenTerminalAt(Path.GetDirectoryName(filePath)!));
        return menu;
    }

    private ContextMenu BuildFolderContextMenu(string folderPath)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, string gesture = "")
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("新しいファイル...",        () => NewFileInFolder(folderPath));
        Add("新しいフォルダー...",      () => NewFolderInFolder(folderPath));
        menu.Items.Add(new Separator());
        Add("名前の変更...",            () => RenameItem(folderPath),        "F2");
        Add("削除",                     () => DeleteItem(folderPath),         "Del");
        menu.Items.Add(new Separator());
        Add("パスのコピー",             () => Clipboard.SetText(folderPath),  "Ctrl+Shift+C");
        menu.Items.Add(new Separator());
        Add("エクスプローラーで表示",   () => Process.Start("explorer", $"\"{folderPath}\""));
        Add("ターミナルで開く",         () => OpenTerminalAt(folderPath));
        Add("統合ターミナルで開く",     () => AddTerminal(folderPath));
        return menu;
    }

    // ファイル/フォルダ操作ヘルパー

    private void NewFileInFolder(string folder)
    {
        var dlg = new InputDialog("新しいファイル", "ファイル名を入力:", "newfile.cs") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var path = Path.Combine(folder, dlg.Result);
        try
        {
            File.WriteAllText(path, "");
            RefreshFileTree();
            OpenFileByPath(path);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "エラー"); }
    }

    private void NewFolderInFolder(string folder)
    {
        var dlg = new InputDialog("新しいフォルダー", "フォルダー名を入力:", "NewFolder") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, dlg.Result));
            RefreshFileTree();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "エラー"); }
    }

    private void RenameItem(string path)
    {
        var isDir = Directory.Exists(path);
        var current = isDir ? Path.GetFileName(path) : Path.GetFileName(path);
        var dlg = new InputDialog("名前の変更", "新しい名前:", current) { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var newPath = Path.Combine(Path.GetDirectoryName(path)!, dlg.Result);
        try
        {
            if (isDir) Directory.Move(path, newPath);
            else       File.Move(path, newPath);

            // 開いているタブのパスを更新
            for (int i = 0; i < _tabs.Count; i++)
                if (_tabs[i].Path == path)
                    _tabs[i] = _tabs[i] with { Path = newPath, Title = dlg.Result };
            RenderTabs();
            RefreshFileTree();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "エラー"); }
    }

    private void DeleteItem(string path)
    {
        var isDir = Directory.Exists(path);
        var name  = Path.GetFileName(path);
        var msg   = isDir ? $"フォルダー「{name}」を削除しますか？\n（中身も含めてすべて削除されます）"
                          : $"ファイル「{name}」を削除しますか？";
        if (MessageBox.Show(msg, "削除の確認",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            if (isDir) Directory.Delete(path, recursive: true);
            else       File.Delete(path);

            // 開いているタブを閉じる
            for (int i = _tabs.Count - 1; i >= 0; i--)
                if (_tabs[i].Path == path || _tabs[i].Path.StartsWith(path + Path.DirectorySeparatorChar))
                    CloseTab(i);
            RefreshFileTree();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "エラー"); }
    }

    private void OpenTerminalAt(string folder)
    {
        // 既存ターミナルがあればcdで移動、なければ新規
        if (_terminals.Count > 0 && _activeTerminal >= 0)
        {
            _terminals[_activeTerminal].SendCommand($"cd \"{folder}\"");
            TogglePanel(true);
        }
        else
        {
            AddTerminal(folder);
        }
    }

    private void RefreshFileTree()
    {
        if (_currentFolderPath == null) return;
        FileTree.Children.Clear();
        BuildFileTree(_currentFolderPath, 0);
    }

    private string GetRelativePath(string path)
    {
        if (_currentFolderPath == null) return path;
        return Path.GetRelativePath(_currentFolderPath, path);
    }

    // ========== ターミナル ==========

    private void AddTerminal(string workDir, string? shell = null)
    {
        shell ??= Settings.Shell;
        try
        {
            var session = new TerminalSession(shell, workDir, this);
            _terminals.Add(session);
            _activeTerminal = _terminals.Count - 1;
            UpdateTerminalTabBar();
            SwitchTerminal(_activeTerminal);
            if (!_bottomVisible)
            {
                _bottomVisible = true;
                BottomPanelRow.Height = new GridLength(200);
            }
            PanelTab_Terminal(this, new RoutedEventArgs());
            TerminalInput.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ターミナルを起動できませんでした:\n{shell}\n\n{ex.Message}",
                "ターミナルエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal void RenderTerminalTabs() => UpdateTerminalTabBar();

    private void UpdateTerminalTabBar()
    {
        if (TerminalTabBar == null) return;
        TerminalTabBar.Children.Clear();
        for (int i = 0; i < _terminals.Count; i++)
        {
            int idx = i;
            var t = _terminals[i];
            var border = new Border
            {
                BorderBrush = i == _activeTerminal
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xd4))
                    : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(10, 4, 6, 4),
                Cursor = Cursors.Hand
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = t.ShellName,
                Foreground = i == _activeTerminal
                    ? new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc))
                    : new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            });
            var closeBtn = new TextBlock
            {
                Text = " ×",
                Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            closeBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; CloseTerminal(idx); };
            sp.Children.Add(closeBtn);
            border.Child = sp;
            border.MouseLeftButtonDown += (_, _) => SwitchTerminal(idx);
            TerminalTabBar.Children.Add(border);
        }
    }

    private void SwitchTerminal(int idx)
    {
        if (idx < 0 || idx >= _terminals.Count) return;
        _activeTerminal = idx;
        TerminalInput.Text = "";
        UpdateTerminalTabBar();
        SwitchTerminalDisplay();
        TerminalInput.Focus();
    }

    private void CloseTerminal(int idx)
    {
        if (idx < 0 || idx >= _terminals.Count) return;
        _terminals[idx].Kill();
        _terminals.RemoveAt(idx);
        if (_terminals.Count == 0) { TogglePanel(false); return; }
        _activeTerminal = Math.Min(idx, _terminals.Count - 1);
        SwitchTerminal(_activeTerminal);
    }

    internal void AppendToTerminal(TerminalSession session, string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_activeTerminal < 0 || _activeTerminal >= _terminals.Count) return;
            if (_terminals[_activeTerminal] != session) return;

            var para = (System.Windows.Documents.Paragraph)TerminalOutput.Document.Blocks.LastBlock
                       ?? new System.Windows.Documents.Paragraph();
            if (TerminalOutput.Document.Blocks.Count == 0)
                TerminalOutput.Document.Blocks.Add(para);

            AnsiColorParser.AppendAnsiText(para, text);
            TerminalOutput.ScrollToEnd();
        });
    }

    private void ClearTerminalOutput()
    {
        TerminalOutput.Document.Blocks.Clear();
        TerminalOutput.Document.Blocks.Add(new System.Windows.Documents.Paragraph());
        AnsiColorParser.Reset();
    }

    private void SwitchTerminalDisplay()
    {
        TerminalOutput.Document.Blocks.Clear();
        AnsiColorParser.Reset();
        if (_activeTerminal < 0 || _activeTerminal >= _terminals.Count) return;

        var para = new System.Windows.Documents.Paragraph();
        TerminalOutput.Document.Blocks.Add(para);
        AnsiColorParser.AppendAnsiText(para, _terminals[_activeTerminal].OutputBuffer);
        TerminalOutput.ScrollToEnd();
    }

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (_activeTerminal < 0 || _activeTerminal >= _terminals.Count) return;
        var session = _terminals[_activeTerminal];

        if (e.Key == Key.Enter)
        {
            var cmd = TerminalInput.Text;
            TerminalInput.Clear();
            session.SendCommand(cmd);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            TerminalInput.Text = session.HistoryUp();
            TerminalInput.CaretIndex = TerminalInput.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            TerminalInput.Text = session.HistoryDown();
            TerminalInput.CaretIndex = TerminalInput.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            session.SendCtrlC();
            e.Handled = true;
        }
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            session.ClearOutput();
            ClearTerminalOutput();
            e.Handled = true;
        }
    }

    // ========== メニュー ==========

    private void Menu_File_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(sender,
            ("新しいテキストファイル",  "Ctrl+N",          (Action)(() => NewFile_Click(sender, e))),
            (null, null, null),
            ("ファイルを開く...",        "Ctrl+O",          () => OpenFile_Click(sender, e)),
            ("フォルダーを開く...",      "Ctrl+K Ctrl+O",   () => OpenFolder_Click(sender, e)),
            (null, null, null),
            ("保存",                     "Ctrl+S",          () => SaveFile_Click(sender, e)),
            ("名前を付けて保存...",      "Ctrl+Shift+S",    () => SaveFileAs_Click(sender, e)),
            (null, null, null),
            ("エディターを閉じる",       "Ctrl+F4",         () => CloseTab(_activeTab)),
            ("ウィンドウを閉じる",       "Alt+F4",          () => Close())
        );
    }

    private void Menu_Edit_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(sender,
            ("元に戻す",         "Ctrl+Z",   () => Editor.Undo()),
            ("やり直し",         "Ctrl+Y",   () => Editor.Redo()),
            (null, null, null),
            ("切り取り",         "Ctrl+X",   () => Editor.Cut()),
            ("コピー",           "Ctrl+C",   () => Editor.Copy()),
            ("貼り付け",         "Ctrl+V",   () => Editor.Paste()),
            ("削除",             "Del",      () => Editor.Delete()),
            (null, null, null),
            ("すべて選択",       "Ctrl+A",   () => Editor.SelectAll()),
            (null, null, null),
            ("検索...",          "Ctrl+F",   () => Find_Click(sender, e)),
            ("置換...",          "Ctrl+H",   () => Replace_Click(sender, e)),
            (null, null, null),
            ("行に移動...",      "Ctrl+G",   () => GoToLine_Click(sender, e)),
            ("コメントのトグル", "Ctrl+/",   () => ToggleComment_Click(sender, e))
        );
    }

    private void Menu_Selection_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(sender,
            ("すべて選択",           "Ctrl+A",        () => Editor.SelectAll()),
            ("行を選択",             "Ctrl+L",        () => SelectLine()),
            (null, null, null),
            ("カーソルを上に追加",   "Ctrl+Alt+Up",   null),
            ("カーソルを下に追加",   "Ctrl+Alt+Down", null),
            (null, null, null),
            ("選択範囲を上に移動",   "Alt+Up",        () => MoveLineUp()),
            ("選択範囲を下に移動",   "Alt+Down",      () => MoveLineDown())
        );
    }

    private void Menu_View_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(sender,
            ("コマンドパレット",           "Ctrl+Shift+P",  () => CommandPalette_Open(sender, e)),
            (null, null, null),
            ("エクスプローラー",           "Ctrl+Shift+E",  () => ToggleSidebar_Click(sender, e)),
            ("検索",                       "Ctrl+Shift+F",  () => ShowSearch_Click(sender, e)),
            (null, null, null),
            ("ターミナル",                 "Ctrl+`",        () => TogglePanel(true)),
            (null, null, null),
            ("ワードラップの切り替え",     "Alt+Z",         () => ToggleWordWrap()),
            ("ミニマップの表示/非表示",    "",              null),
            (null, null, null),
            ("ズームイン",                 "Ctrl+=",        () => ZoomIn()),
            ("ズームアウト",               "Ctrl+-",        () => ZoomOut()),
            ("ズームをリセット",           "Ctrl+Num0",     () => ZoomReset())
        );
    }

    private void Menu_Go_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(sender,
            ("行/列に移動...",   "Ctrl+G",           () => GoToLine_Click(sender, e)),
            ("ファイルに移動...", "Ctrl+P",           () => CommandPalette_Open(sender, e)),
            (null, null, null),
            ("前の編集箇所",     "Ctrl+Alt+Left",    null),
            ("次の編集箇所",     "Ctrl+Alt+Right",   null),
            (null, null, null),
            ("定義へ移動",       "F12",              null),
            ("参照へ移動",       "Shift+F12",        null)
        );
    }

    private void Menu_Run_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(sender,
            ("デバッグの開始",       "F5",   null),
            ("デバッグなしで実行",   "Ctrl+F5", null),
            (null, null, null),
            ("ブレークポイントのトグル", "F9", null)
        );
    }

    private void Menu_Terminal_Click(object sender, RoutedEventArgs e)
    {
        var workDir = _currentFolderPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ShowMenu(sender,
            ("新しいターミナル (cmd)",        "Ctrl+Shift+`",  () => AddTerminal(workDir, "cmd.exe")),
            ("新しいターミナル (PowerShell)", "",              () => AddTerminal(workDir, "powershell.exe")),
            ("新しいターミナル (pwsh)",       "",              () => AddTerminal(workDir, "pwsh.exe")),
            (null, null, null),
            ("ターミナルの表示/非表示",       "Ctrl+`",        () => TogglePanel(!_bottomVisible)),
            (null, null, null),
            ("ビルドタスクの実行",            "Ctrl+Shift+B",  null)
        );
    }

    private void Menu_Help_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu(sender,
            ("キーボードショートカット",  "Ctrl+K Ctrl+S", null),
            (null, null, null),
            ("duk について",              "",              () => MessageBox.Show("duk v0.1.0\nリアルタイム共同コーディングソフト", "duk について"))
        );
    }

    private void ShowMenu(object anchor, params (string? label, string? gesture, Action? action)[] items)
    {
        var menu = new ContextMenu();
        foreach (var (label, gesture, action) in items)
        {
            if (label == null) { menu.Items.Add(new Separator()); continue; }
            var item = new MenuItem
            {
                Header = label,
                InputGestureText = gesture ?? "",
                IsEnabled = action != null
            };
            if (action != null) item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (UIElement)anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ========== 編集操作 ==========

    private void Undo_Click(object s, RoutedEventArgs e) => Editor.Undo();
    private void Redo_Click(object s, RoutedEventArgs e) => Editor.Redo();
    private void Cut_Click(object s, RoutedEventArgs e) => Editor.Cut();
    private void Copy_Click(object s, RoutedEventArgs e) => Editor.Copy();
    private void Paste_Click(object s, RoutedEventArgs e) => Editor.Paste();
    private void SelectAll_Click(object s, RoutedEventArgs e) => Editor.SelectAll();

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        var sp = SearchPanel.Install(Editor);
        sp.Open();
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var sp = SearchPanel.Install(Editor);
        sp.Open();
    }

    private void GoToLine_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("行に移動", "行番号を入力:", "");
        if (dlg.ShowDialog() != true) return;
        if (int.TryParse(dlg.Result, out int line) && line > 0 && line <= Editor.Document.LineCount)
        {
            Editor.TextArea.Caret.Line = line;
            Editor.TextArea.Caret.Column = 1;
            Editor.ScrollToLine(line);
            Editor.Focus();
        }
    }

    private void ToggleComment_Click(object sender, RoutedEventArgs e)
    {
        var doc = Editor.Document;
        var line = doc.GetLineByOffset(Editor.TextArea.Caret.Offset);
        var text = doc.GetText(line.Offset, line.Length);
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("//"))
            doc.Replace(line.Offset, line.Length, text.ReplaceFirst("//", "").TrimStart(' '));
        else
            doc.Replace(line.Offset, line.Length, "// " + text);
    }

    private void SelectLine()
    {
        var line = Editor.Document.GetLineByOffset(Editor.TextArea.Caret.Offset);
        Editor.Select(line.Offset, line.Length);
    }

    private void MoveLineUp()
    {
        var doc = Editor.Document;
        var caret = Editor.TextArea.Caret;
        if (caret.Line <= 1) return;
        var cur = doc.GetLineByNumber(caret.Line);
        var prev = doc.GetLineByNumber(caret.Line - 1);
        var curText  = doc.GetText(cur.Offset, cur.Length);
        var prevText = doc.GetText(prev.Offset, prev.Length);
        using var ug = doc.RunUpdate();
        doc.Replace(cur.Offset, cur.Length, prevText);
        doc.Replace(prev.Offset, prev.Length, curText);
        caret.Line--;
    }

    private void MoveLineDown()
    {
        var doc = Editor.Document;
        var caret = Editor.TextArea.Caret;
        if (caret.Line >= doc.LineCount) return;
        var cur  = doc.GetLineByNumber(caret.Line);
        var next = doc.GetLineByNumber(caret.Line + 1);
        var curText  = doc.GetText(cur.Offset, cur.Length);
        var nextText = doc.GetText(next.Offset, next.Length);
        using var ug = doc.RunUpdate();
        doc.Replace(next.Offset, next.Length, curText);
        doc.Replace(cur.Offset, cur.Length, nextText);
        caret.Line++;
    }

    // ========== 表示 ==========

    private void ToggleWordWrap()
    {
        Editor.WordWrap = !Editor.WordWrap;
        StatusEol.Text = Editor.WordWrap ? "Wrap" : "CRLF";
    }

    private void ZoomIn()  => Editor.FontSize = Math.Min(Editor.FontSize + 2, 40);
    private void ZoomOut() => Editor.FontSize = Math.Max(Editor.FontSize - 2, 8);
    private void ZoomReset() => Editor.FontSize = 14;

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarVisible = !_sidebarVisible;
        SidebarCol.Width = _sidebarVisible ? new GridLength(220) : new GridLength(0);
    }

    private void ShowSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!_sidebarVisible) { _sidebarVisible = true; SidebarCol.Width = new GridLength(220); }
        Find_Click(sender, e);
    }

    private void TogglePanel(bool show)
    {
        _bottomVisible = show;
        BottomPanelRow.Height = show ? new GridLength(200) : new GridLength(0);
        if (show)
        {
            // AddTerminal内からは呼ばれないようにTerminalCountを確認してから起動
            if (_terminals.Count == 0)
            {
                var workDir = _currentFolderPath
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                AddTerminal(workDir);
            }
            else
            {
                TerminalInput.Focus();
            }
        }
    }

    private void ClosePanel_Click(object sender, RoutedEventArgs e) => TogglePanel(false);

    private void NewTerminal_Click(object sender, RoutedEventArgs e)
    {
        var workDir = _currentFolderPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddTerminal(workDir, Settings.Shell);
    }

    private void SelectShell_Click(object sender, RoutedEventArgs e)
    {
        var workDir = _currentFolderPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ShowMenu(sender,
            ("cmd.exe",         "", () => AddTerminal(workDir, "cmd.exe")),
            ("powershell.exe",  "", () => AddTerminal(workDir, "powershell.exe")),
            ("pwsh.exe (Core)", "", () => AddTerminal(workDir, "pwsh.exe"))
        );
    }

    private void SplitEditor_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("分割エディターは今後実装予定です", "duk");
    }

    // ========== パネルタブ切り替え ==========

    private void PanelTab_Terminal(object s, RoutedEventArgs e)
    {
        TerminalPanel.Visibility = Visibility.Visible;
        OutputPanel.Visibility   = Visibility.Collapsed;
        ProblemsPanel.Visibility = Visibility.Collapsed;
        SetPanelTabActive(BtnTabTerminal);
    }

    private void PanelTab_Output(object s, RoutedEventArgs e)
    {
        TerminalPanel.Visibility = Visibility.Collapsed;
        OutputPanel.Visibility   = Visibility.Visible;
        ProblemsPanel.Visibility = Visibility.Collapsed;
        SetPanelTabActive(BtnTabOutput);
    }

    private void PanelTab_Problems(object s, RoutedEventArgs e)
    {
        TerminalPanel.Visibility = Visibility.Collapsed;
        OutputPanel.Visibility   = Visibility.Collapsed;
        ProblemsPanel.Visibility = Visibility.Visible;
        SetPanelTabActive(BtnTabProblems);
    }

    private void SetPanelTabActive(Button active)
    {
        foreach (var btn in new[] { BtnTabTerminal, BtnTabOutput, BtnTabProblems })
            btn.Foreground = btn == active
                ? new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc))
                : new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e));
    }

    // ========== コマンドパレット ==========

    private void BuildCommandList()
    {
        _commands.AddRange([
            ("ファイル: 新しいファイル",           () => NewFile_Click(this, new())),
            ("ファイル: ファイルを開く",           () => OpenFile_Click(this, new())),
            ("ファイル: 保存",                     () => SaveFile_Click(this, new())),
            ("表示: ターミナルの切り替え",         () => TogglePanel(!_bottomVisible)),
            ("表示: サイドバーの切り替え",         () => ToggleSidebar_Click(this, new())),
            ("表示: ズームイン",                   () => ZoomIn()),
            ("表示: ズームアウト",                 () => ZoomOut()),
            ("表示: ズームをリセット",             () => ZoomReset()),
            ("表示: ワードラップの切り替え",       () => ToggleWordWrap()),
            ("編集: すべて選択",                   () => Editor.SelectAll()),
            ("編集: 元に戻す",                     () => Editor.Undo()),
            ("編集: やり直し",                     () => Editor.Redo()),
            ("移動: 行に移動",                     () => GoToLine_Click(this, new())),
        ]);
    }

    private void CommandPalette_Open(object sender, EventArgs e)
    {
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandInput.Text = "";
        CommandInput.Focus();
        RefreshCommandList("");
    }

    private void RefreshCommandList(string filter)
    {
        CommandList.Items.Clear();
        foreach (var (label, _) in _commands)
            if (string.IsNullOrEmpty(filter) || label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                CommandList.Items.Add(label);
        if (CommandList.Items.Count > 0) CommandList.SelectedIndex = 0;
    }

    private void CommandInput_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshCommandList(CommandInput.Text);

    private void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedCommand();
        }
        else if (e.Key == Key.Down)
        {
            if (CommandList.SelectedIndex < CommandList.Items.Count - 1)
                CommandList.SelectedIndex++;
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (CommandList.SelectedIndex > 0)
                CommandList.SelectedIndex--;
            e.Handled = true;
        }
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void CommandList_DoubleClick(object sender, MouseButtonEventArgs e) =>
        ExecuteSelectedCommand();

    private void ExecuteSelectedCommand()
    {
        if (CommandList.SelectedItem is string label)
        {
            var match = _commands.FirstOrDefault(c => c.Label == label);
            CommandPaletteOverlay.Visibility = Visibility.Collapsed;
            match.Action?.Invoke();
        }
    }

    // ========== セッション ==========

    private void CreateSession_Click(object sender, RoutedEventArgs e)
    {
        StatusSession.Text = "● 接続中...";
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(Settings) { Owner = this };
        if (win.ShowDialog() == true)
        {
            Settings = win.Settings;
            ApplySettings();
        }
    }

    // ========== ステータスバー ==========

    private void UpdateCursorPosition()
    {
        var pos = Editor.TextArea.Caret.Position;
        StatusCursor.Text = $"Ln {pos.Line}, Col {pos.Column}";
    }

    // ========== ウィンドウ ==========

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // 最大化時はタスクバー分のマージンを追加して被らないようにする
        var margin = WindowState == WindowState.Maximized ? new Thickness(6) : new Thickness(0);
        ((System.Windows.Controls.Grid)Content).Margin = margin;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Win_Minimize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Win_Maximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void Win_Close(object sender, RoutedEventArgs e) => Close();

    // ========== キーボードショートカット ==========

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (ctrl && e.Key == Key.N)           NewFile_Click(this, new());
        else if (ctrl && e.Key == Key.O)      OpenFile_Click(this, new());
        else if (ctrl && e.Key == Key.S && shift) SaveFileAs_Click(this, new());
        else if (ctrl && e.Key == Key.S)      SaveFile_Click(this, new());
        else if (ctrl && e.Key == Key.W)      CloseTab(_activeTab);
        else if (ctrl && e.Key == Key.G)      GoToLine_Click(this, new());
        else if (ctrl && e.Key == Key.B)      ToggleSidebar_Click(this, new());
        else if (ctrl && shift && e.Key == Key.P) CommandPalette_Open(this, EventArgs.Empty);
        else if (ctrl && e.Key == Key.OemTilde) TogglePanel(!_bottomVisible);
        else if (ctrl && e.Key == Key.OemPlus) ZoomIn();
        else if (ctrl && e.Key == Key.OemMinus) ZoomOut();
        else if (alt && e.Key == Key.Z)       ToggleWordWrap();
        else if (alt && e.Key == Key.Up)      MoveLineUp();
        else if (alt && e.Key == Key.Down)    MoveLineDown();
        else if (e.Key == Key.Escape && CommandPaletteOverlay.Visibility == Visibility.Visible)
            CommandPaletteOverlay.Visibility = Visibility.Collapsed;
    }

    // ========== ユーティリティ ==========

    private static IHighlightingDefinition? GetHighlighting(string lang) => lang switch
    {
        "C#"         => HighlightingManager.Instance.GetDefinition("C#"),
        "XAML"       => HighlightingManager.Instance.GetDefinition("XML"),
        "XML"        => HighlightingManager.Instance.GetDefinition("XML"),
        "HTML"       => HighlightingManager.Instance.GetDefinition("HTML"),
        "JavaScript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
        "CSS"        => HighlightingManager.Instance.GetDefinition("CSS"),
        _            => null
    };

    private static string ExtToLang(string ext) => ext switch
    {
        ".cs"   => "C#",
        ".xaml" => "XAML",
        ".xml"  => "XML",
        ".html" => "HTML",
        ".htm"  => "HTML",
        ".js"   => "JavaScript",
        ".ts"   => "TypeScript",
        ".css"  => "CSS",
        ".md"   => "Markdown",
        ".json" => "JSON",
        ".txt"  => "Plain Text",
        _       => ext.TrimStart('.').ToUpper()
    };

    private static (string icon, string color) ExtToIcon(string ext) => ext switch
    {
        ".cs"   => ("C#", "#519aba"),
        ".xaml" => ("⌗",  "#e37933"),
        ".xml"  => ("⌗",  "#e37933"),
        ".html" => ("H",   "#e34c26"),
        ".js"   => ("JS",  "#cbcb41"),
        ".ts"   => ("TS",  "#519aba"),
        ".css"  => ("CSS", "#563d7c"),
        ".json" => ("{}",  "#cbcb41"),
        ".md"   => ("M",   "#519aba"),
        ".txt"  => ("≡",   "#888888"),
        ".sln"  or ".slnx" => ("◈",  "#854cc7"),
        _       => ("📄",  "#888888")
    };
}

static class StringExtensions
{
    public static string ReplaceFirst(this string text, string search, string replace)
    {
        var pos = text.IndexOf(search);
        if (pos < 0) return text;
        return text[..pos] + replace + text[(pos + search.Length)..];
    }
}

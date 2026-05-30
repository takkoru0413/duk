using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;

namespace duk.App;

public partial class MainWindow : Window
{

    private bool _sidebarVisible = true;
    private string? _currentFilePath;
    private string? _currentFolderPath;

    public MainWindow()
    {
        InitializeComponent();
        Editor.TextArea.Caret.PositionChanged += (s, e) => UpdateCursorPosition();
        LoadWelcome();
    }

    // ========== エディタ初期表示 ==========

    private void LoadWelcome()
    {
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
        Editor.Text =
            "using System;\n\n" +
            "namespace duk;\n\n" +
            "class Program\n" +
            "{\n" +
            "    static void Main(string[] args)\n" +
            "    {\n" +
            "        Console.WriteLine(\"Hello, duk!\");\n" +
            "    }\n" +
            "}\n";
    }

    // ========== ファイル操作 ==========

    private void NewFile_Click(object sender, RoutedEventArgs e)
    {
        _currentFilePath = null;
        Editor.Text = "";
        Editor.SyntaxHighlighting = null;
        StatusLang.Text = "Plain Text";
        Title = "duk - Untitled";
    }

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

    private void OpenFileByPath(string path)
    {
        try
        {
            Editor.Text = File.ReadAllText(path);
            _currentFilePath = path;
            var ext = Path.GetExtension(path).ToLower();
            Editor.SyntaxHighlighting = ext switch
            {
                ".cs"   => HighlightingManager.Instance.GetDefinition("C#"),
                ".xaml" => HighlightingManager.Instance.GetDefinition("XML"),
                ".xml"  => HighlightingManager.Instance.GetDefinition("XML"),
                ".html" => HighlightingManager.Instance.GetDefinition("HTML"),
                ".js"   => HighlightingManager.Instance.GetDefinition("JavaScript"),
                ".css"  => HighlightingManager.Instance.GetDefinition("CSS"),
                _       => null
            };
            StatusLang.Text = ext switch
            {
                ".cs"   => "C#",
                ".xaml" => "XAML",
                ".xml"  => "XML",
                ".html" => "HTML",
                ".js"   => "JavaScript",
                ".css"  => "CSS",
                ".md"   => "Markdown",
                ".txt"  => "Plain Text",
                _       => ext.TrimStart('.').ToUpper()
            };
            Title = $"duk - {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ファイルを開けませんでした:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath != null)
            SaveToPath(_currentFilePath);
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
            File.WriteAllText(path, Editor.Text);
            _currentFilePath = path;
            Title = $"duk - {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存できませんでした:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "フォルダを選択（任意のファイルを選択してください）",
            CheckFileExists = false,
            FileName = "フォルダを選択"
        };
        if (dlg.ShowDialog() != true) return;
        var folder = Path.GetDirectoryName(dlg.FileName)!;
        LoadFolder(folder);
    }

    private void LoadFolder(string folderPath)
    {
        _currentFolderPath = folderPath;
        FolderName.Text = Path.GetFileName(folderPath).ToUpper();
        FolderName.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#bbbbbb"));

        FileTree.Children.Clear();

        foreach (var file in Directory.GetFiles(folderPath))
        {
            var btn = new Button
            {
                Style = (Style)FindResource("FileBtn"),
                Tag = file
            };
            var ext = Path.GetExtension(file).ToLower();
            var icon = ext switch
            {
                ".cs"   => ("C# ", "#519aba"),
                ".xaml" => ("⌗ ", "#e37933"),
                ".xml"  => ("⌗ ", "#e37933"),
                ".md"   => ("M ", "#519aba"),
                ".json" => ("{ }", "#cbcb41"),
                ".txt"  => ("≡ ", "#888888"),
                _       => ("📄 ", "#888888")
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = icon.Item1,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(icon.Item2)),
                FontSize = 11
            });
            panel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(file),
                Margin = new Thickness(8, 0, 0, 0)
            });
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Width = 20 });
            sp.Children.Add(panel);
            btn.Content = sp;
            btn.Click += (s, _) => OpenFileByPath((string)((Button)s).Tag);
            FileTree.Children.Add(btn);
        }

        foreach (var dir in Directory.GetDirectories(folderPath))
        {
            var btn = new Button { Style = (Style)FindResource("FileBtn") };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = $"▸ {Path.GetFileName(dir)}",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#cccccc")),
                Margin = new Thickness(8, 0, 0, 0)
            });
            btn.Content = sp;
            FileTree.Children.Add(btn);
        }
    }

    // ========== メニュー ==========

    private void Menu_File_Click(object sender, RoutedEventArgs e)
    {
        var col = System.Windows.Media.Color.FromRgb(0x1e, 0x1e, 0x1e);
        var fgCol = System.Windows.Media.Color.FromRgb(0xcc, 0xcc, 0xcc);
        var fg = new System.Windows.Media.SolidColorBrush(fgCol);

        var menu = new ContextMenu
        {
            Background = new System.Windows.Media.SolidColorBrush(col),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x3c, 0x3c, 0x3c)),
            BorderThickness = new Thickness(1)
        };

        MenuItem Item(string header, RoutedEventHandler handler, string gesture = "")
        {
            var item = new MenuItem
            {
                Header = header,
                InputGestureText = gesture,
                Foreground = fg,
                Background = System.Windows.Media.Brushes.Transparent
            };
            item.Click += handler;
            return item;
        }

        menu.Items.Add(Item("新しいテキストファイル",  NewFile_Click,    "Ctrl+N"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("ファイルを開く...",        OpenFile_Click,   "Ctrl+O"));
        menu.Items.Add(Item("フォルダーを開く...",      OpenFolder_Click, "Ctrl+K Ctrl+O"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("保存",                     SaveFile_Click,   "Ctrl+S"));
        menu.Items.Add(Item("名前を付けて保存...",      SaveFileAs_Click, "Ctrl+Shift+S"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("エディターを閉じる",       (s2, e2) => NewFile_Click(s2, e2), "Ctrl+F4"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("ウィンドウを閉じる",       (s2, e2) => Close(),           "Alt+F4"));

        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void Menu_View_Click(object sender, RoutedEventArgs e) =>
        ToggleSidebar_Click(sender, e);

    private void OpenSettings_Click(object sender, RoutedEventArgs e) { }

    // ========== サイドバートグル ==========

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarVisible = !_sidebarVisible;
        SidebarCol.Width = _sidebarVisible ? new GridLength(220) : new GridLength(0);
    }

    // ========== ステータス更新 ==========

    private void UpdateCursorPosition()
    {
        var pos = Editor.TextArea.Caret.Position;
        StatusCursor.Text = $"Ln {pos.Line}, Col {pos.Column}";
    }

    // ========== ウィンドウ操作 ==========

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
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            NewFile_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            OpenFile_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            SaveFile_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            SaveFileAs_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
            ToggleSidebar_Click(this, new RoutedEventArgs());
    }

    private void CreateSession_Click(object sender, RoutedEventArgs e)
    {
        StatusSession.Text = "● 接続中...";
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace duk.App;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; }
    private string _currentCategory = "editor";
    private readonly Button[] _catBtns;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        _catBtns = [CatEditor, CatAppearance, CatTerminal, CatLanguage, CatFile, CatSession, CatSearch, CatKeybindings];
        ShowCategory("editor");
    }

    // ========== カテゴリ切替 ==========

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            ShowCategory(tag);
    }

    private void ShowCategory(string category)
    {
        _currentCategory = category;

        var activeStyle   = (Style)FindResource("CatBtnActive");
        var inactiveStyle = (Style)FindResource("CatBtn");
        foreach (var b in _catBtns)
            b.Style = (string)b.Tag == category ? activeStyle : inactiveStyle;

        SettingsContent.Children.Clear();
        switch (category)
        {
            case "editor":     BuildEditorSettings();     break;
            case "appearance": BuildAppearanceSettings(); break;
            case "terminal":   BuildTerminalSettings();   break;
            case "language":   BuildLanguageSettings();   break;
            case "file":       BuildFileSettings();       break;
            case "session":    BuildSessionSettings();    break;
            case "search":     BuildSearchSettings();     break;
            case "keys":       BuildKeybindingSettings(); break;
        }
    }

    // ========== 設定UI構築ヘルパー ==========

    private void AddSection(string title)
    {
        SettingsContent.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 20, 0, 12)
        });
        SettingsContent.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x2d, 0x2d)),
            Margin = new Thickness(0, 0, 0, 12)
        });
    }

    private void AddToggle(string label, string desc, bool value, Action<bool> onChange)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI")
        });
        if (!string.IsNullOrEmpty(desc))
            textPanel.Children.Add(new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(textPanel, 0);

        var chk = new CheckBox
        {
            IsChecked = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        chk.Checked   += (_, _) => onChange(true);
        chk.Unchecked += (_, _) => onChange(false);
        Grid.SetColumn(chk, 1);

        row.Children.Add(textPanel);
        row.Children.Add(chk);
        SettingsContent.Children.Add(row);
    }

    private void AddDropdown(string label, string desc, string[] options, string current, Action<string> onChange)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI")
        });
        if (!string.IsNullOrEmpty(desc))
            textPanel.Children.Add(new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(textPanel, 0);

        var combo = new ComboBox
        {
            Style = (Style)FindResource("SettingCombo"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        foreach (var opt in options) combo.Items.Add(opt);
        combo.SelectedItem = current;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string s) onChange(s);
        };
        Grid.SetColumn(combo, 1);

        row.Children.Add(textPanel);
        row.Children.Add(combo);
        SettingsContent.Children.Add(row);
    }

    private void AddSlider(string label, string desc, double value, double min, double max, Action<double> onChange, Func<double, string>? formatter = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI")
        });
        if (!string.IsNullOrEmpty(desc))
            textPanel.Children.Add(new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(textPanel, 0);

        var valLabel = new TextBlock
        {
            Text = formatter != null ? formatter(value) : value.ToString("0"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
            FontSize = 12,
            Width = 36,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Style = (Style)FindResource("SettingSlider"),
            Minimum = min, Maximum = max, Value = value,
            TickFrequency = 1, IsSnapToTickEnabled = true
        };
        slider.ValueChanged += (_, e) =>
        {
            valLabel.Text = formatter != null ? formatter(e.NewValue) : e.NewValue.ToString("0");
            onChange(e.NewValue);
        };

        var ctrl = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        ctrl.Children.Add(slider);
        ctrl.Children.Add(valLabel);
        Grid.SetColumn(ctrl, 1);

        row.Children.Add(textPanel);
        row.Children.Add(ctrl);
        SettingsContent.Children.Add(row);
    }

    private void AddTextInput(string label, string desc, string value, Action<string> onChange)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI")
        });
        if (!string.IsNullOrEmpty(desc))
            textPanel.Children.Add(new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(textPanel, 0);

        var input = new TextBox
        {
            Style = (Style)FindResource("SettingInput"),
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        input.TextChanged += (_, _) => onChange(input.Text);
        Grid.SetColumn(input, 1);

        row.Children.Add(textPanel);
        row.Children.Add(input);
        SettingsContent.Children.Add(row);
    }

    // ========== 各カテゴリの設定 ==========

    private void BuildEditorSettings()
    {
        AddSection("フォント");
        AddDropdown("フォントファミリー", "エディターのフォントを選択します",
            ["Consolas", "Courier New", "Fira Code", "JetBrains Mono", "Source Code Pro", "Cascadia Code", "MS Gothic"],
            Settings.FontFamily, v => Settings.FontFamily = v);
        AddSlider("フォントサイズ", "エディターのフォントサイズ（px）",
            Settings.FontSize, 8, 40, v => Settings.FontSize = v, v => $"{v:0}px");
        AddSlider("行の高さ", "行間のサイズ",
            Settings.LineHeight, 16, 40, v => Settings.LineHeight = (int)v, v => $"{v:0}px");

        AddSection("エディター");
        AddToggle("行番号の表示", "エディターの左側に行番号を表示します",
            Settings.ShowLineNumbers, v => Settings.ShowLineNumbers = v);
        AddToggle("ワードラップ", "長い行を折り返して表示します",
            Settings.WordWrap, v => Settings.WordWrap = v);
        AddToggle("括弧の自動補完", "括弧・引用符を入力すると自動で閉じます",
            Settings.AutoCloseBrackets, v => Settings.AutoCloseBrackets = v);
        AddToggle("コードの折りたたみ", "{ } ブロックを折りたたんで表示できます",
            Settings.CodeFolding, v => Settings.CodeFolding = v);
        AddToggle("ブラケットのハイライト", "対応する括弧をハイライトします",
            Settings.BracketHighlight, v => Settings.BracketHighlight = v);
        AddToggle("空白の表示", "スペース・タブを可視化します",
            Settings.RenderWhitespace, v => Settings.RenderWhitespace = v);

        AddSection("インデント");
        AddDropdown("タブサイズ", "タブ・インデントのスペース数",
            ["1", "2", "4", "8"],
            Settings.TabSize.ToString(), v => Settings.TabSize = int.Parse(v));

        AddSection("自動保存");
        AddToggle("自動保存", "変更後に自動でファイルを保存します",
            Settings.AutoSave, v => Settings.AutoSave = v);
    }

    private void BuildAppearanceSettings()
    {
        AddSection("テーマ");
        AddDropdown("カラーテーマ", "エディター全体の配色テーマを選択します",
            ["Dark (デフォルト)", "Light", "High Contrast Dark", "Monokai"],
            Settings.Theme, v => Settings.Theme = v);

        AddSection("言語");
        AddDropdown("表示言語", "UIの表示言語を選択します",
            ["日本語 (ja)", "English (en)"],
            Settings.Language == "ja" ? "日本語 (ja)" : "English (en)",
            v => Settings.Language = v.Contains("ja") ? "ja" : "en");
    }

    private void BuildTerminalSettings()
    {
        AddSection("ターミナル");
        AddDropdown("シェル", "ターミナルで使用するシェルを選択します",
            ["cmd.exe", "powershell.exe", "pwsh.exe"],
            Settings.Shell, v => Settings.Shell = v);
        AddDropdown("フォントファミリー", "ターミナルのフォント",
            ["Consolas", "Courier New", "Cascadia Code", "MS Gothic"],
            Settings.TerminalFontFamily, v => Settings.TerminalFontFamily = v);
        AddSlider("フォントサイズ", "ターミナルのフォントサイズ",
            Settings.TerminalFontSize, 8, 24, v => Settings.TerminalFontSize = v, v => $"{v:0}px");
    }

    private void BuildLanguageSettings()
    {
        AddSection("言語");
        AddDropdown("デフォルト言語", "新しいファイルのデフォルト言語",
            ["Plain Text", "C#", "JavaScript", "TypeScript", "HTML", "CSS", "JSON", "XML", "Markdown", "Python"],
            Settings.DefaultLanguage, v => Settings.DefaultLanguage = v);

        AddSection("フォーマット");
        AddToggle("保存時にフォーマット", "ファイル保存時にコードを自動整形します",
            Settings.FormatOnSave, v => Settings.FormatOnSave = v);
        AddToggle("貼り付け時にフォーマット", "コードを貼り付けた際に自動整形します",
            Settings.FormatOnPaste, v => Settings.FormatOnPaste = v);
    }

    private void BuildFileSettings()
    {
        AddSection("ファイル");
        AddDropdown("デフォルトエンコーディング", "新しいファイルの文字コード",
            ["UTF-8", "UTF-8 BOM", "Shift-JIS", "EUC-JP", "UTF-16 LE"],
            Settings.DefaultEncoding, v => Settings.DefaultEncoding = v);
        AddDropdown("改行コード", "デフォルトの改行コード",
            ["CRLF (Windows)", "LF (Unix)", "CR (Mac)"],
            Settings.DefaultEol.Contains("CRLF") ? "CRLF (Windows)" :
            Settings.DefaultEol.Contains("CR") ? "CR (Mac)" : "LF (Unix)",
            v => Settings.DefaultEol = v.Contains("CRLF") ? "CRLF" : v.Contains("LF") ? "LF" : "CR");

        AddSection("保存");
        AddToggle("末尾の空白を削除", "保存時にファイル末尾の空白を自動削除します",
            Settings.TrimTrailingWhitespace, v => Settings.TrimTrailingWhitespace = v);
        AddToggle("末尾に改行を挿入", "保存時にファイルの最後に改行を追加します",
            Settings.InsertFinalNewline, v => Settings.InsertFinalNewline = v);
    }

    private void BuildSessionSettings()
    {
        AddSection("コラボレーション");
        AddTextInput("ユーザー名", "セッション参加時に表示される名前",
            Settings.UserName, v => Settings.UserName = v);
        AddDropdown("カーソルカラー", "自分のカーソルの色",
            ["#4ec9b0 (シアン)", "#f14c4c (レッド)", "#ffd700 (ゴールド)", "#a8cc8c (グリーン)", "#e5c07b (イエロー)"],
            Settings.CursorColor.StartsWith("#4ec") ? "#4ec9b0 (シアン)" :
            Settings.CursorColor.StartsWith("#f14") ? "#f14c4c (レッド)" : "#4ec9b0 (シアン)",
            v => Settings.CursorColor = v.Split(' ')[0]);

        AddSection("接続");
        var info = new TextBlock
        {
            Text = "セッション機能はフェーズ2で実装予定です。\nリンクを共有するだけでグループ編集に参加できるようになります。",
            Foreground = new SolidColorBrush(Color.FromRgb(0x6e, 0x6e, 0x6e)),
            FontSize = 13,
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        SettingsContent.Children.Add(info);
    }

    private void BuildSearchSettings()
    {
        AddSection("検索");
        AddToggle("大文字と小文字を区別", "検索時に大文字・小文字を区別します",
            Settings.SearchCaseSensitive, v => Settings.SearchCaseSensitive = v);
        AddToggle("単語全体に一致", "完全一致の単語のみを検索します",
            Settings.SearchWholeWord, v => Settings.SearchWholeWord = v);
        AddToggle("正規表現を使用", "正規表現パターンで検索します",
            Settings.SearchRegex, v => Settings.SearchRegex = v);
    }

    private void BuildKeybindingSettings()
    {
        AddSection("キーボードショートカット一覧");
        var shortcuts = new[]
        {
            ("Ctrl+N",         "新しいファイル"),
            ("Ctrl+O",         "ファイルを開く"),
            ("Ctrl+S",         "保存"),
            ("Ctrl+Shift+S",   "名前を付けて保存"),
            ("Ctrl+W",         "タブを閉じる"),
            ("Ctrl+Z",         "元に戻す"),
            ("Ctrl+Y",         "やり直し"),
            ("Ctrl+X",         "切り取り"),
            ("Ctrl+C",         "コピー"),
            ("Ctrl+V",         "貼り付け"),
            ("Ctrl+A",         "すべて選択"),
            ("Ctrl+F",         "検索"),
            ("Ctrl+H",         "置換"),
            ("Ctrl+G",         "行に移動"),
            ("Ctrl+/",         "コメントのトグル"),
            ("Ctrl+B",         "サイドバーの切り替え"),
            ("Ctrl+`",         "ターミナルの切り替え"),
            ("Ctrl+Shift+P",   "コマンドパレット"),
            ("Ctrl+=",         "ズームイン"),
            ("Ctrl+-",         "ズームアウト"),
            ("Alt+Z",          "ワードラップの切り替え"),
            ("Alt+Up/Down",    "行の移動"),
        };

        foreach (var (key, desc) in shortcuts)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3c, 0x3c, 0x3c)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            keyBadge.Child = new TextBlock
            {
                Text = key,
                Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
            Grid.SetColumn(keyBadge, 0);

            var descText = new TextBlock
            {
                Text = desc,
                Foreground = new SolidColorBrush(Color.FromRgb(0xbb, 0xbb, 0xbb)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(descText, 1);

            row.Children.Add(keyBadge);
            row.Children.Add(descText);
            SettingsContent.Children.Add(row);
        }
    }

    // ========== 検索フィルター ==========

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ShowCategory(_currentCategory);

    // ========== ボタン ==========

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Settings.Save();
        DialogResult = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("設定をすべてデフォルトに戻しますか？", "設定をリセット",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var defaults = new AppSettings();
        foreach (var prop in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
            prop.SetValue(Settings, prop.GetValue(defaults));
        ShowCategory(_currentCategory);
    }
}

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace duk.App;

public static partial class AnsiColorParser
{
    [GeneratedRegex(@"\x1B\[([0-9;]*)m|\x1B\[[0-9;]*[A-Za-z]|\x0D")]
    private static partial Regex EscapeRegex();

    private static readonly Dictionary<int, Color> FgColors = new()
    {
        { 30, Color.FromRgb(0x1e, 0x1e, 0x1e) },   // Black
        { 31, Color.FromRgb(0xf1, 0x4c, 0x4c) },   // Red
        { 32, Color.FromRgb(0x4e, 0xc9, 0x94) },   // Green
        { 33, Color.FromRgb(0xe5, 0xc0, 0x7b) },   // Yellow
        { 34, Color.FromRgb(0x61, 0xaf, 0xef) },   // Blue
        { 35, Color.FromRgb(0xc6, 0x78, 0xdd) },   // Magenta
        { 36, Color.FromRgb(0x56, 0xb6, 0xc2) },   // Cyan
        { 37, Color.FromRgb(0xab, 0xb2, 0xbf) },   // White
        { 90, Color.FromRgb(0x5c, 0x63, 0x70) },   // Bright Black
        { 91, Color.FromRgb(0xe0, 0x6c, 0x75) },   // Bright Red
        { 92, Color.FromRgb(0x98, 0xc3, 0x79) },   // Bright Green
        { 93, Color.FromRgb(0xe5, 0xc0, 0x7b) },   // Bright Yellow
        { 94, Color.FromRgb(0x61, 0xaf, 0xef) },   // Bright Blue
        { 95, Color.FromRgb(0xc6, 0x78, 0xdd) },   // Bright Magenta
        { 96, Color.FromRgb(0x4e, 0xc9, 0xb0) },   // Bright Cyan
        { 97, Color.FromRgb(0xff, 0xff, 0xff) },   // Bright White
    };

    private static Color _currentFg = Color.FromRgb(0xcc, 0xcc, 0xcc);
    private static bool _bold;

    public static void Reset()
    {
        _currentFg = Color.FromRgb(0xcc, 0xcc, 0xcc);
        _bold = false;
    }

    /// <summary>テキスト（ANSI含む）を Inline のリストに変換して Paragraph に追加</summary>
    public static void AppendAnsiText(Paragraph para, string text)
    {
        int pos = 0;
        foreach (Match m in EscapeRegex().Matches(text))
        {
            // エスケープより前の普通のテキスト
            if (m.Index > pos)
                para.Inlines.Add(MakeRun(text[pos..m.Index]));

            // SGR コード (色変更)
            if (m.Value.EndsWith('m'))
            {
                var codes = m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (codes.Length == 0) { _currentFg = Color.FromRgb(0xcc, 0xcc, 0xcc); _bold = false; }
                foreach (var code in codes)
                {
                    if (!int.TryParse(code, out int n)) continue;
                    switch (n)
                    {
                        case 0:  _currentFg = Color.FromRgb(0xcc, 0xcc, 0xcc); _bold = false; break;
                        case 1:  _bold = true; break;
                        case 22: _bold = false; break;
                        default:
                            if (FgColors.TryGetValue(n, out var c)) _currentFg = c;
                            break;
                    }
                }
            }
            pos = m.Index + m.Length;
        }

        // 残りのテキスト
        if (pos < text.Length)
            para.Inlines.Add(MakeRun(text[pos..]));
    }

    private static Run MakeRun(string text) => new(text)
    {
        Foreground  = new SolidColorBrush(_currentFg),
        FontWeight  = _bold ? FontWeights.Bold : FontWeights.Normal
    };

    /// <summary>ANSI コードを完全除去（ログ保存用）</summary>
    public static string StripAll(string text) => EscapeRegex().Replace(text, "");
}

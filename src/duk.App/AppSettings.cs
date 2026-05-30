using System.IO;
using System.Text.Json;

namespace duk.App;

public class AppSettings
{
    // エディター
    public double FontSize        { get; set; } = 14;
    public string FontFamily      { get; set; } = "Consolas";
    public int    TabSize         { get; set; } = 4;
    public bool   WordWrap        { get; set; } = false;
    public bool   ShowLineNumbers { get; set; } = true;
    public bool   AutoSave        { get; set; } = false;
    public bool   AutoCloseBrackets { get; set; } = true;
    public bool   CodeFolding     { get; set; } = true;
    public bool   BracketHighlight { get; set; } = true;
    public bool   RenderWhitespace { get; set; } = false;
    public bool   SmoothScrolling { get; set; } = true;
    public int    LineHeight      { get; set; } = 20;

    // 表示
    public string Theme           { get; set; } = "Dark";
    public string Language        { get; set; } = "ja";

    // ターミナル
    public string Shell           { get; set; } = "cmd.exe";
    public double TerminalFontSize { get; set; } = 13;
    public string TerminalFontFamily { get; set; } = "Consolas";

    // セッション・コラボ
    public string UserName        { get; set; } = "";
    public string CursorColor     { get; set; } = "#4ec9b0";

    // 言語
    public string DefaultLanguage { get; set; } = "Plain Text";
    public bool   FormatOnSave    { get; set; } = false;
    public bool   FormatOnPaste   { get; set; } = false;

    // ファイル
    public string DefaultEncoding { get; set; } = "UTF-8";
    public string DefaultEol      { get; set; } = "CRLF";
    public bool   TrimTrailingWhitespace { get; set; } = false;
    public bool   InsertFinalNewline     { get; set; } = false;

    // 検索
    public bool   SearchCaseSensitive  { get; set; } = false;
    public bool   SearchWholeWord      { get; set; } = false;
    public bool   SearchRegex          { get; set; } = false;

    // ========== 永続化 ==========

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "duk", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

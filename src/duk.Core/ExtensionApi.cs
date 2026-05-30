namespace duk.Core;

// ========== 拡張機能が実装するインターフェース ==========

public interface IExtension
{
    string Name        { get; }
    string Version     { get; }
    string Description { get; }
    string Author      { get; }

    void Activate(IExtensionContext ctx);
    void Deactivate();
}

// ========== 拡張機能からアプリを操作するAPI ==========

public interface IExtensionContext
{
    // エディター操作
    string  GetEditorText();
    void    SetEditorText(string text);
    int     GetCursorLine();
    int     GetCursorColumn();
    string? GetCurrentFilePath();
    string? GetCurrentFolderPath();

    // ステータスバー
    void SetStatusMessage(string message, int durationMs = 3000);

    // メニュー
    void RegisterMenuItem(string menu, string label, Action handler);

    // コマンドパレット
    void RegisterCommand(string label, Action handler);

    // 通知
    void ShowInfo(string message);
    void ShowWarning(string message);
    void ShowError(string message);

    // ファイル操作
    void OpenFile(string path);
    void SaveCurrentFile();
}

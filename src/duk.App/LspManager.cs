using System.IO;
using System.Windows;
using duk.Core.Lsp;

namespace duk.App;

/// <summary>言語サーバーの設定</summary>
public record LspServerConfig(
    string[] Extensions,   // 対応拡張子
    string   Executable,   // サーバーの実行ファイル
    string[] Args,         // 引数
    string   LanguageId);  // LSP languageId

/// <summary>LSPクライアントを言語ごとに管理する</summary>
public class LspManager : IDisposable
{
    private readonly Dictionary<string, LspClient> _clients = [];   // languageId → client
    private readonly Dictionary<string, string>    _fileToLang = []; // filePath → languageId
    private readonly MainWindow _window;

    // 言語サーバーの設定一覧
    private static readonly LspServerConfig[] Configs =
    [
        new([".cs"],                "csharp-ls",                       [],            "csharp"),
        new([".py"],                "pylsp",                           [],            "python"),
        new([".js", ".mjs"],        "typescript-language-server",      ["--stdio"],   "javascript"),
        new([".ts", ".tsx"],        "typescript-language-server",      ["--stdio"],   "typescript"),
        new([".json"],              "vscode-json-language-server",     ["--stdio"],   "json"),
        new([".html", ".htm"],      "vscode-html-language-server",     ["--stdio"],   "html"),
        new([".css"],               "vscode-css-language-server",      ["--stdio"],   "css"),
        new([".go"],                "gopls",                           [],            "go"),
        new([".rs"],                "rust-analyzer",                   [],            "rust"),
    ];

    public event EventHandler<(string FilePath, Core.Lsp.Diagnostic[] Diagnostics)>? DiagnosticsUpdated;

    public LspManager(MainWindow window)
    {
        _window = window;
    }

    public bool HasServer(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        return Configs.Any(c => c.Extensions.Contains(ext));
    }

    public async Task NotifyOpenedAsync(string filePath, string text)
    {
        var client = await GetOrCreateClientAsync(filePath);
        if (client == null) return;
        var lang = GetLanguageId(filePath);
        _fileToLang[filePath] = lang;
        await client.DidOpenAsync(filePath, text, lang);
    }

    public async Task NotifyChangedAsync(string filePath, string text)
    {
        if (!_clients.TryGetValue(GetLanguageId(filePath), out var client)) return;
        await client.DidChangeAsync(filePath, text);
    }

    public async Task NotifyClosedAsync(string filePath)
    {
        if (!_clients.TryGetValue(GetLanguageId(filePath), out var client)) return;
        await client.DidCloseAsync(filePath);
        _fileToLang.Remove(filePath);
    }

    public async Task<CompletionList?> GetCompletionsAsync(string filePath, int line, int col)
    {
        var client = GetActiveClient(filePath);
        if (client == null) return null;
        return await client.GetCompletionsAsync(filePath, line, col);
    }

    public async Task<Hover?> GetHoverAsync(string filePath, int line, int col)
    {
        var client = GetActiveClient(filePath);
        if (client == null) return null;
        return await client.GetHoverAsync(filePath, line, col);
    }

    public async Task<Location[]?> GoToDefinitionAsync(string filePath, int line, int col)
    {
        var client = GetActiveClient(filePath);
        if (client == null) return null;
        return await client.GetDefinitionAsync(filePath, line, col);
    }

    // ========== 内部メソッド ==========

    private async Task<LspClient?> GetOrCreateClientAsync(string filePath)
    {
        var lang = GetLanguageId(filePath);
        if (lang == "") return null;
        if (_clients.TryGetValue(lang, out var existing)) return existing;

        var cfg = GetConfig(filePath);
        if (cfg == null) return null;

        // サーバーの実行ファイルが存在するか確認
        if (!IsExecutableAvailable(cfg.Executable))
        {
            _window.Dispatcher.Invoke(() =>
                _window.ShowLspWarning($"{cfg.Executable} が見つかりません。\n" +
                    GetInstallHint(cfg.LanguageId)));
            return null;
        }

        try
        {
            var rootDir = _window.CurrentFolderPath
                ?? Path.GetDirectoryName(filePath)
                ?? Environment.CurrentDirectory;

            var client = new LspClient(cfg.Executable, cfg.Args, rootDir);
            client.DiagnosticsReceived += (_, p) =>
            {
                var path = LspClient.UriToPath(p.Uri);
                DiagnosticsUpdated?.Invoke(this, (path, p.Diagnostics));
            };

            await client.InitializeAsync(rootDir);
            _clients[lang] = client;
            return client;
        }
        catch (Exception ex)
        {
            _window.Dispatcher.Invoke(() =>
                _window.ShowLspWarning($"言語サーバー起動失敗: {cfg.Executable}\n{ex.Message}"));
            return null;
        }
    }

    private LspClient? GetActiveClient(string filePath)
    {
        var lang = GetLanguageId(filePath);
        return _clients.GetValueOrDefault(lang);
    }

    private static string GetLanguageId(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        return Configs.FirstOrDefault(c => c.Extensions.Contains(ext))?.LanguageId ?? "";
    }

    private static LspServerConfig? GetConfig(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        return Configs.FirstOrDefault(c => c.Extensions.Contains(ext));
    }

    private static bool IsExecutableAvailable(string exe)
    {
        // フルパスの場合
        if (File.Exists(exe)) return true;
        // PATHから探す
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
        return paths.Any(p =>
            File.Exists(Path.Combine(p, exe)) ||
            File.Exists(Path.Combine(p, exe + ".exe")) ||
            File.Exists(Path.Combine(p, exe + ".cmd")));
    }

    private static string GetInstallHint(string lang) => lang switch
    {
        "csharp"     => "インストール: dotnet tool install -g csharp-ls",
        "python"     => "インストール: pip install python-lsp-server",
        "javascript" or "typescript" => "インストール: npm install -g typescript-language-server typescript",
        "go"         => "インストール: go install golang.org/x/tools/gopls@latest",
        "rust"       => "インストール: rustup component add rust-analyzer",
        _            => ""
    };

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            try { client.Dispose(); } catch { }
        _clients.Clear();
    }
}

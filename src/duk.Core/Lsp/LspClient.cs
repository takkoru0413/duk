using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;

namespace duk.Core.Lsp;

public class LspClient : IDisposable
{
    private readonly Process      _server;
    private readonly JsonRpc      _rpc;
    private          int          _version = 0;
    private          bool         _initialized = false;
    private          string       _rootUri = "";

    public event EventHandler<PublishDiagnosticsParams>? DiagnosticsReceived;
    public bool IsReady => _initialized;
    public string ServerName { get; }

    public LspClient(string serverExe, string[] args, string workDir)
    {
        ServerName = Path.GetFileNameWithoutExtension(serverExe);

        _server = new Process
        {
            StartInfo = new ProcessStartInfo(serverExe, string.Join(" ", args))
            {
                WorkingDirectory       = workDir,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };
        _server.Start();

        var options = new JsonRpcProxyOptions { MethodNameTransform = _ => _ };
        _rpc = new JsonRpc(_server.StandardInput.BaseStream,
                           _server.StandardOutput.BaseStream);

        // サーバーからの通知を受け取る
        _rpc.AddLocalRpcMethod("textDocument/publishDiagnostics",
            (PublishDiagnosticsParams p) => DiagnosticsReceived?.Invoke(this, p));

        _rpc.StartListening();
    }

    // ========== ライフサイクル ==========

    public async Task InitializeAsync(string rootPath)
    {
        _rootUri = PathToUri(rootPath);
        var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement>(
            "initialize",
            new InitializeParams
            {
                ProcessId = Environment.ProcessId,
                RootUri   = _rootUri
            });

        await _rpc.NotifyWithParameterObjectAsync("initialized", new { });
        _initialized = true;
    }

    public async Task ShutdownAsync()
    {
        if (!_initialized) return;
        try
        {
            await _rpc.InvokeAsync("shutdown");
            await _rpc.NotifyAsync("exit");
        }
        catch { }
    }

    // ========== ドキュメント操作 ==========

    public async Task DidOpenAsync(string filePath, string text, string languageId)
    {
        if (!_initialized) return;
        await _rpc.NotifyWithParameterObjectAsync("textDocument/didOpen",
            new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri        = PathToUri(filePath),
                    LanguageId = languageId,
                    Version    = ++_version,
                    Text       = text
                }
            });
    }

    public async Task DidChangeAsync(string filePath, string newText)
    {
        if (!_initialized) return;
        await _rpc.NotifyWithParameterObjectAsync("textDocument/didChange",
            new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier
                {
                    Uri     = PathToUri(filePath),
                    Version = ++_version
                },
                ContentChanges = [new TextDocumentContentChangeEvent { Text = newText }]
            });
    }

    public async Task DidCloseAsync(string filePath)
    {
        if (!_initialized) return;
        await _rpc.NotifyWithParameterObjectAsync("textDocument/didClose",
            new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier(PathToUri(filePath))
            });
    }

    // ========== 言語機能 ==========

    public async Task<CompletionList?> GetCompletionsAsync(string filePath, int line, int character)
    {
        if (!_initialized) return null;
        try
        {
            var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement>(
                "textDocument/completion",
                new CompletionParams
                {
                    TextDocument = new TextDocumentIdentifier(PathToUri(filePath)),
                    Position     = new Position(line, character),
                    Context      = new CompletionContext { TriggerKind = 1 }
                });

            // レスポンスは CompletionList か CompletionItem[] のどちらかの場合がある
            if (result.ValueKind == JsonValueKind.Array)
            {
                var items = JsonSerializer.Deserialize<CompletionItem[]>(result);
                return new CompletionList { Items = items ?? [] };
            }
            return JsonSerializer.Deserialize<CompletionList>(result);
        }
        catch { return null; }
    }

    public async Task<Hover?> GetHoverAsync(string filePath, int line, int character)
    {
        if (!_initialized) return null;
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<Hover>(
                "textDocument/hover",
                new HoverParams
                {
                    TextDocument = new TextDocumentIdentifier(PathToUri(filePath)),
                    Position     = new Position(line, character)
                });
        }
        catch { return null; }
    }

    public async Task<Location[]?> GetDefinitionAsync(string filePath, int line, int character)
    {
        if (!_initialized) return null;
        try
        {
            var result = await _rpc.InvokeWithParameterObjectAsync<JsonElement>(
                "textDocument/definition",
                new TextDocumentPositionParams(
                    new TextDocumentIdentifier(PathToUri(filePath)),
                    new Position(line, character)));

            if (result.ValueKind == JsonValueKind.Array)
                return JsonSerializer.Deserialize<Location[]>(result);
            if (result.ValueKind == JsonValueKind.Object)
                return [JsonSerializer.Deserialize<Location>(result)!];
            return null;
        }
        catch { return null; }
    }

    // ========== ユーティリティ ==========

    public static string PathToUri(string path) =>
        new Uri(path.Replace('\\', '/')).AbsoluteUri;

    public static string UriToPath(string uri) =>
        new Uri(uri).LocalPath;

    public void Dispose()
    {
        _rpc.Dispose();
        try { _server.Kill(); } catch { }
        _server.Dispose();
    }
}

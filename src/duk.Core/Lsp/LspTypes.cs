using System.Text.Json.Serialization;

namespace duk.Core.Lsp;

// ========== 基本型 ==========

public record Position(
    [property: JsonPropertyName("line")]      int Line,
    [property: JsonPropertyName("character")] int Character);

public record Range(
    [property: JsonPropertyName("start")] Position Start,
    [property: JsonPropertyName("end")]   Position End);

public record Location(
    [property: JsonPropertyName("uri")]   string Uri,
    [property: JsonPropertyName("range")] Range Range);

public record TextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri);

public record TextDocumentPositionParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")]     Position Position);

// ========== Initialize ==========

public class InitializeParams
{
    [JsonPropertyName("processId")]    public int?   ProcessId    { get; init; }
    [JsonPropertyName("rootUri")]      public string? RootUri     { get; init; }
    [JsonPropertyName("capabilities")] public object  Capabilities { get; init; } = new ClientCapabilities();
}

public class ClientCapabilities
{
    [JsonPropertyName("textDocument")] public TextDocumentClientCapabilities TextDocument { get; init; } = new();
}

public class TextDocumentClientCapabilities
{
    [JsonPropertyName("completion")]   public CompletionClientCapabilities Completion   { get; init; } = new();
    [JsonPropertyName("hover")]        public HoverClientCapabilities      Hover        { get; init; } = new();
    [JsonPropertyName("publishDiagnostics")] public object PublishDiagnostics { get; init; } = new { };
}

public class CompletionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")] public bool DynamicRegistration { get; init; } = false;
    [JsonPropertyName("completionItem")]      public object CompletionItem    { get; init; } = new { snippetSupport = false };
}

public class HoverClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")] public bool DynamicRegistration { get; init; } = false;
    [JsonPropertyName("contentFormat")]       public string[] ContentFormat   { get; init; } = ["plaintext", "markdown"];
}

// ========== textDocument/didOpen ==========

public class DidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")] public TextDocumentItem TextDocument { get; init; } = null!;
}

public class TextDocumentItem
{
    [JsonPropertyName("uri")]        public string Uri        { get; init; } = "";
    [JsonPropertyName("languageId")] public string LanguageId { get; init; } = "";
    [JsonPropertyName("version")]    public int    Version    { get; init; } = 1;
    [JsonPropertyName("text")]       public string Text       { get; init; } = "";
}

// ========== textDocument/didChange ==========

public class DidChangeTextDocumentParams
{
    [JsonPropertyName("textDocument")]   public VersionedTextDocumentIdentifier TextDocument   { get; init; } = null!;
    [JsonPropertyName("contentChanges")] public TextDocumentContentChangeEvent[] ContentChanges { get; init; } = [];
}

public class VersionedTextDocumentIdentifier
{
    [JsonPropertyName("uri")]     public string Uri     { get; init; } = "";
    [JsonPropertyName("version")] public int    Version { get; init; }
}

public class TextDocumentContentChangeEvent
{
    [JsonPropertyName("text")] public string Text { get; init; } = "";
}

// ========== textDocument/didClose ==========

public class DidCloseTextDocumentParams
{
    [JsonPropertyName("textDocument")] public TextDocumentIdentifier TextDocument { get; init; } = null!;
}

// ========== Diagnostics ==========

public class PublishDiagnosticsParams
{
    [JsonPropertyName("uri")]         public string       Uri         { get; init; } = "";
    [JsonPropertyName("diagnostics")] public Diagnostic[] Diagnostics { get; init; } = [];
}

public class Diagnostic
{
    [JsonPropertyName("range")]    public Range  Range    { get; init; } = null!;
    [JsonPropertyName("severity")] public int?   Severity { get; init; }
    [JsonPropertyName("message")]  public string Message  { get; init; } = "";
    [JsonPropertyName("source")]   public string? Source  { get; init; }

    // 1=Error 2=Warning 3=Info 4=Hint
    public bool IsError   => Severity == 1;
    public bool IsWarning => Severity == 2;
}

// ========== Completion ==========

public class CompletionParams
{
    [JsonPropertyName("textDocument")] public TextDocumentIdentifier TextDocument { get; init; } = null!;
    [JsonPropertyName("position")]     public Position Position                   { get; init; } = null!;
    [JsonPropertyName("context")]      public CompletionContext? Context           { get; init; }
}

public class CompletionContext
{
    [JsonPropertyName("triggerKind")]      public int     TriggerKind      { get; init; } = 1;
    [JsonPropertyName("triggerCharacter")] public string? TriggerCharacter { get; init; }
}

public class CompletionList
{
    [JsonPropertyName("isIncomplete")] public bool             IsIncomplete { get; init; }
    [JsonPropertyName("items")]        public CompletionItem[] Items        { get; init; } = [];
}

public class CompletionItem
{
    [JsonPropertyName("label")]         public string  Label         { get; init; } = "";
    [JsonPropertyName("kind")]          public int?    Kind          { get; init; }
    [JsonPropertyName("detail")]        public string? Detail        { get; init; }
    [JsonPropertyName("documentation")] public object? Documentation { get; init; }
    [JsonPropertyName("insertText")]    public string? InsertText    { get; init; }
    [JsonPropertyName("filterText")]    public string? FilterText    { get; init; }

    // kind: 1=Text 2=Method 3=Function 4=Constructor 5=Field 6=Variable 7=Class 9=Interface 14=Keyword
    public string KindIcon => Kind switch
    {
        2 or 3 => "⚙",
        4      => "🔨",
        5 or 6 => "📦",
        7      => "🔷",
        9      => "🔶",
        14     => "🔑",
        _      => "📝"
    };
}

// ========== Hover ==========

public class HoverParams
{
    [JsonPropertyName("textDocument")] public TextDocumentIdentifier TextDocument { get; init; } = null!;
    [JsonPropertyName("position")]     public Position Position                   { get; init; } = null!;
}

public class Hover
{
    [JsonPropertyName("contents")] public object? Contents { get; init; }
    [JsonPropertyName("range")]    public Range?  Range    { get; init; }

    public string GetText()
    {
        if (Contents is string s) return s;
        if (Contents is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.String)
                return je.GetString() ?? "";
            if (je.TryGetProperty("value", out var v)) return v.GetString() ?? "";
        }
        return "";
    }
}

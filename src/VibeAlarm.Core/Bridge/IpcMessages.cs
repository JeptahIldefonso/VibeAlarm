using System.Text.Json;
using System.Drawing;

namespace VibeAlarm.Bridge
{
    /// <summary>
    /// The wire contract between the React SPA and the WinForms host, mirrored on the
    /// TypeScript side by ui/src/bridge/protocol.ts. Every message is one JSON object
    /// posted through WebView2's PostWebMessageAsJson / window.chrome.webview.postMessage
    /// channel, discriminated by <c>kind</c>:
    ///
    ///   • request  — { kind: "req",  id, type, payload }        (React → host)
    ///   • response — { kind: "res",  id, ok, payload | error }  (host → React)
    ///   • push     — { kind: "push", type, payload }            (host → React, unsolicited)
    ///
    /// Property names are camelCase on the wire (see <see cref="IpcJson"/>), matching
    /// the TypeScript interfaces exactly.
    /// </summary>
    public static class IpcJson
    {
        /// <summary>CamelCase + case-insensitive binding, shared by both directions.</summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
    }

    /// <summary>A request from React: <c>{ kind: "req", id, type, payload }</c>.</summary>
    public sealed class IpcRequest
    {
        public string Kind { get; set; } = "req";
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public JsonElement? Payload { get; set; }
    }

    /// <summary>The host's reply to a request: <c>{ kind: "res", id, ok, payload }</c>
    /// or <c>{ kind: "res", id, ok: false, error }</c>.</summary>
    public sealed class IpcResponse
    {
        public string Kind { get; set; } = "res";
        public long Id { get; set; }
        public bool Ok { get; set; }
        public object? Payload { get; set; }
        public string? Error { get; set; }

        public static IpcResponse Success(long id, object? payload) => new() { Id = id, Ok = true, Payload = payload };
        public static IpcResponse Failure(long id, string error) => new() { Id = id, Ok = false, Error = error };
    }

    /// <summary>An unsolicited host → React event: <c>{ kind: "push", type, payload }</c>.</summary>
    public sealed class IpcPush
    {
        public string Kind { get; set; } = "push";
        public string Type { get; set; } = string.Empty;
        public object? Payload { get; set; }
    }

    /// <summary>
    /// Wire form of one <see cref="UI.Theming.AccentOption"/> for <c>getAccentLibrary</c>.
    /// Colors travel as #RRGGBB strings — System.Drawing.Color does not serialize to
    /// anything CSS-usable, and the React side sets CSS variables verbatim from these.
    /// </summary>
    public sealed class AccentDto
    {
        public string Key { get; set; } = string.Empty;
        public string Base { get; set; } = string.Empty;
        public string Hover { get; set; } = string.Empty;
        public string OnAccent { get; set; } = string.Empty;

        public static AccentDto From(UI.Theming.AccentOption option) => new()
        {
            Key = option.Key,
            Base = ToHex(option.Base),
            Hover = ToHex(option.Hover),
            OnAccent = ToHex(option.OnAccent),
        };

        private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}

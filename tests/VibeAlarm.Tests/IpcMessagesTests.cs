using System.Text.Json;
using VibeAlarm.Bridge;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Round-trip tests for the IPC wire contract — the C# side of what
    /// ui/src/bridge/protocol.ts mirrors. These pin the exact JSON shape React
    /// expects: camelCase names, "kind" discriminator, payload-as-raw-JSON.
    /// </summary>
    public sealed class IpcMessagesTests
    {
        [Fact]
        public void Request_RoundTrips_WithCamelCaseKindAndType()
        {
            const string json = """{"kind":"req","id":42,"type":"createTask","payload":{"title":"Test"}}""";

            IpcRequest? request = JsonSerializer.Deserialize<IpcRequest>(json, IpcJson.Options);

            Assert.NotNull(request);
            Assert.Equal("req", request.Kind);
            Assert.Equal(42L, request.Id);
            Assert.Equal("createTask", request.Type);
            Assert.NotNull(request.Payload);
            Assert.Equal("Test", request.Payload.Value.GetProperty("title").GetString());
        }

        [Fact]
        public void Request_Deserializes_WithoutPayload()
        {
            const string json = """{"kind":"req","id":7,"type":"getTasks"}""";

            IpcRequest? request = JsonSerializer.Deserialize<IpcRequest>(json, IpcJson.Options);

            Assert.NotNull(request);
            Assert.Null(request.Payload);
        }

        [Fact]
        public void Response_Serializes_ToCamelCaseShapeReactExpects()
        {
            string json = JsonSerializer.Serialize(
                IpcResponse.Success(99, new { title = "Hi" }), IpcJson.Options);

            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal("res", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal(99L, doc.RootElement.GetProperty("id").GetInt64());
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("Hi", doc.RootElement.GetProperty("payload").GetProperty("title").GetString());
            // camelCase, exactly the TS IpcResponse interface.
            Assert.False(doc.RootElement.TryGetProperty("Ok", out _));
        }

        [Fact]
        public void FailureResponse_CarriesErrorInsteadOfPayload()
        {
            string json = JsonSerializer.Serialize(
                IpcResponse.Failure(5, "Task not found."), IpcJson.Options);

            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("Task not found.", doc.RootElement.GetProperty("error").GetString());
            // Null payload travels as JSON null — React branches on `ok` and reads
            // `error`, so the null never reaches app code.
            Assert.True(doc.RootElement.TryGetProperty("payload", out JsonElement payload));
            Assert.Equal(JsonValueKind.Null, payload.ValueKind);
        }

        [Fact]
        public void Push_Serializes_WithCamelCaseType()
        {
            string json = JsonSerializer.Serialize(
                new IpcPush { Type = "alarmFired", Payload = new { title = "Wake up" } },
                IpcJson.Options);

            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal("push", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal("alarmFired", doc.RootElement.GetProperty("type").GetString());
            Assert.Equal("Wake up", doc.RootElement.GetProperty("payload").GetProperty("title").GetString());
        }

        [Fact]
        public void Push_Payload_CanCarryNestedObjects()
        {
            string json = """{"kind":"push","type":"ambientChanged","payload":{"playing":true,"volume":42,"name":"Rain"}}""";

            IpcPush? push = JsonSerializer.Deserialize<IpcPush>(json, IpcJson.Options);

            Assert.NotNull(push);
            JsonElement payload = (JsonElement)push.Payload!;
            Assert.True(payload.GetProperty("playing").GetBoolean());
            Assert.Equal(42, payload.GetProperty("volume").GetInt32());
            Assert.Equal("Rain", payload.GetProperty("name").GetString());
        }

        [Fact]
        public void AccentDto_From_CarriesHexCssColors()
        {
            var option = new UI.Theming.AccentOption(
                "Test Accent",
                System.Drawing.Color.FromArgb(0x22, 0xC5, 0x5E),
                System.Drawing.Color.FromArgb(0x34, 0xD3, 0x74),
                System.Drawing.Color.Black);

            AccentDto dto = AccentDto.From(option);

            Assert.Equal("Test Accent", dto.Key);
            Assert.Equal("#22C55E", dto.Base);
            Assert.Equal("#34D374", dto.Hover);
            Assert.Equal("#000000", dto.OnAccent);
        }

        [Fact]
        public void AccentDto_RoundTrips_ThroughJson()
        {
            var option = new UI.Theming.AccentOption(
                "Neo Blue",
                System.Drawing.Color.FromArgb(0x4C, 0x8D, 0xFF),
                System.Drawing.Color.FromArgb(0x6F, 0xA1, 0xFF),
                System.Drawing.Color.White);

            string json = JsonSerializer.Serialize(AccentDto.From(option), IpcJson.Options);

            AccentDto? dto = JsonSerializer.Deserialize<AccentDto>(json, IpcJson.Options);
            Assert.NotNull(dto);
            Assert.Equal("Neo Blue", dto.Key);
            Assert.Equal("#4C8DFF", dto.Base);
            Assert.Equal("#6FA1FF", dto.Hover);
            Assert.Equal("#FFFFFF", dto.OnAccent);
        }
    }
}

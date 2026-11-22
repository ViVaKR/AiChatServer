using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiChatServer.Grpc.Ollama;

// ---- Ollama /api/chat 요청/응답 모델 (JSON은 snake_case) ----------------------

public sealed record OllamaMessage(string Role, string Content);

public sealed record OllamaChatRequest(
    string Model,
    IReadOnlyList<OllamaMessage> Messages,
    bool Stream,
    bool? Think);

public sealed record OllamaResponseMessage(string? Content, string? Thinking);

public sealed record OllamaChatResponse(
    OllamaResponseMessage? Message,
    bool Done,
    int? PromptEvalCount,
    int? EvalCount);

public sealed class OllamaException(HttpStatusCode status, string body)
    : Exception($"Ollama 오류 {(int)status}: {body}")
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Ollama /api/chat 스트림(NDJSON)을 읽는 얇은 클라이언트.</summary>
public sealed class OllamaClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public async IAsyncEnumerable<OllamaChatResponse> ChatStreamAsync(
        OllamaChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request, options: JsonOpts)
        };

        // 헤더만 받고 바로 반환 → 본문은 토큰이 생성되는 대로 읽는다
        using var res = await http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!res.IsSuccessStatusCode)
            throw new OllamaException(res.StatusCode, await res.Content.ReadAsStringAsync(ct));

        await using var body = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(body);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOpts);
            if (chunk is null) continue;

            yield return chunk;
            if (chunk.Done) yield break;
        }
    }
}

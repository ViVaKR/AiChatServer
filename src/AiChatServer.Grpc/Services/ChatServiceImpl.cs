using System.Diagnostics;
using System.Text;
using AiChatServer.Contracts.V1;
using AiChatServer.Grpc.Ollama;
using AiChatServer.Grpc.Options;
using AiChatServer.Grpc.Retrieval;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace AiChatServer.Grpc.Services;

public sealed class ChatServiceImpl(
    OllamaClient ollama,
    IRetriever retriever,
    IOptions<PersonasOptions> personas,
    ILogger<ChatServiceImpl> logger)
    : ChatService.ChatServiceBase
{
    private readonly PersonasOptions _personas = personas.Value;

    /// <summary>rpc Complete: 스트림을 모아서 한 번에 돌려준다 (thinking 은 제외).</summary>
    public override async Task<ChatReply> Complete(ChatRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var (persona, ollamaRequest) = await BuildAsync(request, ct);

        var sw = Stopwatch.StartNew();
        var content = new StringBuilder();
        Usage? usage = null;

        try
        {
            await foreach (var c in ollama.ChatStreamAsync(ollamaRequest, ct))
            {
                if (!string.IsNullOrEmpty(c.Message?.Content))
                    content.Append(c.Message.Content);
                if (c.Done)
                    usage = ToUsage(c);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw Translate(ex);
        }

        logger.LogInformation("Complete 완료 model={Model} {Ms}ms tokens={Prompt}/{Completion}",
            persona.Model, sw.ElapsedMilliseconds, usage?.PromptTokens, usage?.CompletionTokens);

        return new ChatReply
        {
            ConversationId = request.ConversationId,
            Message = new ChatMessage { Role = ChatRole.Assistant, Content = content.ToString() },
            Usage = usage
        };
    }

    /// <summary>rpc StreamChat: Ollama 의 thinking / content 를 그대로 구분해서 흘려보낸다.</summary>
    public override async Task StreamChat(
        ChatRequest request,
        IServerStreamWriter<ChatChunk> stream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var (persona, ollamaRequest) = await BuildAsync(request, ct);

        var sw = Stopwatch.StartNew();
        Usage? usage = null;

        try
        {
            await foreach (var c in ollama.ChatStreamAsync(ollamaRequest, ct))
            {
                if (!string.IsNullOrEmpty(c.Message?.Thinking))
                    await stream.WriteAsync(NewChunk(request, ChunkKind.Thinking, c.Message.Thinking), ct);

                if (!string.IsNullOrEmpty(c.Message?.Content))
                    await stream.WriteAsync(NewChunk(request, ChunkKind.Content, c.Message.Content), ct);

                if (c.Done)
                    usage = ToUsage(c);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw Translate(ex);
        }

        await stream.WriteAsync(new ChatChunk
        {
            ConversationId = request.ConversationId,
            Kind = ChunkKind.Content,
            Done = true,
            Usage = usage
        }, ct);

        // 질문/답변 본문은 개인적인 내용이 많으니 로그에 남기지 않는다.
        logger.LogInformation("StreamChat 완료 model={Model} {Ms}ms tokens={Prompt}/{Completion}",
            persona.Model, sw.ElapsedMilliseconds, usage?.PromptTokens, usage?.CompletionTokens);
    }

    // ---- 요청 조립 ---------------------------------------------------------

    private async Task<(PersonaOptions Persona, OllamaChatRequest Request)> BuildAsync(
        ChatRequest request, CancellationToken ct)
    {
        if (!_personas.TryGetValue(request.Persona, out var persona))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"설정되지 않은 페르소나입니다: {request.Persona}"));

        if (request.Messages.Count == 0 ||
            request.Messages[request.Messages.Count - 1].Role != ChatRole.User)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "마지막 메시지는 user 메시지여야 합니다."));

        var messages = new List<OllamaMessage>();

        // SystemPrompt 가 null 이면 보내지 않는다 → Modelfile 의 SYSTEM(법등) 이 그대로 적용된다.
        if (!string.IsNullOrWhiteSpace(persona.SystemPrompt))
            messages.Add(new OllamaMessage("system", persona.SystemPrompt));

        var last = request.Messages.Count - 1;
        for (var i = 0; i < last; i++)
        {
            var m = request.Messages[i];
            var role = m.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                _ => null
            };
            if (role is null || string.IsNullOrWhiteSpace(m.Content)) continue;
            messages.Add(new OllamaMessage(role, m.Content));
        }

        // RAG: 원문은 system 이 아니라 '이번 질문'에 붙인다.
        // (요청에 system 메시지가 들어가면 Modelfile 의 SYSTEM 을 대체할 수 있어 법등 페르소나가 사라질 수 있음)
        var question = request.Messages[last].Content;
        var passages = await retriever.RetrieveAsync(question, ct);
        messages.Add(new OllamaMessage("user", passages.Count == 0 ? question : Ground(question, passages)));

        return (persona, new OllamaChatRequest(persona.Model, messages, Stream: true, Think: persona.Think));
    }

    private static string Ground(string question, IReadOnlyList<RetrievedPassage> passages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("【경전 원문】");
        for (var i = 0; i < passages.Count; i++)
            sb.AppendLine($"[{i + 1}] ({passages[i].Source}) {passages[i].Text}");
        sb.AppendLine();
        sb.AppendLine("위 원문에 근거해서만 인용하십시오. 원문에 없는 구절은 만들어내지 말고, 인용할 때는 출처를 함께 밝히십시오.");
        sb.AppendLine();
        sb.AppendLine("【질문】");
        sb.Append(question);
        return sb.ToString();
    }

    // ---- 변환 / 오류 ---------------------------------------------------------

    private static ChatChunk NewChunk(ChatRequest request, ChunkKind kind, string delta) => new()
    {
        ConversationId = request.ConversationId,
        Kind = kind,
        Delta = delta
    };

    private static Usage ToUsage(OllamaChatResponse c) => new()
    {
        PromptTokens = c.PromptEvalCount ?? 0,
        CompletionTokens = c.EvalCount ?? 0
    };

    private RpcException Translate(Exception ex)
    {
        logger.LogError(ex, "Ollama 호출 실패");
        return ex switch
        {
            RpcException rpc => rpc,
            OllamaException o => new RpcException(new Status(StatusCode.Internal, o.Message)),
            HttpRequestException => new RpcException(new Status(StatusCode.Unavailable, "Ollama 서버에 연결할 수 없습니다.")),
            OperationCanceledException => new RpcException(new Status(StatusCode.DeadlineExceeded, "Ollama 응답 시간이 초과되었습니다.")),
            _ => new RpcException(new Status(StatusCode.Internal, "알 수 없는 오류가 발생했습니다."))
        };
    }
}

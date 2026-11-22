namespace AiChatServer.Grpc.Retrieval;

/// <summary>검색된 경전 원문 한 조각. Source 예) "금강경 제5분", Text 는 번역 본문.</summary>
public sealed record RetrievedPassage(string Source, string Text);

/// <summary>RAG 삽입 지점. 지금은 빈 구현, 나중에 pgvector 구현으로 교체한다.</summary>
public interface IRetriever
{
    Task<IReadOnlyList<RetrievedPassage>> RetrieveAsync(string question, CancellationToken ct);
}

public sealed class NullRetriever : IRetriever
{
    public Task<IReadOnlyList<RetrievedPassage>> RetrieveAsync(string question, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<RetrievedPassage>>([]);
}

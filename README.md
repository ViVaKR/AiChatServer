# AiChatServer.Contracts

[![NuGet](https://img.shields.io/nuget/v/AiChatServer.Contracts.svg)](https://www.nuget.org/packages/AiChatServer.Contracts)

[AiChatServer](https://github.com/ViVaKR/AiChatServer)와 통신하기 위한 gRPC / Protocol Buffers 계약 패키지입니다.
채팅 허브나 호스트 애플리케이션이 이 패키지를 참조하면 메시지 타입을 직접 정의하지 않고도 서버와 같은 규약을 공유할 수 있습니다.

## 설치

```bash
dotnet add package AiChatServer.Contracts
```

서버를 호출하려면 gRPC 클라이언트 패키지가 따로 필요합니다.

```bash
dotnet add package Grpc.Net.ClientFactory
```

## 포함 내용

proto 패키지는 `aichat.v1`, C# 네임스페이스는 `AiChatServer.Contracts.V1` 입니다.
서버용 추상 클래스(`ChatService.ChatServiceBase`)와 클라이언트(`ChatService.ChatServiceClient`)가 모두 생성되어 들어 있습니다.

| 구분 | 타입 | 설명 |
|---|---|---|
| 서비스 | `ChatService` | `Complete`(단건 응답), `StreamChat`(서버 스트리밍) |
| 요청 | `ChatRequest` | `conversation_id`, `persona`, `messages` |
| 응답 | `ChatReply` | 단건 응답 메시지와 `Usage` |
| 청크 | `ChatChunk` | 스트림 한 조각 (`kind`, `delta`, `done`, `usage`) |
| 메시지 | `ChatMessage`, `Usage` | 역할별 본문, 토큰 사용량 |
| 열거형 | `ChatRole`, `Persona`, `ChunkKind` | 아래 표 참고 |

proto 의 열거형 값은 C# 에서 접두사가 빠진 이름으로 생성됩니다.

| proto | C# |
|---|---|
| `CHAT_ROLE_USER` / `CHAT_ROLE_ASSISTANT` | `ChatRole.User` / `ChatRole.Assistant` |
| `PERSONA_BEOPDEUNG` | `Persona.Beopdeung` |
| `CHUNK_KIND_THINKING` / `CHUNK_KIND_CONTENT` | `ChunkKind.Thinking` / `ChunkKind.Content` |

## 사용 예

### 클라이언트 등록

```csharp
using AiChatServer.Contracts.V1;

builder.Services.AddGrpcClient<ChatService.ChatServiceClient>(o =>
    o.Address = new Uri("https://your-aichat-server"));
```

### 스트리밍 호출

```csharp
var request = new ChatRequest
{
    ConversationId = conversationId,
    Persona = Persona.Beopdeung,
    Messages =
    {
        new ChatMessage { Role = ChatRole.User, Content = "마음이 어지러울 때는 어떻게 해야 하나요?" }
    }
};

using var call = client.StreamChat(request, cancellationToken: ct);

await foreach (var chunk in call.ResponseStream.ReadAllAsync(ct))
{
    if (chunk.Done)
    {
        Console.WriteLine($"tokens: {chunk.Usage.PromptTokens}/{chunk.Usage.CompletionTokens}");
        continue;
    }

    switch (chunk.Kind)
    {
        case ChunkKind.Thinking: /* 사고 과정: UI 에서 접어서 표시 */ break;
        case ChunkKind.Content:  /* 실제 답변 */ break;
    }
}
```

### 단건 호출

```csharp
ChatReply reply = await client.CompleteAsync(request, cancellationToken: ct);
Console.WriteLine(reply.Message.Content);
```

## 동작 규약 (AiChatServer 기준)

- `StreamChat` 은 `Thinking` 청크와 `Content` 청크를 구분해서 보내고, 마지막에 `done = true` 청크 하나를 보냅니다. `usage` 는 이 마지막 청크에만 채워집니다.
- `Complete` 는 사고 과정을 제외한 답변 본문만 모아서 돌려줍니다.
- `persona` 는 반드시 지정해야 합니다. 서버에 설정되지 않은 페르소나는 `InvalidArgument` 로 거절됩니다.
- `messages` 의 마지막 항목은 `ChatRole.User` 여야 합니다. 아니면 `InvalidArgument` 입니다.

## 호환성

- `.proto` 의 필드 번호는 한 번 배포하면 바꾸지 않습니다. 필드를 지울 때는 `reserved` 로 막아 둡니다.
- 필드 추가는 Minor 버전, 호환되지 않는 변경은 Major 버전(`aichat.v2` 패키지)으로 올립니다.

## 라이선스

[MIT](https://github.com/ViVaKR/AiChatServer/blob/main/LICENSE)

# AiChatServer.Contracts

`AiChatServer.Grpc`와 연동하기 위한 gRPC · Protocol Buffers 계약 패키지입니다[cite: 15, 17].
여러 서비스와 채팅 허브가 동일한 메시지 규약과 페르소나 정의를 공유할 수 있도록 설계되었습니다[cite: 15, 26].

## 주요 구성 (`AiChatServer.Contracts.V1`)

- **`ChatService`**: 단건 응답을 위한 `Complete`와 실시간 토큰 스트리밍을 위한 `StreamChat` RPC를 제공합니다[cite: 15, 24].
- **대화 모델 (`ChatRequest`, `ChatReply`, `ChatMessage`, `Usage`)**: `conversation_id` 기반의 대화 맥락 전달, 역할(`CHAT_ROLE_USER`, `CHAT_ROLE_ASSISTANT`) 구분, 토큰 사용량 집계를 지원합니다[cite: 15].
- **페르소나 & 스트림 청크 (`Persona`, `ChunkKind`, `ChatChunk`)**: `PERSONA_BEOPDEUNG` 등 정의된 페르소나와 사고 과정(`CHUNK_KIND_THINKING`), 본문(`CHUNK_KIND_CONTENT`) 스트림을 명확히 구분합니다[cite: 15, 24].

## 활용 방향

채팅 허브 및 호스트 애플리케이션에서는 별도의 계약 타입을 중복 정의하지 않고 본 패키지를 참조함으로써, `AiChatServer`와의 통신 규약을 일관되게 유지할 수 있습니다[cite: 15, 16, 26, 31].

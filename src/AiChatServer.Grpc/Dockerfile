# ============================================================
# AiChatServer.Grpc (ARM64 / Talos 배포용)
# 빌드 컨텍스트는 솔루션 루트:
#   docker buildx build --platform linux/arm64 \
#     -f src/AiChatServer.Grpc/Dockerfile -t kimbumjun/aichat-server:latest --push .
# ============================================================

# ---------- 1단계: restore ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 공통 속성 / 중앙 패키지 관리 파일은 csproj 보다 먼저 복사해야 합니다.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]

# csproj 만 먼저 복사해서 restore 레이어를 캐시합니다.
COPY ["src/AiChatServer.Contracts/AiChatServer.Contracts.csproj", "src/AiChatServer.Contracts/"]
COPY ["src/AiChatServer.Grpc/AiChatServer.Grpc.csproj", "src/AiChatServer.Grpc/"]
RUN dotnet restore "src/AiChatServer.Grpc/AiChatServer.Grpc.csproj"

# 나머지 소스 복사 (.dockerignore 가 bin / obj / artifacts 를 걸러냅니다)
COPY . .

# ---------- 2단계: 퍼블리시 ----------
# UseArtifactsOutput 을 켜 두었기 때문에 -o 로 출력 경로를 명시합니다.
FROM build AS publish
RUN dotnet publish "src/AiChatServer.Grpc/AiChatServer.Grpc.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- 3단계: 최종 실행 이미지 ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 포트는 appsettings.json 의 Kestrel Endpoints 가 정합니다 (8080 gRPC / 8081 관리용).
# ASPNETCORE_HTTP_PORTS 는 일부러 설정하지 않습니다 (Endpoints 설정과 충돌).
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "AiChatServer.Grpc.dll"]

# SearXNG MCP Server — Docker 이미지
#
# 빌드:  docker build -t searxng-search-mcp .
# 실행:  docker run -d -p 5000:5000 -e SEARXNG__BASEURL=http://<host>:8080 searxng-search-mcp
# 또는:  docker compose up -d --build   (SearXNG + MCP 서버를 함께 실행)

# ---------- 빌드 ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 프로젝트 파일을 먼저 복사해 Docker 레이어 캐싱을 활용
COPY SearXngSearch/SearXngSearch.csproj SearXngSearch/
COPY SearXngSearch.Mcp/SearXngSearch.Mcp.csproj SearXngSearch.Mcp/
RUN dotnet restore SearXngSearch.Mcp/SearXngSearch.Mcp.csproj

# 소스를 복사하고 publish
COPY SearXngSearch/ SearXngSearch/
COPY SearXngSearch.Mcp/ SearXngSearch.Mcp/
RUN dotnet publish SearXngSearch.Mcp/SearXngSearch.Mcp.csproj -c Release -o /app/publish --self-contained false

# ---------- 런타임 ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# 컨테이너 내부에서 모든 인터페이스에 바인딩 (외부 접근에 필수)
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
# SearXNG 인스턴스 주소 (-e SEARXNG__BASEURL=... 로 오버라이드)
ENV SEARXNG__BASEURL=http://localhost:8080

EXPOSE 5000
ENTRYPOINT ["./SearXngSearch.Mcp"]

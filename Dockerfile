FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["DiscordStreamBotBackend/DiscordStreamBotBackend.csproj", "DiscordStreamBotBackend/"]
RUN dotnet restore "DiscordStreamBotBackend/DiscordStreamBotBackend.csproj"

COPY . .
WORKDIR "/src/DiscordStreamBotBackend"
RUN dotnet publish "DiscordStreamBotBackend.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 5003

ENTRYPOINT ["dotnet", "DiscordStreamBotBackend.dll"]

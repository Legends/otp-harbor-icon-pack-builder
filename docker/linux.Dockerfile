FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

WORKDIR /source
COPY . .

RUN dotnet restore OtpHarbor.IconPackBuilder.sln
RUN dotnet build OtpHarbor.IconPackBuilder.sln \
    --configuration Release \
    --no-restore \
    --nologo
RUN dotnet test OtpHarbor.IconPackBuilder.sln \
    --configuration Release \
    --no-build \
    --nologo \
    --verbosity minimal
RUN dotnet publish src/OtpHarbor.IconPackBuilder/OtpHarbor.IconPackBuilder.csproj \
    --configuration Release \
    --no-restore \
    --nologo \
    --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS final

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    XDG_CACHE_HOME=/tmp/otp-harbor-cache

WORKDIR /app
COPY --from=build /app .

RUN mkdir -p /output "$XDG_CACHE_HOME" \
    && chown -R "$APP_UID:$APP_UID" /app /output "$XDG_CACHE_HOME"

USER $APP_UID
ENTRYPOINT ["dotnet", "OtpHarbor.IconPackBuilder.dll"]

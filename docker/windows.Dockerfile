# escape=`

FROM mcr.microsoft.com/dotnet/sdk:10.0-windowsservercore-ltsc2022 AS build

SHELL ["powershell", "-NoLogo", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

WORKDIR C:\source
COPY . .

RUN dotnet restore OtpHarbor.IconPackBuilder.sln
RUN dotnet build OtpHarbor.IconPackBuilder.sln `
    --configuration Release `
    --no-restore `
    --nologo
RUN dotnet test OtpHarbor.IconPackBuilder.sln `
    --configuration Release `
    --no-build `
    --nologo `
    --verbosity minimal
RUN dotnet publish src\OtpHarbor.IconPackBuilder\OtpHarbor.IconPackBuilder.csproj `
    --configuration Release `
    --no-restore `
    --nologo `
    --output C:\app

FROM mcr.microsoft.com/dotnet/runtime:10.0-windowsservercore-ltsc2022 AS final

SHELL ["powershell", "-NoLogo", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 `
    DOTNET_NOLOGO=1

WORKDIR C:\app
COPY --from=build C:\app .

RUN New-Item -ItemType Directory -Path C:\output -Force | Out-Null; `
    & icacls.exe C:\output /grant 'ContainerUser:(OI)(CI)M' | Out-Null; `
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

USER ContainerUser
ENTRYPOINT ["dotnet", "OtpHarbor.IconPackBuilder.dll"]


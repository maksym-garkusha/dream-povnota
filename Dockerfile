# Збірка на повному SDK, запуск на мінімальному образі без shell і пакетного менеджера.
# Образи закріплено за digest списку платформ: тег можна переписати, digest ні.
# Dependabot (docker) оновлює тег і digest разом
FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
WORKDIR /src
COPY global.json DreamPovnota.slnx ./
COPY src/DreamPovnota.Core/DreamPovnota.Core.csproj src/DreamPovnota.Core/
COPY src/DreamPovnota.Web/DreamPovnota.Web.csproj src/DreamPovnota.Web/
RUN dotnet restore src/DreamPovnota.Web/DreamPovnota.Web.csproj -r linux-x64 -p:PublishReadyToRun=true
COPY src/DreamPovnota.Core/ src/DreamPovnota.Core/
COPY src/DreamPovnota.Web/ src/DreamPovnota.Web/
RUN dotnet publish src/DreamPovnota.Web/DreamPovnota.Web.csproj -c Release -r linux-x64 \
    --self-contained false --no-restore -p:PublishReadyToRun=true -o /app

# composite: фреймворк уже скомпільований наперед, швидший старт на 0,1 CPU;
# extra: ICU і часові пояси для українського впорядкування і часу за Києвом
FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-composite-extra@sha256:d7ce3896765dd02661041a8ca8db6ed8f0c59e1a65f64862dfc691ae2d295708
WORKDIR /app
COPY --from=build /app .
# gen0 до 32 МБ: без цього після збірки xlsx процес тримав понад сотню мегабайтів ще не зібраного сміття
ENV ASPNETCORE_HTTP_PORTS=10000 DOTNET_gcServer=0 DOTNET_gcConcurrent=0 DOTNET_GCgen0size=0x2000000
EXPOSE 10000
ENTRYPOINT ["dotnet", "DreamPovnota.Web.dll"]

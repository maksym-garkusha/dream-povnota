# Збірка на повному SDK, запуск на мінімальному образі без shell і пакетного менеджера
FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-composite-extra
WORKDIR /app
COPY --from=build /app .
# gen0 до 32 МБ: без цього після збірки xlsx процес тримав понад сотню мегабайтів ще не зібраного сміття
ENV ASPNETCORE_HTTP_PORTS=10000 DOTNET_gcServer=0 DOTNET_gcConcurrent=0 DOTNET_GCgen0size=0x2000000
EXPOSE 10000
ENTRYPOINT ["dotnet", "DreamPovnota.Web.dll"]

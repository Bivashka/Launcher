FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/BivLauncher.Api/BivLauncher.Api.csproj backend/BivLauncher.Api/
RUN dotnet restore backend/BivLauncher.Api/BivLauncher.Api.csproj

COPY backend/BivLauncher.Api/ backend/BivLauncher.Api/
RUN dotnet publish backend/BivLauncher.Api/BivLauncher.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BivLauncher.Api.dll"]

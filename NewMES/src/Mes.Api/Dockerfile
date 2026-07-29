FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "Directory.Build.props", "Mes.slnx", "./"]
COPY ["src/Mes.Domain/Mes.Domain.csproj", "src/Mes.Domain/"]
COPY ["src/Mes.Infrastructure/Mes.Infrastructure.csproj", "src/Mes.Infrastructure/"]
COPY ["src/Mes.Api/Mes.Api.csproj", "src/Mes.Api/"]
RUN dotnet restore "src/Mes.Api/Mes.Api.csproj"

COPY src/ src/
RUN dotnet publish "src/Mes.Api/Mes.Api.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Mes.Api.dll"]

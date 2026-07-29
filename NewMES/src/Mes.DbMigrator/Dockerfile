FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "Directory.Build.props", "Mes.slnx", "./"]
COPY ["src/Mes.Domain/Mes.Domain.csproj", "src/Mes.Domain/"]
COPY ["src/Mes.Infrastructure/Mes.Infrastructure.csproj", "src/Mes.Infrastructure/"]
COPY ["src/Mes.DbMigrator/Mes.DbMigrator.csproj", "src/Mes.DbMigrator/"]
RUN dotnet restore "src/Mes.DbMigrator/Mes.DbMigrator.csproj"

COPY src/ src/
RUN dotnet publish "src/Mes.DbMigrator/Mes.DbMigrator.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Mes.DbMigrator.dll"]
CMD ["migrate"]

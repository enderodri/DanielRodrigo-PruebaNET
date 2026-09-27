FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/DanielRodrigo.PruebaNET.Application/DanielRodrigo.PruebaNET.Application.csproj src/DanielRodrigo.PruebaNET.Application/
COPY src/DanielRodrigo.PruebaNET.Infrastructure/DanielRodrigo.PruebaNET.Infrastructure.csproj src/DanielRodrigo.PruebaNET.Infrastructure/
COPY src/DanielRodrigo.PruebaNET.Api/DanielRodrigo.PruebaNET.Api.csproj src/DanielRodrigo.PruebaNET.Api/
RUN dotnet restore src/DanielRodrigo.PruebaNET.Api/DanielRodrigo.PruebaNET.Api.csproj

COPY src/ src/
RUN dotnet publish src/DanielRodrigo.PruebaNET.Api/DanielRodrigo.PruebaNET.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DanielRodrigo.PruebaNET.Api.dll"]

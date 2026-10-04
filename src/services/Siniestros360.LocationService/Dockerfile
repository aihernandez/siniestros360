# Contexto de build: la raíz del repositorio (docker build -f src/services/Siniestros360.LocationService/Dockerfile .).
# Sólo entra src/: apps, pruebas, documentación y archivos subidos quedan fuera de la imagen.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/services/Siniestros360.LocationService/Siniestros360.LocationService.csproj -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Siniestros360.LocationService.dll"]

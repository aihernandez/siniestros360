# Contexto de build: la raíz del repositorio (docker build -f src/gateway/Siniestros360.Gateway/Dockerfile .).
# Sólo entra src/: apps, pruebas, documentación y archivos subidos quedan fuera de la imagen.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY src/ src/
RUN dotnet publish src/gateway/Siniestros360.Gateway/Siniestros360.Gateway.csproj -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Siniestros360.Gateway.dll"]

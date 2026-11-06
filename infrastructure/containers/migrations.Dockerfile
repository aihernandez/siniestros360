ARG SDK_IMAGE
ARG RUNTIME_IMAGE
FROM ${SDK_IMAGE} AS build
ARG SERVICE
ARG DB_KEY
ARG EF_VERSION=10.0.11
WORKDIR /repo
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet tool install dotnet-ef --tool-path /tools --version "$EF_VERSION"
# Design-time placeholders let EF construct the host without embedding deployment credentials.
RUN env "ConnectionStrings__${DB_KEY}=Host=localhost;Database=design;Username=design;Password=design" \
    Jwt__SigningKey=design-time-only-key-at-least-32-characters \
    ASPNETCORE_ENVIRONMENT=Production \
    /tools/dotnet-ef migrations bundle \
    --project "src/services/Siniestros360.${SERVICE}Service" \
    --configuration Release --target-runtime linux-x64 --output /out/efbundle \
    && cp "src/services/Siniestros360.${SERVICE}Service/appsettings.json" /out/appsettings.json

FROM ${RUNTIME_IMAGE}
WORKDIR /app
COPY --from=build --chmod=0555 /out/efbundle .
COPY --from=build /out/appsettings.json .
ENV ASPNETCORE_ENVIRONMENT=Production DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net
USER 1654
ENTRYPOINT ["/app/efbundle"]

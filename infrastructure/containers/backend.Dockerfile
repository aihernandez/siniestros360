ARG SDK_IMAGE
ARG RUNTIME_IMAGE
FROM ${SDK_IMAGE} AS build
ARG PROJECT
WORKDIR /repo
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish "$PROJECT" -c Release -o /out

FROM ${RUNTIME_IMAGE}
ARG ASSEMBLY
ENV APP_DLL=${ASSEMBLY}.dll ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /out/ .
USER 1654
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]

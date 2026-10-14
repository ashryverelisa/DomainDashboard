# Build (runs natively on the build machine, cross-compiles for the target architecture)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /src
COPY src/DomainDashboard/DomainDashboard.csproj src/DomainDashboard/
RUN dotnet restore src/DomainDashboard/DomainDashboard.csproj -a $TARGETARCH
COPY src/ src/
RUN dotnet publish src/DomainDashboard/DomainDashboard.csproj -c Release -a $TARGETARCH -o /app --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "DomainDashboard.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /build

COPY src/ ./src/

RUN dotnet restore ./src/Schleusenwerk/Schleusenwerk.csproj && \
    dotnet publish ./src/Schleusenwerk/Schleusenwerk.csproj \
        --configuration Release \
        --output /app \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=goacme/lego /lego /usr/local/bin/lego

WORKDIR /app

COPY --from=build /app .

RUN mkdir -p /tmp/acme-webroot/.well-known/acme-challenge && \
    mkdir -p /certs/lego && \
    mkdir -p /data && \
    chown -R app:app /tmp/acme-webroot /certs /data

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80;https://+:443;http://+:5000

EXPOSE 80 443 5000

VOLUME ["/data", "/certs"]

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -sf http://localhost:5000/api/health || exit 1

USER app

ENTRYPOINT ["dotnet", "Schleusenwerk.dll"]

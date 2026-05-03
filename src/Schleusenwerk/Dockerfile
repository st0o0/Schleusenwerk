FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /build

COPY src/ ./src/

RUN dotnet restore ./src/Schleusenwerk/Schleusenwerk.csproj && \
    dotnet publish ./src/Schleusenwerk/Schleusenwerk.csproj \
        --configuration Release \
        --output /app \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

COPY --from=goacme/lego /lego /usr/local/bin/lego

WORKDIR /app

COPY --from=build /app .

RUN mkdir -p /tmp/acme-webroot/.well-known/acme-challenge && \
    mkdir -p /certs/lego

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80;https://+:5000

EXPOSE 80 5000

ENTRYPOINT ["dotnet", "Schleusenwerk.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /build

COPY src/ ./src/

RUN dotnet restore ./src/Schleusenwerk/Schleusenwerk.csproj && \
    dotnet publish ./src/Schleusenwerk/Schleusenwerk.csproj \
        --configuration Release \
        --output /app \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80;https://+:443

EXPOSE 80 443 5000

ENTRYPOINT ["dotnet", "Schleusenwerk.dll"]

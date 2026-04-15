FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY Schleusenwerk/Schleusenwerk.csproj Schleusenwerk/
RUN dotnet restore Schleusenwerk/Schleusenwerk.csproj

COPY Schleusenwerk/ Schleusenwerk/
RUN dotnet publish Schleusenwerk/Schleusenwerk.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Schleusenwerk.dll"]

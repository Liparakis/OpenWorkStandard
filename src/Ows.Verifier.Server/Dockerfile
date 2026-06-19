FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["OWS.sln", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["NuGet.Config", "./"]
COPY ["src/Ows.Core/Ows.Core.csproj", "src/Ows.Core/"]
COPY ["src/Ows.Verifier.Server/Ows.Verifier.Server.csproj", "src/Ows.Verifier.Server/"]

RUN dotnet restore "src/Ows.Verifier.Server/Ows.Verifier.Server.csproj" --nologo

COPY . .
RUN dotnet publish "src/Ows.Verifier.Server/Ows.Verifier.Server.csproj" -c Release -o /app/publish --no-restore --nologo

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ows.Verifier.Server.dll"]

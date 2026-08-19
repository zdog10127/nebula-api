FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DiscordClone.sln .
COPY src/Api/DiscordClone.Api.csproj src/Api/
COPY src/Domain/DiscordClone.Domain.csproj src/Domain/
COPY src/Application/DiscordClone.Application.csproj src/Application/
COPY src/Infrastructure/DiscordClone.Infrastructure.csproj src/Infrastructure/
RUN dotnet restore src/Api/DiscordClone.Api.csproj

COPY . .
RUN dotnet publish src/Api/DiscordClone.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DiscordClone.Api.dll"]

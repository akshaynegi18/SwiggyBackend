# NotificationService/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8082

# Enable hot reload and debugging
ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:8082

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
WORKDIR /src
RUN dotnet restore "NotificationService.csproj"
RUN dotnet build "NotificationService.csproj" -c Debug -o /app/build

FROM build AS publish
RUN dotnet publish "NotificationService.csproj" -c Debug -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NotificationService.dll"]

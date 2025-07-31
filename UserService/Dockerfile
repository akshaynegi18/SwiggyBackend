# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Use the SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["*.csproj", "./"]
RUN dotnet restore

# Copy the rest of the application code
COPY . ./

# Build the application
RUN dotnet publish -c Release -o /app/publish

# Use the base image for the final stage
FROM base AS final
WORKDIR /app

# Copy the published application
COPY --from=build /app/publish .

# Set environment variables for Render.com
ENV ASPNETCORE_URLS=http://+:$PORT
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose the port (Render.com will set the PORT environment variable)
EXPOSE $PORT

# Start the application
ENTRYPOINT ["dotnet", "UserService.dll"]
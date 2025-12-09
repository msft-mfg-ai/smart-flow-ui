# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything (simpler and avoids case-sensitivity issues)
COPY . .

# Fix case-sensitivity issue: rename Shared to shared to match solution file expectation
RUN if [ -d "Shared" ] && [ ! -d "shared" ]; then mv Shared shared; fi

# Restore dependencies
RUN dotnet restore "SmartFlow.UI.sln"

# Clean the solution to remove any stale Razor-generated files
RUN dotnet clean "SmartFlow.UI.sln" -c Release

# Build the solution with --no-incremental to force fresh Razor compilation
WORKDIR "/src"
RUN dotnet build "SmartFlow.UI.sln" -c Release --no-incremental

# Publish stage
FROM build AS publish
# Use --no-build as the project is already built
RUN dotnet publish "SmartFlow.UI.API/SmartFlow.UI.API.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-build

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartFlow.UI.API.dll"]

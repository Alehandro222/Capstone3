# The API as a container, for Render (or any Docker host). Build from this folder:
#   docker build -t misa-api .
# Settings come from environment variables (see DEPLOYMENT.md at the repository root).

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY TrackerApi.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish TrackerApi.csproj -c Release -o /app --no-restore

# ---- Run ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
# The .NET image's non-root user: the API never runs as root.
USER $APP_UID
# Render tells the app which port to use in $PORT; elsewhere 8080.
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet TrackerApi.dll"]

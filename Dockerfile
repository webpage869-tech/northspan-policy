# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/NorthspanPolicy/NorthspanPolicy.csproj src/NorthspanPolicy/
RUN dotnet restore src/NorthspanPolicy/NorthspanPolicy.csproj
COPY src/ src/
RUN dotnet publish src/NorthspanPolicy/NorthspanPolicy.csproj -c Release -o /app/publish --no-restore

# ---- Run ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
# Data folder for the SQLite waitlist; owned by the image's non-root "app" user.
RUN mkdir -p /app/data && chown -R app:app /app/data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "NorthspanPolicy.dll"]

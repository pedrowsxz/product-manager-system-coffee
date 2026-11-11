# Build stage: restore and publish with the full SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Moinho.Web/Moinho.Web.csproj src/Moinho.Web/
RUN dotnet restore src/Moinho.Web/Moinho.Web.csproj
COPY src/ src/
RUN dotnet publish src/Moinho.Web/Moinho.Web.csproj -c Release -o /app --no-restore

# Runtime stage: ASP.NET runtime only, running as the image's non-root "app" user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /keys && chown app /keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Moinho.Web.dll"]

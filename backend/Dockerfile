FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY backend/backend.csproj backend/
COPY shared/catalog.json shared/
RUN dotnet restore backend/backend.csproj
COPY backend/ backend/
RUN dotnet publish backend/backend.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
RUN apk add --no-cache icu-libs tzdata krb5-libs
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
RUN mkdir -p /app/Data/images && chown -R app:app /app/Data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "backend.dll"]

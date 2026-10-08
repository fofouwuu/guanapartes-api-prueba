# Compilar el proyecto con .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Guanapartes.Api.csproj ./
RUN dotnet restore Guanapartes.Api.csproj

COPY . ./
RUN dotnet publish Guanapartes.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Ejecutar la API
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production

USER app

CMD ["sh", "-c", "exec dotnet Guanapartes.Api.dll --urls \"http://0.0.0.0:${PORT:-8080}\""]
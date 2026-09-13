# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SmartX.slnx .
COPY src/SmartX.Client/SmartX.Client.csproj src/SmartX.Client/
COPY src/SmartX.Shared/SmartX.Shared.csproj src/SmartX.Shared/
RUN dotnet restore src/SmartX.Client/SmartX.Client.csproj

COPY src/SmartX.Client/ src/SmartX.Client/
COPY src/SmartX.Shared/ src/SmartX.Shared/
RUN dotnet publish src/SmartX.Client/SmartX.Client.csproj -c Release -o /app/publish --no-restore

# Blazor WebAssembly has no server component of its own - the published
# wwwroot is pure static files, served here by nginx.
FROM nginx:alpine AS runtime
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/SmartX.Client/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

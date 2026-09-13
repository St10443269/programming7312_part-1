# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SmartX.slnx .
COPY src/SmartX.Api/SmartX.Api.csproj src/SmartX.Api/
COPY src/SmartX.Shared/SmartX.Shared.csproj src/SmartX.Shared/
RUN dotnet restore src/SmartX.Api/SmartX.Api.csproj

COPY src/SmartX.Api/ src/SmartX.Api/
COPY src/SmartX.Shared/ src/SmartX.Shared/
RUN dotnet publish src/SmartX.Api/SmartX.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SmartX.Api.dll"]

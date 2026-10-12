FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY SmartLibrary.sln ./
COPY backend/src/SmartLibrary.Domain/SmartLibrary.Domain.csproj backend/src/SmartLibrary.Domain/
COPY backend/src/SmartLibrary.Application/SmartLibrary.Application.csproj backend/src/SmartLibrary.Application/
COPY backend/src/SmartLibrary.Infrastructure/SmartLibrary.Infrastructure.csproj backend/src/SmartLibrary.Infrastructure/
COPY backend/src/SmartLibrary.Api/SmartLibrary.Api.csproj backend/src/SmartLibrary.Api/
COPY backend/tests/SmartLibrary.UnitTests/SmartLibrary.UnitTests.csproj backend/tests/SmartLibrary.UnitTests/
RUN dotnet restore SmartLibrary.sln
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/SmartLibrary.Api/SmartLibrary.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartLibrary.Api.dll"]

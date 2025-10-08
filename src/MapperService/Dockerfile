# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY MapReduceSolution.sln ./
COPY src/ApiService/ApiService.csproj src/ApiService/
COPY src/MapperService/MapperService.csproj src/MapperService/
COPY src/ReducerService/ReducerService.csproj src/ReducerService/
COPY src/Shared/Shared.csproj src/Shared/
RUN dotnet restore MapReduceSolution.sln

COPY . .
RUN dotnet publish src/MapperService/MapperService.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "MapperService.dll"]

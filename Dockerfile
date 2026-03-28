FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY vision-backend.csproj ./
RUN dotnet restore vision-backend.csproj

COPY . ./
RUN dotnet publish vision-backend.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "vision-backend.dll"]

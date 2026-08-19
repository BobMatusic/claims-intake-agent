FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["ClaimsIntake.Web.csproj", "./"]
COPY ["ClaimsIntake.Core/ClaimsIntake.Core.csproj", "ClaimsIntake.Core/"]
RUN dotnet restore "ClaimsIntake.Web.csproj"
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ClaimsIntake.Web.dll"]

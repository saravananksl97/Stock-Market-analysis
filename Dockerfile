FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY RupeeDesk.Mvc.csproj ./
RUN dotnet restore RupeeDesk.Mvc.csproj
COPY . ./
RUN dotnet publish RupeeDesk.Mvc.csproj --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=10000
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "RupeeDesk.Mvc.dll"]
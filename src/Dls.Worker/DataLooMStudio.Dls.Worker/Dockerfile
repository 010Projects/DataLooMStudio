FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore DataLooMStudio.slnx
RUN dotnet publish src/Dls.Worker/DataLooMStudio.Dls.Worker/DataLooMStudio.Dls.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DataLooMStudio.Dls.Worker.dll"]

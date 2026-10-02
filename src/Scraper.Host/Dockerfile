FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Scraper/Scraper.csproj src/Scraper/
COPY src/Scraper.Host/Scraper.Host.csproj src/Scraper.Host/
RUN dotnet restore src/Scraper.Host
COPY src/ src/
RUN dotnet publish src/Scraper.Host -c Release -o /publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
COPY configs/ configs/
# Run the apphost binary, not `dotnet <dll>`: SIGTERM graceful shutdown only works via the apphost.
ENTRYPOINT ["./Scraper.Host"]

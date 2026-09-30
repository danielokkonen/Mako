FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Scraper/Scraper.csproj src/Scraper/
COPY src/Scraper.Blocket/Scraper.Blocket.csproj src/Scraper.Blocket/
RUN dotnet restore src/Scraper.Blocket
COPY src/ src/
RUN dotnet publish src/Scraper.Blocket -c Release -o /publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
ENV XDG_DATA_HOME=/data
# Run the apphost binary, not `dotnet <dll>`: SIGTERM graceful shutdown only works via the apphost.
ENTRYPOINT ["./Scraper.Blocket"]

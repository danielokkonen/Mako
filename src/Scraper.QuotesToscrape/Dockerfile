FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Scraper/Scraper.csproj src/Scraper/
COPY src/Scraper.QuotesToscrape/Scraper.QuotesToscrape.csproj src/Scraper.QuotesToscrape/
RUN dotnet restore src/Scraper.QuotesToscrape
COPY src/ src/
RUN dotnet publish src/Scraper.QuotesToscrape -c Release -o /publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
ENV XDG_DATA_HOME=/data
# Run the apphost binary, not `dotnet <dll>`: SIGTERM graceful shutdown only works via the apphost.
ENTRYPOINT ["./Scraper.QuotesToscrape"]

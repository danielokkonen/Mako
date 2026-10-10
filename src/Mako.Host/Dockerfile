FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Mako.Core/Mako.Core.csproj src/Mako.Core/
COPY src/Mako.Host/Mako.Host.csproj src/Mako.Host/
RUN dotnet restore src/Mako.Host
COPY src/ src/
RUN dotnet publish src/Mako.Host -c Release -o /publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
COPY configs/ configs/
# Run the apphost binary, not `dotnet <dll>`: SIGTERM graceful shutdown only works via the apphost.
ENTRYPOINT ["./Mako.Host"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /source
COPY global.json ./
COPY server/Odysseum.Server/Odysseum.Server.csproj server/Odysseum.Server/
RUN dotnet restore server/Odysseum.Server/Odysseum.Server.csproj
COPY server/ server/
RUN dotnet publish server/Odysseum.Server/Odysseum.Server.csproj -c Release --no-restore -o /output

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=server /output .
USER root
RUN mkdir -p /projects /data/keys && chown -R app:app /projects /data
USER app
# /projects holds one folder per project; /data holds everything else the app keeps (settings, keys).
ENV ASPNETCORE_HTTP_PORTS=5080 ODYSSEUM_WORKSPACE=/projects ODYSSEUM_KEYS=/data/keys ODYSSEUM_SETTINGS=/data/server-settings.json
VOLUME ["/projects", "/data"]
EXPOSE 5080
ENTRYPOINT ["dotnet", "Odysseum.Server.dll"]

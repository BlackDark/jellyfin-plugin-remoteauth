FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Jellyfin.Plugin.RemoteAuth/Jellyfin.Plugin.RemoteAuth.csproj Jellyfin.Plugin.RemoteAuth/
RUN dotnet restore Jellyfin.Plugin.RemoteAuth/Jellyfin.Plugin.RemoteAuth.csproj
COPY . .
RUN dotnet publish Jellyfin.Plugin.RemoteAuth/Jellyfin.Plugin.RemoteAuth.csproj \
    -c Release -o /app/publish

FROM scratch AS artifact
COPY --from=build /app/publish/*.dll /
COPY --from=build /app/publish/meta.json /

FROM build AS package-build
RUN apt-get update && apt-get install -y zip && rm -rf /var/lib/apt/lists/*
RUN cd /app/publish && zip /remote-auth.zip *.dll meta.json

FROM scratch AS package
COPY --from=package-build /remote-auth.zip /

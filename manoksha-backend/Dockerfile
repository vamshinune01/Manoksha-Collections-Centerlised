# Builds the API (default) or the Worker: docker build --build-arg APP=Worker .
# The API image also runs operational commands, e.g. the Cloud Run Job: `dotnet Manoksha.Api.dll migrate`.
ARG APP=Api

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG APP
WORKDIR /src
COPY . .
RUN dotnet publish src/Hosts/Manoksha.${APP}/Manoksha.${APP}.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
ARG APP
# ffmpeg optimizes product videos (H.264 MP4 + poster) for web/mobile delivery (ADR-001 §29). Images use SkiaSharp (bundled).
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
ENV APP_DLL=Manoksha.${APP}.dll \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]

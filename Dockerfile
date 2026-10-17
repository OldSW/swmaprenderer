# syntax=docker/dockerfile:1

# Builds the linux binary from source and ships it alone in a minimal image. Skia comes as the
# NoDependencies native asset and the .NET runtime is bundled into the single file, so the
# final image needs nothing beyond libc and the handful of libraries runtime-deps provides.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, so editing a source file does not re-download the packages.
COPY swmaprenderer.csproj ./
RUN case "$TARGETARCH" in \
        amd64) echo linux-x64 > /rid ;; \
        arm64) echo linux-arm64 > /rid ;; \
        *) echo "unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet restore swmaprenderer.csproj -r "$(cat /rid)"

COPY . .
RUN dotnet publish swmaprenderer.csproj \
        --no-restore \
        --configuration Release \
        --runtime "$(cat /rid)" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=none \
        --output /out \
    && find /out -name '*.pdb' -delete

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
COPY --from=build /out/ /app/

# appsettings.json is read from the binary's directory, but --data and every other path
# resolve against the working directory. Mount the client folder and an output folder here:
#
#   docker run --rm -v "$PWD:/data" swmaprenderer --map 1 -x 1440 -y 1688 -o map.png
WORKDIR /data
ENTRYPOINT ["/app/swmaprenderer"]

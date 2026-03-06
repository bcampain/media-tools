# /srv/media-tools/Dockerfile

# ── Stage 1: build the mt CLI ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY mt/ .
RUN dotnet publish src/MediaTools.Cli/MediaTools.Cli.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained \
      -o /publish

# ── Stage 2: media-tools runtime image ───────────────────────────────────────
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=America/Chicago

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
      ca-certificates \
      curl \
      tzdata \
      bash \
      coreutils \
      findutils \
      grep \
      sed \
      gawk \
      jq \
      rsync \
      ffmpeg \
      handbrake-cli \
      mediainfo \
    && rm -rf /var/lib/apt/lists/*

# Copy bash scripts
COPY bin/ /usr/local/bin/
RUN chmod +x /usr/local/bin/*

# Copy mt CLI (self-contained — no .NET runtime required in this image)
COPY --from=build /publish /usr/local/lib/mt/
RUN ln -s /usr/local/lib/mt/mt /usr/local/bin/mt

WORKDIR /work

# default just drops you in bash if you run interactively
CMD ["bash"]

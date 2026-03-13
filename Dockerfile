# /srv/media-tools/Dockerfile
# The mt CLI binary is built locally (or via CI) before running docker compose build.
# Run ./build.sh to publish and build the image in one step.

# ── Runtime image ─────────────────────────────────────────────────────────────
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

# Copy mt CLI (self-contained binary, built locally via ./build.sh)
COPY publish/ /usr/local/lib/mt/
RUN ln -s /usr/local/lib/mt/mt /usr/local/bin/mt

WORKDIR /work

# default just drops you in bash if you run interactively
CMD ["bash"]

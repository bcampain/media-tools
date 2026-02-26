# /srv/media-tools/Dockerfile
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

# Copy scripts
COPY bin/ /usr/local/bin/
RUN chmod +x /usr/local/bin/*

WORKDIR /work

# default just drops you in bash if you run interactively
CMD ["bash"]

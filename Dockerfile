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
      python3 \
      software-properties-common \
    && add-apt-repository -y ppa:kobuk-team/intel-graphics \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
      intel-media-va-driver-non-free \
      libmfx-gen1 \
      libvpl2 \
      vainfo \
    && rm -rf /var/lib/apt/lists/*

# Install yt-dlp from the official GitHub "latest" release.
#
# WHY LATEST (not pinned): YouTube changes frequently break older yt-dlp
# versions, so a pinned version goes stale within weeks. Staying current
# matters more here than reproducibility.
#
# WHY ADD (not RUN curl): Docker caches a `RUN curl …` layer based on the
# command text, so a rebuild would reuse the old binary forever. With
# `ADD <url>`, BuildKit checks the remote file on every build and only
# invalidates the cache when its content changed — so a plain
# `docker compose build` (via ./build.sh) picks up new releases automatically
# and skips the download when nothing changed.
#
# --chmod=755 makes it executable without a separate RUN chmod layer.
#
# ffmpeg is already installed above; yt-dlp uses it to merge separate
# audio/video streams (common on YouTube).
ADD --chmod=755 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp /usr/local/bin/yt-dlp

# Print the version into the build log (and fail the build if the binary
# can't run), so you can see which yt-dlp each image contains.
RUN yt-dlp --version

# Copy bash scripts
COPY bin/ /usr/local/bin/
RUN chmod +x /usr/local/bin/*

# Copy mt CLI (self-contained binary, built locally via ./build.sh)
COPY publish/ /usr/local/lib/mt/
RUN ln -s /usr/local/lib/mt/mt /usr/local/bin/mt

WORKDIR /work

# default just drops you in bash if you run interactively
CMD ["bash"]

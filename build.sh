#!/usr/bin/env bash
set -e

dotnet publish mt/src/MediaTools.Cli/MediaTools.Cli.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o publish

docker compose build

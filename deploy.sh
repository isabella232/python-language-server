#!/bin/bash
set -ex
cd $(dirname "${BASH_SOURCE[0]}")

# Build image
if [ -n "$BUILDKITE_BUILD_NUMBER" ]; then
  VERSION=$(printf "%05d" $BUILDKITE_BUILD_NUMBER)_$(date +%Y-%m-%d)_$(git rev-parse --short HEAD)
else
  VERSION=insiders
fi
pushd src/LanguageServer/Impl
dotnet publish -r linux-x64 Microsoft.Python.LanguageServer.csproj
popd
docker build -t sourcegraph/lang-python:$VERSION .

# Upload to Docker Hub
docker push sourcegraph/lang-python:$VERSION
docker tag sourcegraph/lang-python:$VERSION sourcegraph/lang-python:latest
docker push sourcegraph/lang-python:latest
docker tag sourcegraph/lang-python:$VERSION sourcegraph/lang-python:insiders
docker push sourcegraph/lang-python:insiders

#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_dir"
image="${CLICK_BUILD_IMAGE:-clickable/amd64-ut24.04-2.x-arm64:latest}"
publish_dir="$project_dir/artifacts/linux-arm64"
stage_dir="$project_dir/artifacts/click/stage"
dotnet publish samples/UTDotNet.InputDemo -c Release -r linux-arm64 --self-contained true \
  -p:PublishTrimmed=false -p:UseSharedCompilation=false -m:1 -nodeReuse:false -o "$publish_dir"
rm -rf -- "$stage_dir"
mkdir -p "$stage_dir/app"
cp -a "$publish_dir/." "$stage_dir/app/"
cp packaging/click/* "$stage_dir/"
chmod +x "$stage_dir/launch.sh" "$stage_dir/app/UTDotNet.InputDemo"
desktop-file-validate "$stage_dir/inputdemo.desktop"
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$project_dir/artifacts/click,dst=/work" \
  --workdir /work --entrypoint click "$image" build --no-validate stage
package_name="$(python3 -c 'import json; m=json.load(open("packaging/click/manifest.json")); print("{name}_{version}_{architecture}.click".format(**m))')"
sha256sum "artifacts/click/$package_name"
docker run --rm --user "$(id -u):$(id -g)" \
  --mount "type=bind,src=$project_dir/artifacts/click,dst=/work,readonly" \
  --workdir /work --entrypoint click-review "$image" "$package_name" \
  2>&1 | tee artifacts/click/review.txt

#!/bin/sh
set -eu
cd "${0%/*}"
# Shell builtins only: the standard Click profile does not allow utility execution.
export DOTNET_EnableDiagnostics=0
# Avalonia otherwise reads /proc to derive WM_CLASS, which confinement disallows.
export RESOURCE_NAME="${APP_ID:-utdotnet.inputdemo_inputdemo}"
exec ./app/UTDotNet.InputDemo --trace "$@"

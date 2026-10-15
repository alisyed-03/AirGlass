#!/usr/bin/env bash
set -euo pipefail
export PATH="/mingw64/bin:/usr/bin:$PATH"
native_source=$(cygpath -u "$1")
build_directory=$(cygpath -u "$2")
app_directory=$(cygpath -u "$3")
cmake -S "$native_source" -B "$build_directory" -G Ninja -DNO_MARCH_NATIVE=ON -DCMAKE_BUILD_TYPE=Release
cmake --build "$build_directory" -j 8
cp "$build_directory/uxplay.exe" "$app_directory/engine/bin/uxplay.exe"

#!/bin/sh
# Builds the native library for Linux x64 (and macOS with clang). Run from anywhere.
set -e
ROOT="$(cd "$(dirname "$0")" && pwd)"
case "$(uname -s)" in
  Linux)  OUT="$ROOT/runtime/linux-x64"; mkdir -p "$OUT"
          gcc -shared -fPIC -O2 -Wall -Wextra -I"$ROOT/include" -o "$OUT/libstockpilot_native.so" "$ROOT/src/fees.c" ;;
  Darwin) OUT="$ROOT/runtime/osx-arm64"; mkdir -p "$OUT"
          clang -dynamiclib -O2 -Wall -Wextra -I"$ROOT/include" -o "$OUT/libstockpilot_native.dylib" "$ROOT/src/fees.c" ;;
  *) echo "Unsupported OS: use build.ps1 on Windows"; exit 1 ;;
esac
echo "Built into $OUT"

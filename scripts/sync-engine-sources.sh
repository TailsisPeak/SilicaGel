#!/usr/bin/env bash
# Re-vendor the Axion.Scripting source files (AST, parsers, emitter, converter)
# from a sibling AxionEngine checkout into Languages/Engine/.
#
# Usage:  scripts/sync-engine-sources.sh [path/to/axion-engine]
# Default search order:
#   $1
#   ../AxionEngine
#   ../axion-engine
set -euo pipefail
cd "$(dirname "$0")/.."

CANDIDATES=("${1:-}" "../AxionEngine" "../axion-engine")
SRC=""
for c in "${CANDIDATES[@]}"; do
  [ -z "$c" ] && continue
  if [ -d "$c/src/Axion.Scripting" ]; then SRC="$c/src/Axion.Scripting"; break; fi
done
if [ -z "$SRC" ]; then
  echo "error: could not find AxionEngine sibling checkout" >&2
  echo "tried: ${CANDIDATES[*]}" >&2
  exit 1
fi

DEST="src/SilicaGel/Languages/Engine"
mkdir -p "$DEST"
for f in Ast.cs Gel.cs Silica.cs Blocks.cs CSharpEmitter.cs LanguageConverter.cs; do
  cp "$SRC/$f" "$DEST/$f"
  echo "synced: $f"
done
echo "done — vendored from $SRC"

#!/bin/bash

GREEN='\033[0;32m'
NC='\033[0m'

echo "========================================"
echo "StellarStation Server"
echo "========================================"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/Server"

dotnet run
#!/bin/bash

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================"
echo "StellarStation Server Build Script"
echo "========================================"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/Server"

echo "[1/4] Cleaning previous build..."
dotnet clean > /dev/null 2>&1
rm -rf publish

echo "[2/4] Restoring packages..."
dotnet restore
if [ $? -ne 0 ]; then
    echo -e "${RED}[ERROR] Package restore failed!${NC}"
    exit 1
fi

echo "[3/4] Building project..."
dotnet build -c Release --no-restore
if [ $? -ne 0 ]; then
    echo -e "${RED}[ERROR] Build failed!${NC}"
    exit 1
fi

echo "[4/4] Publishing..."
dotnet publish -c Release -o publish --no-build

echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}BUILD COMPLETED SUCCESSFULLY!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo "Server files: $SCRIPT_DIR/Server/publish/"
echo ""
#!/bin/bash

RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -f "$SCRIPT_DIR/Server/publish/Server" ] && [ ! -f "$SCRIPT_DIR/Server/publish/Server.exe" ]; then
    echo -e "${RED}[ERROR] Server not built! Run build_server.sh first.${NC}"
    exit 1
fi

cd "$SCRIPT_DIR/Server/publish"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}StellarStation Server (Published)${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""

if [ -f "Server.exe" ]; then
    ./Server.exe
else
    chmod +x Server
    ./Server
fi
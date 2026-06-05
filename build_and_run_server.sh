#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Сборка
"$SCRIPT_DIR/build_server.sh"

if [ $? -ne 0 ]; then
    echo "Build failed, aborting..."
    exit 1
fi

# Запуск
"$SCRIPT_DIR/run_publish_server.sh"
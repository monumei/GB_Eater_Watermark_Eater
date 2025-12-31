#!/bin/bash

MODE=$1

if [ -z "$MODE" ]; then
  echo "Please specify a mode to run:"
  echo "  ./run.sh desktop  (Runs the Desktop App)"
  echo "  ./run.sh web      (Runs the Web App)"
  exit 1
fi

if [ "$MODE" == "web" ]; then
    echo "🚀 Starting Web Version..."
    cd GB_Eater.Web
    # Check if bun is installed, else use npm
    if command -v bun &> /dev/null; then
        bun run dev
    else
        npm run dev
    fi
elif [ "$MODE" == "desktop" ] || [ "$MODE" == "app" ] || [ "$MODE" == "windows" ]; then
    echo "🖥️  Starting Desktop App..."
    dotnet run --project GB_Eater.Windows
else
    echo "Unknown mode: $MODE"
    echo "Usage: ./run.sh [web|desktop]"
    exit 1
fi

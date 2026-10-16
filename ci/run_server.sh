#!/bin/bash
if ! command -v dotnet &> /dev/null
then
    echo "============================================="
    echo "Error: .NET SDK (dotnet) is not installed."
    echo "Please install it using your package manager:"
    echo "  sudo pacman -S dotnet-sdk-8.0"
    echo "============================================="
    exit 1
fi

echo "Building and starting SLOPArena Authoritative Server..."
dotnet run --project src/Server/SlopArena.Server.csproj

# GB Eater (Cross-Platform)

This is a cross-platform port of the GB Eater Watermark tool, built using **Avalonia UI** and **SkiaSharp**. It works on Windows, macOS, and Linux.

## Features

- **AI Disruption**: Adds pixel noise patterns (Soft, Balanced, Strong) to disrupt AI training on your images.
- **Watermarking**: Tiled circular watermarks ("DO NOT TRAIN") to further protect your art.
- **Cross-Platform**: Runs natively on your Mac.

## How to Run

### Prerequisites

- .NET 8 SDK

### Command Line

1. Open a terminal in this directory.
2. Run:
   ```bash
   dotnet run --project GB_Eater.Windows
   ```

### Publishing (Create standalone app)

To create a standalone app for macOS (so you don't need the terminal):

```bash
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

### Web Version

A modern web-based version is available in the `GB_Eater_Web` directory.
To run it:

```bash
cd GB_Eater_Web
bun run dev
```

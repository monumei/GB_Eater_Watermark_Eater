# GB Eater (Cross-Platform)

This is a cross-platform port of the GB Eater Watermark tool, built using **Avalonia UI** and **SkiaSharp**. It works on Windows, macOS, and Linux.

## Features

- **Advanced AI Disruption**:
  - **Poison Mode**: Uses a multi-stage attack including **Sine Interference**, **Geometric Distortion**, **High-Frequency Grid Injection**, and **Block Scrambling** to severely disrupt AI feature extraction.
  - **Standard Modes**: Soft, Balanced, and Strong noise patterns for varying levels of visual impact.
- **Smart Watermarking**:ß
  - **Custom Images**: Drag, drop, and position your own watermark images (Desktop).
- **Interactive Preview**: Zoom and Pan to inspect pixel-level details of the protection (Desktop).
- **Cross-Platform**: Optimized for macOS (Apple Silicon native), Windows, and Web.

## About This Project

This tool was developed in response to the increasing scraping of artwork on platforms like X. It creates a defensive layer for your images by combining visible watermarks with **adversarial pixel noise**.

**Current Status:**

- Tested against **Grok** with observed success in disrupting image analysis.
- _Note:_ Broader testing against other models has been limited to minimize risk to personal artwork during the development phase.

### Mechanism of Action

The software injects high-frequency, randomized pixel noise into the image. This process uses a random seed, ensuring that even if the same image is processed twice, the noise pattern will be mathematically unique.

> **Disclaimer**: While this method may not completely prevent a model from training on your data, it significantly degrades the quality of the input ("poisoning"), aiming to disrupt inference and "annoy" the model's feature extraction process.

## How to Run

### Prerequisites

- .NET 10 SDK

### Quick Run Script

You can use the helper script to run either version:

```bash
# Run Desktop App
./run.sh desktop

# Run Web App
./run.sh web
```

### Manual Command Line (Desktop)

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

A modern web-based version is available in the `GB_Eater.Web` directory.
To run it manually:

```bash
cd GB_Eater.Web
bun run dev
```

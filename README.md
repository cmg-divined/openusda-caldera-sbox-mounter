# OpenUSDA for s&box

A pure C# implementation of a USD (Universal Scene Description) loader for s&box, capable of loading massive scenes like the full Caldera map from Call of Duty: Warzone.

## Overview

This project serves two purposes:

1. **An OpenUSD content mount** - Browse, preview, and drag USD geometry directly into your s&box scenes via the Asset Browser
2. **A proof of concept** - Demonstrating what s&box can handle as an engine when pushed to extremes

The loader parses USDA files without requiring the official USD SDK, traverses complex scene graphs with references and payloads, and includes an indexing system for handling massive datasets.

### s&box as an Engine

This project really shows off what s&box is capable of. We're loading over **3.6 million mesh instances** from the Caldera dataset - a full Call of Duty: Warzone map - and s&box handles it. The SceneObject system is ridiculously efficient at batching and rendering geometry. You can fly around the entire map in the editor with reasonable performance (tested on a 2070 and a 3700X). Note this is without any GPU culling which is nuts.

The indexing system is the key to handling large scenes. Instead of parsing the entire USD scene graph every time you want to load a map, you build an index once. This index stores every mesh's world transform, source file, and metadata in a compact binary format. Loading from an index takes seconds instead of hours.

## Why USDA Instead of USD Binary?

OpenUSD files come in two formats:
- **`.usd`** (binary crate format) - Fast to read but requires the C++ USD SDK to parse
- **`.usda`** (ASCII text format) - Human-readable and parseable with simple text processing

Since s&box doesn't support native C++ libraries, we need the ASCII format. The official `usdcat` tool from Pixar can convert binary USD files to ASCII:

```bash
usdcat input.usd -o output.usda
```

If you're working with the Caldera dataset or similar, you'll need to batch convert all `.usd` files to `.usda`. You can do this with a PowerShell script:

```powershell
Get-ChildItem -Path "C:\OpenUSDA" -Filter "*.usd" -Recurse | ForEach-Object {
    $output = $_.FullName -replace '\.usd$', '.usda'
    if (-not (Test-Path $output)) {
        usdcat $_.FullName -o $output
    }
}
```

## File Structure

Place your converted USD content at:
```
C:\OpenUSDA\
├── caldera.usda              # Root scene file
├── caldera.usdi              # Pre-built index (generated)
├── assets\
│   └── xmodel\               # Geometry files (.geo.usda)
└── map_source\
    └── prefabs\              # Scene prefabs and references
```

The base path is configured in `UsdMount.cs`. If you want to change it, update the `BasePath` constant.

## How the Index System Works

### The Problem

A scene like Caldera has over 3.6 million mesh instances across 17,000+ source files. Parsing all of that on every load would take forever. The USD format uses a complex graph of references, payloads, and variant sets that need to be resolved to find actual geometry.

### The Solution

The indexer does all the heavy lifting once:

1. **Traverses the scene graph** starting from the root `.usda` file
2. **Resolves all references and payloads** following the USD composition rules
3. **Accumulates world transforms** as it walks down the hierarchy
4. **Records every mesh** with its final world position, rotation, scale, and source file
5. **Writes a binary index** (`.usdi` file) that can be loaded in seconds

The index format is simple:
- Magic header ("USDI")
- List of unique source file paths (deduplicated)
- List of mesh references, each containing:
  - Index into the source file list
  - Mesh name and path
  - World transform (position, rotation, scale)
  - Skeleton binding flag
  - Extent bounds (if available)

### Building an Index

In the s&box editor, go to **Editor → USD** and you'll find:

- **Build Caldera Index** - Processes `caldera.usda` and outputs `caldera.usdi`
- **Build Hotel_01 Index** - Processes just the hotel prefab (good for testing)

Building the full Caldera index takes a while (the scene is massive), but you only need to do it once. The indexer streams batches to disk to avoid running out of memory.

### Loading from an Index

Once you have an index, loading is fast:

- **Load Caldera (SceneObjects FULL)** - Loads all ~3.6 million meshes
- **Load Caldera (SceneObjects 50%)** - Loads about 320k meshes (good for testing)
- **Load Hotel_01 (True GPU Batch)** - Loads just the hotel

The loader reads the index, groups meshes by source geometry file, parses each unique geometry file once, and spawns instances using GPU batching. Identical geometry shares the same model, so memory usage stays reasonable even with millions of instances.

## Asset Browser Integration

The project includes a content mount that registers all `.geo.usda` geometry files as s&box Models. Once installed, you'll see "OpenUSD Caldera" appear in your Asset Browser alongside your other content sources.

You can browse the entire asset library, preview individual models, and drag them directly into your scene. Each geometry file is converted to a native s&box Model on the fly, so they work just like any other model in the engine.

## Architecture

```
Editor/
├── UsdEditorMenu.cs          # Menu commands and scene spawning
└── Usd/
    ├── Parser/
    │   ├── UsdaParser.cs     # Parses .usda text files
    │   ├── UsdaTokenizer.cs  # Lexer for USDA format
    │   └── UsdaToken.cs      # Token types
    ├── Schema/
    │   ├── UsdStage.cs       # Root container for a USD file
    │   ├── UsdPrim.cs        # A node in the scene graph
    │   ├── UsdMesh.cs        # Mesh geometry with points, normals, UVs
    │   └── UsdSkeleton.cs    # Skeleton for rigged meshes
    ├── UsdSceneLoader.cs     # Traverses scene graph, resolves references
    ├── UsdSceneIndex.cs      # Binary index reader/writer
    ├── UsdMount.cs           # s&box content mount
    └── UsdModelResourceLoader.cs  # Converts USD meshes to s&box Models
```

## Coordinate System

USD uses a different coordinate system than s&box:
- **USD**: X-right, Y-forward, Z-up
- **s&box**: X-forward, Y-right, Z-up

The loader handles this conversion automatically when reading transforms and vertex positions.

## Known Limitations

- Only supports ASCII `.usda` format (use `usdcat` to convert from binary)
- Skeleton skinning is not fully implemented (meshes render in bind pose)
- Materials are not loaded (meshes render with default material)
- Animations are not supported

## Credits

This project was developed to load the Caldera dataset released by Activision under the OpenUSD Alliance. The dataset is available at [https://openusd.org/release/dl_caldera.html](https://openusd.org/release/dl_caldera.html). God bless!


# OpenUSDA Setup Tutorial

This guide walks you through setting up the OpenUSD tools and converting the Caldera dataset for use with s&box.

## Step 1: Download the Caldera Dataset

Head over to the OpenUSD Alliance and grab the Caldera dataset:

https://github.com/Activision/caldera

Extract it to `C:\OpenUSDA`. You should end up with something like:

```
C:\OpenUSDA\
├── caldera.usd
├── assets\
└── map_source\
```

## Step 2: Download the OpenUSD Tools

Nvidia provides pre-built USD binaries that include `usdcat`, the tool we need to convert binary `.usd` files to ASCII `.usda` format.

Download the latest release from:

https://developer.nvidia.com/downloads/usd/usd_binaries/25.08/usd.py312.windows-x86_64.usdview.release-v25.08.71e038c1.zip

Extract it somewhere convenient, like `C:\USD`. After extraction you should have:

```
C:\USD\
├── bin\
│   ├── usdcat.exe
│   ├── usdview.exe
│   └── ...
├── lib\
└── ...
```

## Step 3: Convert USD to USDA

s&box can't load binary `.usd` files directly, so we need to convert everything to ASCII `.usda` format. This repository includes conversion scripts that handle this automatically.

### Using the Conversion Scripts

You have two options:

**Option A: Batch file (simple)**
- Copy `convert_to_usda.bat` to your `C:\OpenUSDA` folder
- Edit it to set your `USD_PATH` if needed
- Double-click to run

**Option B: PowerShell (nicer output)**
- Copy `convert_to_usda.ps1` and `run_converter.bat` to your `C:\OpenUSDA` folder
- Edit the ps1 file to set your `$UsdPath` if needed
- Double-click `run_converter.bat` to run

Both scripts do the same thing. The PowerShell version has colored output and slightly better progress reporting.

### What the Scripts Do

The scripts will:
- Find all `.usd` files recursively in the target directory
- Convert each one to `.usda` in the same location (so references still work)
- Skip files that have already been converted
- Show progress as they go
- Report how many files were converted, skipped, or failed

You can run them multiple times safely. They won't reconvert files that already have a `.usda` version.

### Configuration

Before running, open the script and check these settings at the top:

**For the .bat file:**
```batch
set USD_PATH=C:\USD
set TARGET_DIR=C:\OpenUSDA
```

**For the .ps1 file:**
```powershell
$UsdPath = "C:\USD"
$TargetDir = "C:\OpenUSDA"
```

- `USD_PATH` / `$UsdPath` = where you extracted the Nvidia USD tools
- `TARGET_DIR` / `$TargetDir` = where your Caldera dataset is

### Conversion Time

The Caldera dataset has thousands of files, so conversion takes a while. Expect it to run for 30 minutes to a few hours depending on your hardware. You can let it run in the background or overnight.

## Step 4: Open s&box and Build the Index

Once conversion is complete:

1. Open s&box with the OpenUSDA project
2. Go to **Editor → USD → Build Caldera Index**
3. Wait for the indexing to complete (this processes all 17k+ files)
4. Once done, use **Editor → USD → Load Caldera (SceneObjects FULL)** to load the map

For testing, you can use **Load Caldera (SceneObjects 50%)** which loads about half the meshes and is much faster.

## Troubleshooting

### "usdcat is not recognized"

Make sure the `USD_PATH` in the batch file points to the correct location. The `bin` folder should contain `usdcat.exe`.

### Conversion is slow

This is normal. The Caldera dataset has thousands of files and each one needs to be parsed and rewritten. Let it run overnight if needed.

### Some files fail to convert

A few files might fail due to encoding issues or corrupted data. The loader will skip these gracefully. As long as most files convert successfully, you're good.

### Out of memory during indexing

The indexer streams data to disk in batches to avoid this, but if you're still running low on RAM, close other applications and try again.


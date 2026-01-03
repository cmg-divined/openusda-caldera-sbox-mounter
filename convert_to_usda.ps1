# ============================================
#   USD to USDA PowerShell Converter
#   Converts binary .usd files to ASCII .usda
#   Files are converted in-place (same folder)
# ============================================

# Path to your USD installation (where you extracted the Nvidia tools)
$UsdPath = "C:\Users\James\Desktop\usd.py312.windows-x86_64.usdview.release-v25.08.71e038c1"

# Path to your Caldera dataset
$TargetDir = "C:\USD"

# Set up environment
$env:PATH = "$UsdPath\bin;$UsdPath\lib;$env:PATH"
$env:PYTHONPATH = "$UsdPath\lib\python;$env:PYTHONPATH"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  USD to USDA PowerShell Converter" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "USD Tools: $UsdPath"
Write-Host "Target: $TargetDir"
Write-Host ""

# Verify usdcat exists
$usdcat = Join-Path $UsdPath "bin\usdcat.exe"
if (-not (Test-Path $usdcat)) {
    Write-Host "ERROR: usdcat.exe not found at $usdcat" -ForegroundColor Red
    Write-Host "Please update `$UsdPath in this script to point to your USD installation."
    Read-Host "Press Enter to exit"
    exit 1
}

# Verify target directory exists
if (-not (Test-Path $TargetDir)) {
    Write-Host "ERROR: Target directory not found: $TargetDir" -ForegroundColor Red
    Write-Host "Please update `$TargetDir in this script."
    Read-Host "Press Enter to exit"
    exit 1
}

# Find all .usd files
Write-Host "Scanning for .usd files..."
$files = Get-ChildItem -Path $TargetDir -Filter "*.usd" -Recurse -File
$total = $files.Count
Write-Host "Found $total .usd files to process"
Write-Host ""

$processed = 0
$skipped = 0
$failed = 0
$current = 0

foreach ($file in $files) {
    $current++
    $input = $file.FullName
    $output = [System.IO.Path]::ChangeExtension($input, ".usda")
    
    if (Test-Path $output) {
        $skipped++
    } else {
        Write-Host "[$current/$total] Converting: $($file.Name)"
        
        try {
            $result = & $usdcat $input -o $output 2>&1
            if ($LASTEXITCODE -eq 0) {
                $processed++
            } else {
                Write-Host "  FAILED: $($file.Name)" -ForegroundColor Yellow
                $failed++
            }
        } catch {
            Write-Host "  FAILED: $($file.Name) - $($_.Exception.Message)" -ForegroundColor Yellow
            $failed++
        }
    }
    
    # Progress update every 100 files
    if ($current % 100 -eq 0) {
        $pct = [math]::Round(($current / $total) * 100, 1)
        Write-Host "  Progress: $current / $total ($pct%)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Conversion Complete" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Converted: $processed" -ForegroundColor Green
Write-Host "  Skipped (already exist): $skipped" -ForegroundColor Gray
Write-Host "  Failed: $failed" -ForegroundColor $(if ($failed -gt 0) { "Yellow" } else { "Gray" })
Write-Host "  Total: $total"
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Read-Host "Press Enter to exit"

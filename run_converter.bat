@echo off
REM This launches the PowerShell converter script
REM Use this if you prefer the PowerShell version (has colored output and better progress)

powershell -ExecutionPolicy Bypass -File "%~dp0convert_to_usda.ps1"


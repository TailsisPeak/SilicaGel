# Re-vendor the Axion.Scripting source files (AST, parsers, emitter, converter)
# from a sibling AxionEngine checkout into Languages/Engine/.
#
# Usage:  pwsh scripts/sync-engine-sources.ps1 [path\to\AxionEngine]
param([string]$Source = "")

$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $PSScriptRoot)

$candidates = @($Source, "..\AxionEngine", "..\axion-engine") | Where-Object { $_ -ne "" }
$src = $null
foreach ($c in $candidates) {
    if (Test-Path "$c\src\Axion.Scripting") { $src = "$c\src\Axion.Scripting"; break }
}
if (-not $src) {
    Write-Error "could not find AxionEngine sibling checkout. tried: $($candidates -join ', ')"
}

$dest = "src\SilicaGel\Languages\Engine"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($f in @("Ast.cs","Gel.cs","Silica.cs","Blocks.cs","CSharpEmitter.cs","LanguageConverter.cs")) {
    Copy-Item "$src\$f" "$dest\$f" -Force
    Write-Host "synced: $f"
}
Write-Host "done -- vendored from $src"

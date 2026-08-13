# Publication DrawingComparator : exe unique self-contained + empreinte SHA-256.
# Usage : powershell -File scripts\publish.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "publish"

dotnet publish (Join-Path $root "src\DrawingComparator.App") -c Release -r win-x64 -p:PublishSingleFile=true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Les symboles natifs des packages (libSkiaSharp.pdb, 85 Mo) ne servent a rien en livraison.
Get-ChildItem $out -Filter *.pdb | Remove-Item -Force

$exe = Join-Path $out "DrawingComparator.App.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
"$hash  DrawingComparator.App.exe" | Out-File (Join-Path $out "SHA256SUMS.txt") -Encoding ascii
Write-Output "Publie : $exe ($size Mo)"
Write-Output "SHA-256 : $hash (publish\SHA256SUMS.txt, a transmettre avec l'exe)"

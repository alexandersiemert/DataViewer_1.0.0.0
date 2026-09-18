<#
    Erzeugt ein auslieferbares Setup.

    Ablauf:
      1. Alles loeschen, was von frueheren Baustaenden stammt
      2. Tests ausfuehren - ohne gruene Tests wird nicht gebaut
      3. Eigenstaendig veroeffentlichen (der Kunde braucht keine Vorinstallation)
      4. Selbsttest gegen echte Geraetedaten ausfuehren
      5. Jedes Fenster einmal aufbauen - faengt Fehler in den XAML-Dateien
      6. Setup mit Inno Setup erzeugen

    Aufruf:  powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== 1. Aufraeumen ==" -ForegroundColor Cyan
foreach ($d in @("publish", "installer_out")) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}
Get-ChildItem -Path "src","tests" -Directory -Recurse -Include "bin","obj" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "== 2. Tests ==" -ForegroundColor Cyan
dotnet test SiemertDataViewer.sln -c $Configuration --nologo -v:quiet --filter "Category!=Hardware"
if ($LASTEXITCODE -ne 0) { throw "Tests fehlgeschlagen - es wird kein Setup erzeugt." }

Write-Host "== 3. Veroeffentlichen ==" -ForegroundColor Cyan
dotnet publish "src\DataViewer.App\DataViewer.App.csproj" `
    -c $Configuration -r win-x64 --self-contained true `
    -o publish --nologo -v:quiet
if ($LASTEXITCODE -ne 0) { throw "Veroeffentlichen fehlgeschlagen." }

$size = [math]::Round(((Get-ChildItem publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "   publish\: $size MB" -ForegroundColor Green

Write-Host "== 4. Selbsttest ==" -ForegroundColor Cyan
$sample = "tests\DataViewer.Core.Tests\Data\readout_sn349_G.txt"
if (Test-Path $sample) {
    $png = Join-Path $env:TEMP "dataviewer_buildcheck.png"
    $p = Start-Process ".\publish\SiemertDataViewer.exe" -ArgumentList "--selftest","$sample","$png" `
         -NoNewWindow -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Selbsttest fehlgeschlagen (Code $($p.ExitCode))." }
    Write-Host "   Selbsttest bestanden." -ForegroundColor Green
} else {
    Write-Warning "   Beispieldaten fehlen - Selbsttest uebersprungen."
}

Write-Host "== 5. Fenstertest ==" -ForegroundColor Cyan
$p = Start-Process ".\publish\SiemertDataViewer.exe" -ArgumentList "--fenstertest" `
     -NoNewWindow -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Fenstertest fehlgeschlagen (Code $($p.ExitCode))." }
Write-Host "   Alle Fenster bauen fehlerfrei auf." -ForegroundColor Green

if ($SkipInstaller) { Write-Host "Fertig (ohne Setup)." -ForegroundColor Green; return }

Write-Host "== 6. Setup ==" -ForegroundColor Cyan
$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw "Inno Setup 6 wurde nicht gefunden." }

& $iscc "installer\SiemertDataViewer.iss" | Select-Object -Last 5
if ($LASTEXITCODE -ne 0) { throw "Setup-Erzeugung fehlgeschlagen." }

Get-ChildItem installer_out -Filter *.exe | ForEach-Object {
    Write-Host ("   {0}  ({1} MB)" -f $_.Name, [math]::Round($_.Length/1MB,1)) -ForegroundColor Green
    $sig = Get-AuthenticodeSignature $_.FullName
    if ($sig.Status -ne "Valid") {
        Write-Warning "   Setup ist NICHT signiert. Beim Kunden erscheint die SmartScreen-Warnung."
        Write-Warning "   Zertifikat in Inno Setup hinterlegen und SignTool im Skript aktivieren."
    }
}
Write-Host "Fertig." -ForegroundColor Green

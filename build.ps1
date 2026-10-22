#Requires -Version 7
<#
.SYNOPSIS
  Builds Cursor1cDiff.exe (WinExe, no console) with .NET Framework csc.
#>
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$src = Join-Path $root 'src\Cursor1cDiff.cs'
$outDir = Join-Path $root 'dist'
$exe = Join-Path $outDir 'Cursor1cDiff.exe'

$csc = Get-ChildItem -Path "$env:WINDIR\Microsoft.NET\Framework64" -Filter csc.exe -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $csc) {
    throw 'csc.exe not found under Framework64. Install .NET Framework Developer Pack / targeting pack.'
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $csc /nologo /target:winexe /optimize+ /out:$exe /r:System.Windows.Forms.dll /r:System.dll $src
if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}

Get-Item -LiteralPath $exe | Select-Object FullName, Length, LastWriteTime
Write-Host "OK: $exe"

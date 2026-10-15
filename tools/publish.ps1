<#
.SYNOPSIS
  Publica o Fonia para Windows x64: uma pasta autossuficiente (não precisa instalar o .NET) e um .zip.

.DESCRIPTION
  1. dotnet publish (Release, win-x64, self-contained) em <Output>\fonia, sem símbolos de depuração;
  2. tira o que o Windows x64 não usa (as bibliotecas nativas de Linux, macOS, ARM e x86, ~70 MB, e arquivos de build);
  3. confere que o que o programa precisa para rodar está lá;
  4. compacta em <Output>\fonia-<versão>-win-x64.zip.

  O instalador (installer\fonia.iss, Inno Setup) usa a pasta que este script gera. Os modelos de voz (~800 MB) não vão no pacote:
  o programa os baixa (fonia --download-models).

.EXAMPLE
  pwsh tools/publish.ps1
  pwsh tools/publish.ps1 -Output C:\temp\fonia-dist
#>
[CmdletBinding()]
param(
    [string]$Output = 'dist',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$out = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$target = Join-Path $out 'fonia'

if (Test-Path $target) {
    Remove-Item $target -Recurse -Force
}

Write-Host "Publicando em $target ..."
dotnet publish (Join-Path $root 'src/Fonia.App/Fonia.App.csproj') `
    --configuration $Configuration --runtime win-x64 --self-contained `
    --output $target `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }

# O Windows x64 usa runtimes\win-x64 (reconhecimento na CPU) e runtimes\vulkan\win-x64 (na GPU, com --stt-gpu). O resto é de outros sistemas.
$runtimes = Join-Path $target 'runtimes'
if (Test-Path $runtimes) {
    Get-ChildItem $runtimes -Directory | Where-Object { $_.Name -notin 'win-x64', 'vulkan' } | Remove-Item -Recurse -Force
    $vulkan = Join-Path $runtimes 'vulkan'
    if (Test-Path $vulkan) {
        Get-ChildItem $vulkan -Directory | Where-Object { $_.Name -ne 'win-x64' } | Remove-Item -Recurse -Force
    }
}

# Arquivos de build e de outros sistemas que vieram junto.
Get-ChildItem $target -File | Where-Object { $_.Extension -in '.lib', '.metal', '.pdb' } | Remove-Item -Force

$required = @(
    'fonia.exe',
    'onnxruntime.dll',
    'SimConnect.dll',
    'runtimes/win-x64/whisper.dll',
    'runtimes/vulkan/win-x64'
)
foreach ($item in $required) {
    if (-not (Test-Path (Join-Path $target $item))) { throw "O pacote não tem '$item'. Algo mudou na publicação: confira antes de distribuir." }
}

$version = (Get-Item (Join-Path $target 'fonia.exe')).VersionInfo.ProductVersion -replace '\+.*$', ''
$zip = Join-Path $out "fonia-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zip -CompressionLevel Optimal

$folderMb = [math]::Round((Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB)
Write-Host "Pronto: fonia $version, pasta de $folderMb MB, zip de $zipMb MB."
Write-Host "  $target"
Write-Host "  $zip"

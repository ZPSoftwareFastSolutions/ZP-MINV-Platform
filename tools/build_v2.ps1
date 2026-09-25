<#
.SYNOPSIS
    M-INV V2 - Ciclo completo del libro colaborativo (definicion de terminado, reglas R-12 y C-12).

.DESCRIPTION
    1. tools\build_minv_v2.py        Genera el Core (demo) y el Release del libro colaborativo + fixture de pruebas.
    2. tools\office_scripts.py check  Verifica que cada Office Script tenga el bloque comun actualizado.
    3. tests\office-scripts          Ejecuta los scripts reales contra el simulador de ExcelScript (Node 22.18+).
    4. tools\verify_minv_v2.ps1      Verifica Core y Release en Excel real y los guarda recalculados.
    5. tools\office_scripts.py deploy Genera las versiones instalables (con contrasena) en build\office-scripts.
    6. -Capturas                     Exporta las vistas previas a docs\product\capturas\v2.
    Termina con codigo 1 si falla cualquier paso. Script ASCII a proposito (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build_v2.ps1 -FinDemo 2026-09-25 -Capturas
#>
param(
    [string]$FinDemo = '',
    [switch]$Capturas
)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$root = Split-Path $PSScriptRoot -Parent
$py = Join-Path $root '.venv\Scripts\python.exe'
if (-not (Test-Path $py)) { $py = 'python' }
$corePwd = if ($env:MINV_PASSWORD) { $env:MINV_PASSWORD } else { 'minv-dev' }
$relPwd = if ($env:MINV_RELEASE_PASSWORD) { $env:MINV_RELEASE_PASSWORD } else { $corePwd }
$script:fallas = @()

function Paso([string]$titulo, [scriptblock]$accion) {
    Write-Output ''
    Write-Output ('######## ' + $titulo)
    $global:LASTEXITCODE = 0
    & $accion
    if ($LASTEXITCODE -ne 0) { $script:fallas += $titulo }
}

$pyArgs = @((Join-Path $PSScriptRoot 'build_minv_v2.py'))
if ($FinDemo) { $pyArgs += @('--fin-demo', $FinDemo) }
Paso 'Generar libro colaborativo (build_minv_v2.py)' { & $py @pyArgs }
if ($script:fallas.Count -gt 0) { Write-Output 'RESULTADO: la generacion fallo; no se continua.'; exit 1 }

Paso 'Bloque comun de los Office Scripts' { & $py (Join-Path $PSScriptRoot 'office_scripts.py') check }
Paso 'Pruebas de los Office Scripts (Node)' {
    & node --disable-warning=ExperimentalWarning (Join-Path $root 'tests\office-scripts\pruebas.mts')
}

$previa = Join-Path $root 'build\preview-v2'
$vCore = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'verify_minv_v2.ps1'),
    '-Path', (Join-Path $root 'src\M-INV_V2_Colaborativo.xlsx'), '-Password', $corePwd, '-Save')
if ($Capturas) { $vCore += @('-PreviewDir', $previa) }
Paso 'Verificar src\M-INV_V2_Colaborativo.xlsx' { & powershell @vCore }
$vRel = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'verify_minv_v2.ps1'),
    '-Path', (Join-Path $root 'releases\M-INV_V2_Colaborativo_Produccion.xlsx'), '-Password', $relPwd, '-Save')
Paso 'Verificar releases\M-INV_V2_Colaborativo_Produccion.xlsx' { & powershell @vRel }

Paso 'Office Scripts instalables (build\office-scripts)' {
    & $py (Join-Path $PSScriptRoot 'office_scripts.py') deploy --edicion core
    if ($LASTEXITCODE -eq 0) { & $py (Join-Path $PSScriptRoot 'office_scripts.py') deploy --edicion release }
}

if ($Capturas) {
    $destino = Join-Path $root 'docs\product\capturas\v2'
    New-Item -ItemType Directory -Force -Path $destino | Out-Null
    foreach ($png in Get-ChildItem $previa -Filter '*.png') {
        $nombre = $png.Name -replace '^M-INV_V2_Colaborativo__', ''
        Copy-Item $png.FullName (Join-Path $destino $nombre) -Force
    }
    Write-Output ('Capturas copiadas a ' + $destino)
}

Write-Output ''
if ($script:fallas.Count -gt 0) {
    Write-Output ('RESULTADO: fallaron ' + $script:fallas.Count + ' paso(s): ' + ($script:fallas -join ' | '))
    exit 1
}
Write-Output 'RESULTADO: todos los pasos sin fallas'

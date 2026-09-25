<#
.SYNOPSIS
    M-INV V1.2 - Ciclo completo de construccion y verificacion (definicion de terminado, regla R-12).

.DESCRIPTION
    1. tools\build_minv.py     Genera la edicion Estandar (.xlsx Core y Release) y las bases Plus en build\.
    2. tools\build_xlsm.ps1    Construye la edicion Plus (.xlsm Core y Release) y prueba su VBA en Excel.
    3. tools\verify_minv.ps1   Verifica los 4 entregables en Excel (motor de formulas, macros deshabilitadas).
                               Los .xlsx se guardan recalculados (valores en cache).
    4. -Capturas               Exporta las vistas previas a docs\product\capturas (Estandar) y
                               docs\product\capturas\plus (hojas propias de la edicion Plus).
    Termina con codigo 1 si falla cualquier paso.
    Script ASCII a proposito: PowerShell 5.1 lee los .ps1 sin BOM como ANSI.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build_all.ps1 -FinDemo 2026-09-25 -Capturas
#>
param(
    [string]$FinDemo = '',
    [switch]$Capturas,
    [switch]$SinPlus
)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'   # la salida de Python llega en UTF-8 (tildes correctas en el registro)
$root = Split-Path $PSScriptRoot -Parent
$py = Join-Path $root '.venv\Scripts\python.exe'
if (-not (Test-Path $py)) { $py = 'python' }
$verify = Join-Path $PSScriptRoot 'verify_minv.ps1'
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

$pyArgs = @((Join-Path $PSScriptRoot 'build_minv.py'))
if ($FinDemo) { $pyArgs += @('--fin-demo', $FinDemo) }
Paso 'Generar libros (build_minv.py)' { & $py @pyArgs }
if ($script:fallas.Count -gt 0) { Write-Output 'RESULTADO: la generacion fallo; no se continua.'; exit 1 }

if (-not $SinPlus) {
    Paso 'Edicion Plus (build_xlsm.ps1)' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build_xlsm.ps1')
    }
}

$capturasPlus = Join-Path $root 'build\preview-plus'
$entregables = @(
    @{ Ruta = 'src\M-INV_V1_Core.xlsx'; Clave = $corePwd; Guardar = $true; Previa = (Join-Path $root 'docs\product\capturas') },
    @{ Ruta = 'releases\M-INV_V1_Produccion_Bloqueado.xlsx'; Clave = $relPwd; Guardar = $true; Previa = '' }
)
if (-not $SinPlus) {
    $entregables += @(
        @{ Ruta = 'src\M-INV_V1_Core.xlsm'; Clave = $corePwd; Guardar = $false; Previa = $capturasPlus },
        @{ Ruta = 'releases\M-INV_V1_Produccion_Bloqueado.xlsm'; Clave = $relPwd; Guardar = $false; Previa = '' }
    )
}
foreach ($e in $entregables) {
    $vArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $verify, '-Path', (Join-Path $root $e.Ruta),
        '-Password', $e.Clave)
    if ($e.Guardar) { $vArgs += '-Save' }
    if ($Capturas -and $e.Previa) { $vArgs += @('-PreviewDir', $e.Previa) }
    Paso ('Verificar ' + $e.Ruta) { & powershell @vArgs }
}

if ($Capturas -and -not $SinPlus) {
    $destino = Join-Path $root 'docs\product\capturas\plus'
    New-Item -ItemType Directory -Force -Path $destino | Out-Null
    foreach ($hoja in @('00_PORTADA', '10_MOVIMIENTOS', '12_REGISTRO')) {
        $png = Join-Path $capturasPlus ('M-INV_V1_Core__{0}.png' -f $hoja)
        if (Test-Path $png) { Copy-Item $png (Join-Path $destino ('{0}.png' -f $hoja)) -Force }
        else { $script:fallas += ('Captura Plus ' + $hoja) }
    }
    Write-Output ('Capturas de la edicion Plus copiadas a ' + $destino)
}

Write-Output ''
if ($script:fallas.Count -gt 0) {
    Write-Output ('RESULTADO: fallaron ' + $script:fallas.Count + ' paso(s): ' + ($script:fallas -join ' | '))
    exit 1
}
Write-Output 'RESULTADO: todos los pasos sin fallas'

<#
.SYNOPSIS
    M-INV V3 - Publica el cliente de escritorio (M-INV.exe) listo para copiar a una estacion.

.DESCRIPTION
    Genera dist\M-INV-<version>-win-x64\ con:
      M-INV.exe            ejecutable unico (icono, pantalla de carga, inicio de sesion y demostracion)
      appsettings.json     cadena de conexion a PostgreSQL y perifericos (editable)
      Demo\                libro de la V2.1 para "Explorar la demostracion"
    Por defecto depende del runtime de escritorio de .NET 8 o superior instalado en la estacion (RollForward=Major).
    Con -Autocontenido incluye el runtime (no necesita .NET instalado; la primera vez descarga los paquetes del runtime
    de nuget.org, unos 150 MB). Script ASCII a proposito (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
    powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1 -Autocontenido
#>
param([switch]$Autocontenido, [string]$Salida = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$sufijo = if ($Autocontenido) { 'win-x64-autocontenido' } else { 'win-x64' }
if (-not $Salida) { $Salida = Join-Path $root ('dist\M-INV-' + $version + '-' + $sufijo) }
if (Test-Path $Salida) { Remove-Item -Recurse -Force $Salida }
$sc = if ($Autocontenido) { 'true' } else { 'false' }
dotnet publish 'src/3. Presentation/MINV.DesktopClient' -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $Salida -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Output 'RESULTADO: la publicacion fallo.'; exit 1 }
Get-ChildItem $Salida -Filter *.pdb -Recurse | Remove-Item -Force
$exe = Join-Path $Salida 'M-INV.exe'
$mb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Output ''
Write-Output ('Publicado: ' + $exe + ' (' + $mb + ' MB)')
Write-Output 'Copie la carpeta completa a la estacion y abra M-INV.exe.'
if (-not $Autocontenido) { Write-Output 'Requiere el runtime de escritorio de .NET 8 o superior (Windows Desktop Runtime).' }

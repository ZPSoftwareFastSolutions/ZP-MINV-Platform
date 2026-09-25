<#
.SYNOPSIS
    M-INV V3 - Ciclo completo (definicion de terminado, regla A-11).

.DESCRIPTION
    1. dotnet tool restore                      Herramienta local dotnet-ef (dotnet-tools.json).
    2. dotnet build MINV.sln -c Release         Advertencias como errores (Directory.Build.props).
    3. dotnet test  MINV.sln -c Release         Dominio, aplicacion, hardware e infraestructura (paridad con la V2.1).
                                                Con MINV_TEST_PG definida, tambien las pruebas contra PostgreSQL real.
    4. dotnet ef migrations has-pending-model-changes   El modelo no puede tener cambios sin migracion.
    5. scripts\db_init.sql                      Se regenera: cabecera + "dotnet ef migrations script --idempotent".
    Termina con codigo 1 si falla cualquier paso. Script ASCII a proposito (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1
    $env:MINV_TEST_PG = 'Host=localhost;Username=postgres;Password=postgres;Database=postgres'; powershell -File tools\build_v3.ps1
#>
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$infra = 'src/2. Infrastructure/MINV.Infrastructure'
$script:fallas = @()

function Paso([string]$titulo, [scriptblock]$accion) {
    Write-Output ''
    Write-Output ('######## ' + $titulo)
    $global:LASTEXITCODE = 0
    & $accion
    if ($LASTEXITCODE -ne 0) { $script:fallas += $titulo }
}

Paso 'Herramientas locales (dotnet-ef)' { dotnet tool restore }
Paso ('Compilar MINV.sln (' + $Configuration + ', advertencias como errores)') { dotnet build MINV.sln -c $Configuration -nologo -v q }
if ($script:fallas.Count -gt 0) { Write-Output 'RESULTADO: la compilacion fallo; no se continua.'; exit 1 }

if (-not $env:MINV_TEST_PG) { Write-Output '[aviso]   MINV_TEST_PG no definida: se omiten las pruebas contra PostgreSQL real.' }
Paso 'Pruebas (dominio, aplicacion, hardware, infraestructura y paridad V2.1)' {
    dotnet test MINV.sln -c $Configuration --no-build -nologo -v q
}

Paso 'Migraciones al dia con el modelo' {
    dotnet ef migrations has-pending-model-changes --project $infra --startup-project $infra --configuration $Configuration --no-build
}

Paso 'Regenerar scripts\db_init.sql' {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('minv_migrations_' + [Guid]::NewGuid().ToString('N') + '.sql')
    dotnet ef migrations script --idempotent --project $infra --startup-project $infra --configuration $Configuration --no-build --output $tmp
    if ($LASTEXITCODE -eq 0) {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        $header = [IO.File]::ReadAllText((Join-Path $root 'scripts\db_init.header.sql'), [Text.Encoding]::UTF8)
        $body = [IO.File]::ReadAllText($tmp, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
        [IO.File]::WriteAllText((Join-Path $root 'scripts\db_init.sql'), $header + $body.Replace("`r`n", "`n"), $utf8)
        Remove-Item $tmp -Force
        $tablas = (Select-String -Path (Join-Path $root 'scripts\db_init.sql') -Pattern 'CREATE TABLE' | Measure-Object).Count
        Write-Output ('scripts\db_init.sql regenerado: ' + $tablas + ' sentencias CREATE TABLE (96 tablas + historial de migraciones)')
    }
}

Write-Output ''
if ($script:fallas.Count -gt 0) {
    Write-Output ('RESULTADO: fallaron ' + $script:fallas.Count + ' paso(s): ' + ($script:fallas -join ' | '))
    exit 1
}
Write-Output 'RESULTADO: todos los pasos sin fallas'

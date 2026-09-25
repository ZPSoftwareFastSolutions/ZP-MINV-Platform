<#
.SYNOPSIS
    M-INV V3/V4 - Ciclo completo (definicion de terminado, reglas A-11 y B-14).

.DESCRIPTION
    1. dotnet tool restore                      Herramienta local dotnet-ef (dotnet-tools.json).
    2. dotnet build MINV.sln -c Release         Advertencias como errores (Directory.Build.props).
    3. dotnet test  MINV.sln -c Release         Dominio, aplicacion, hardware, infraestructura (paridad con la V2.1),
                                                escritorio e integracion V4 (servidor en la nube y API Gateway en Kestrel).
                                                Con MINV_TEST_PG definida, tambien las pruebas contra PostgreSQL real.
    4. dotnet ef migrations has-pending-model-changes   El modelo no puede tener cambios sin migracion.
    5. scripts\db_init.sql                      Se regenera: cabecera + "dotnet ef migrations script --idempotent".
    6. -Capturas                                Cliente de escritorio: M-INV.exe --capturas con la demostracion (V2.1)
                                                -> docs\product\capturas\v4 (claro, oscuro y por rol; regla A-11).
                                                Si existe la base local de prueba (tools\bd_local.ps1), las pantallas de
                                                negocio (POS, ventas, compras, reportes, contabilidad) se capturan con ella.
    7. -Publicar                                tools\publicar_escritorio.ps1 -> dist\M-INV-<version>-win-x64\M-INV.exe
    Termina con codigo 1 si falla cualquier paso. Script ASCII a proposito (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1
    $env:MINV_TEST_PG = 'Host=localhost;Username=postgres;Password=postgres;Database=postgres'; powershell -File tools\build_v3.ps1
#>
param([string]$Configuration = 'Release', [switch]$Capturas, [switch]$Publicar)
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
Paso 'Pruebas (dominio, aplicacion, hardware, infraestructura, escritorio, integracion V4 y paridad V2.1)' {
    dotnet test MINV.sln -c $Configuration --no-build -nologo -v q
}

Paso 'Migraciones al dia con el modelo' {
    dotnet ef migrations has-pending-model-changes --context MinvWriteDbContext --project $infra --startup-project $infra --configuration $Configuration --no-build
}

Paso 'Regenerar scripts\db_init.sql' {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('minv_migrations_' + [Guid]::NewGuid().ToString('N') + '.sql')
    dotnet ef migrations script --idempotent --context MinvWriteDbContext --project $infra --startup-project $infra --configuration $Configuration --no-build --output $tmp
    if ($LASTEXITCODE -eq 0) {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        $header = [IO.File]::ReadAllText((Join-Path $root 'scripts\db_init.header.sql'), [Text.Encoding]::UTF8)
        $body = [IO.File]::ReadAllText($tmp, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
        [IO.File]::WriteAllText((Join-Path $root 'scripts\db_init.sql'), $header + $body.Replace("`r`n", "`n"), $utf8)
        Remove-Item $tmp -Force
        $tablas = (Select-String -Path (Join-Path $root 'scripts\db_init.sql') -Pattern 'CREATE TABLE' | Measure-Object).Count
        Write-Output ('scripts\db_init.sql regenerado: ' + $tablas + ' sentencias CREATE TABLE (110 tablas + historial de migraciones)')
    }
}

if ($Capturas) {
    Paso 'Capturas del cliente de escritorio (docs\product\capturas\v4)' {
        $exe = Get-ChildItem ('src\3. Presentation\MINV.DesktopClient\bin\' + $Configuration) -Filter 'M-INV.exe' -Recurse | Select-Object -First 1
        $dir = Join-Path $root 'docs\product\capturas\v4'
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        $usuarios = Join-Path $env:LOCALAPPDATA 'M-INV\usuarios-prueba.txt'
        if (Test-Path $usuarios) { $env:MINV_CAPTURAS_USUARIOS = $usuarios; Write-Output 'Pantallas de negocio con la base LOCAL de prueba.' }
        $p = Start-Process -FilePath $exe.FullName -ArgumentList '--capturas', ('"' + $dir + '"') -PassThru
        if (-not $p.WaitForExit(600000)) { Stop-Process -Id $p.Id -Force; Write-Output 'Las capturas no terminaron en 10 minutos.'; $global:LASTEXITCODE = 1 }
        else { $global:LASTEXITCODE = $p.ExitCode; Get-Content (Join-Path $dir 'capturas.log') -Encoding UTF8 | Select-Object -First 1 }
    }
}

if ($Publicar) {
    Paso 'Publicar el cliente de escritorio (dist)' { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publicar_escritorio.ps1') }
}

Write-Output ''
if ($script:fallas.Count -gt 0) {
    Write-Output ('RESULTADO: fallaron ' + $script:fallas.Count + ' paso(s): ' + ($script:fallas -join ' | '))
    exit 1
}
Write-Output 'RESULTADO: todos los pasos sin fallas'

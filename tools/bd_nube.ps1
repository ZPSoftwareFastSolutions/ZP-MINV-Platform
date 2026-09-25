<#
.SYNOPSIS
    M-INV V4 - Prepara una base PostgreSQL gestionada en la nube (DigitalOcean, AWS RDS, Supabase u otra).

.DESCRIPTION
    Acciones (-Accion):
      preparar  Con la cadena del rol DUENO/administrador de la base en la nube (-Conexion):
                  1. crea (o actualiza) los roles minv_server y minv_app con claves al azar (sin BYPASSRLS, sin ser
                     duenos de las tablas: la seguridad por filas los alcanza siempre);
                  2. aplica las migraciones (110 tablas, RLS por empresa y sucursal, funciones SECURITY DEFINER, modelo
                     de lectura y privilegios de los roles);
                  3. con -DatosPrueba, carga la empresa de prueba multi-sucursal;
                  4. verifica la base (minv verify).
                Guarda en %LOCALAPPDATA%\M-INV\credenciales-nube.txt las cadenas para los servidores (MINV_DB con
                minv_server) y las claves maestras de integracion (MINV_INTEGRATION_KEYS). Nada va al repositorio.
      estado    Verifica la base (tablas, triggers, RLS, conservacion) con la cadena del dueno.
    La cadena debe usar TLS verificado, por ejemplo:
      "Host=<servidor>;Port=25060;Database=minv;Username=doadmin;Password=<clave>;SSL Mode=VerifyFull;Root Certificate=<ca.crt>"
    Con PgBouncer use el modo SESION o la conexion directa: M-INV fija variables de sesion para la seguridad por filas.
    Script ASCII a proposito (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\bd_nube.ps1 -Accion preparar -Conexion "Host=...;SSL Mode=VerifyFull;..." -DatosPrueba
    powershell -ExecutionPolicy Bypass -File tools\bd_nube.ps1 -Accion estado -Conexion "Host=..."
#>
param(
    [ValidateSet('preparar', 'estado')][string]$Accion = 'preparar',
    [Parameter(Mandatory = $true)][string]$Conexion,
    [string]$Empresa = 'MINV',
    [switch]$DatosPrueba
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Join-Path $env:LOCALAPPDATA 'M-INV'
$salida = Join-Path $base 'credenciales-nube.txt'
$usuarios = Join-Path $base 'usuarios-nube.txt'
$cli = Join-Path $root 'src/4. Tools/MINV.Cli'
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $base | Out-Null

function Clave([int]$n = 28) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] $n
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

# Cambia usuario y clave de una cadena de conexion (el resto, incluido TLS, se conserva)
function Cadena-Para([string]$cadena, [string]$usuario, [string]$clave) {
    $partes = $cadena.Split(';') | Where-Object { $_ -and ($_ -notmatch '^\s*(Username|User ID|User|Password|Pwd)\s*=') }
    return (($partes + @('Username=' + $usuario, 'Password=' + $clave)) -join ';')
}

function Minv([string[]]$argumentos) {
    dotnet run --project $cli -c Release -- @argumentos
    if ($LASTEXITCODE -ne 0) { throw ('minv ' + $argumentos[0] + ' fallo.') }
}

if ($Conexion -notmatch 'SSL Mode\s*=\s*(VerifyFull|VerifyCA|Require)') {
    Write-Output 'AVISO: la cadena no exige TLS verificado (SSL Mode=VerifyFull). En la nube use siempre TLS verificado.'
}

if ($Accion -eq 'estado') {
    Minv @('verify', '--codigo', $Empresa, '--conexion', $Conexion)
    exit 0
}

Write-Output '1/4 Roles de conexion (minv_server, minv_app) ...'
$claveServer = Clave
$claveApp = Clave
Minv @('roles', '--conexion', $Conexion, '--clave-servidor', $claveServer, '--clave-app', $claveApp)

Write-Output '2/4 Migraciones (110 tablas, RLS, funciones, modelo de lectura, privilegios) ...'
Minv @('migrate', '--conexion', $Conexion)

$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$llaves = 'nube1:' + [Convert]::ToBase64String($bytes)
$texto = @(
    'M-INV - base de datos en la NUBE (solo este equipo; guarde estas claves en la boveda de su proveedor)',
    '# Servidor en la nube y API Gateway (variables de entorno):',
    ('MINV_DB=' + (Cadena-Para $Conexion 'minv_server' $claveServer)),
    ('MINV_INTEGRATION_KEYS=' + $llaves),
    '# Escritorio con conexion directa (solo si la red lo permite; se recomienda el modo Nube):',
    ('MINV_DB_APP=' + (Cadena-Para $Conexion 'minv_app' $claveApp))
)
Set-Content -Path $salida -Value $texto -Encoding UTF8

if ($DatosPrueba) {
    Write-Output '3/4 Datos de prueba multi-sucursal ...'
    $env:MINV_INTEGRATION_KEYS = $llaves
    try { Minv @('datos-prueba', '--codigo', $Empresa, '--conexion', $Conexion, '--credenciales', $usuarios, '--integracion', $salida) }
    finally { Remove-Item Env:\MINV_INTEGRATION_KEYS -ErrorAction SilentlyContinue }
}
else { Write-Output '3/4 Sin datos de prueba (use -DatosPrueba para cargarlos).' }

Write-Output '4/4 Verificacion ...'
Minv @('verify', '--codigo', $Empresa, '--conexion', $Conexion)
Write-Output ''
Write-Output ('Cadenas y claves de los servidores: ' + $salida)
if ($DatosPrueba) { Write-Output ('Usuarios de prueba: ' + $usuarios) }
Write-Output 'Siguiente paso: despliegue MINV.CloudServer y MINV.ApiGateway (deploy\docker-compose.yml) con esas variables.'

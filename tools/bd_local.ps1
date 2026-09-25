<#
.SYNOPSIS
    M-INV V3 - Base de datos PostgreSQL LOCAL (portable, sin instalador ni permisos de administrador).

.DESCRIPTION
    Acciones (-Accion):
      instalar   (por defecto) Extrae PostgreSQL 16 portable en %LOCALAPPDATA%\M-INV\postgresql-16, crea el cluster,
                 lo inicia en localhost:5432, crea los roles minv_owner y minv_app y la base "minv", aplica las
                 migraciones (97 tablas, 5FN, RLS, triggers) y carga los datos de prueba (minv datos-prueba).
                 Es idempotente: si algo ya existe, lo reutiliza.
      iniciar    Inicia el servidor.            detener   Lo detiene.            estado   Muestra si responde.
      recrear    Borra la base "minv", la vuelve a crear, migra y carga datos de prueba nuevos.
    Las contrasenas (superusuario postgres, minv_owner y los usuarios de la aplicacion) se generan al azar y se
    guardan SOLO en %LOCALAPPDATA%\M-INV\credenciales-bd-local.txt (fuera del repositorio). minv_app usa la clave de
    desarrollo "minv-dev" de appsettings.json (solo escucha en localhost).
    -Autoiniciar  deja un acceso en la carpeta Inicio de Windows para que PostgreSQL arranque al iniciar sesion.
    Script ASCII a proposito (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1
    powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear
    powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion iniciar -Autoiniciar
#>
param(
    [ValidateSet('instalar', 'iniciar', 'detener', 'estado', 'recrear')][string]$Accion = 'instalar',
    [string]$Zip = '',
    [int]$Puerto = 5432,
    [switch]$SinDatos,
    [switch]$Autoiniciar
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Join-Path $env:LOCALAPPDATA 'M-INV'
$pgHome = Join-Path $base 'postgresql-16'
$pgBin = Join-Path $pgHome 'bin'
$data = Join-Path $base 'pgdata'
$log = Join-Path $base 'postgresql.log'
$cred = Join-Path $base 'credenciales-bd-local.txt'
$usuarios = Join-Path $base 'usuarios-prueba.txt'
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Force -Path $base | Out-Null

function Clave([int]$n = 18) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] $n
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

function Leer-Credencial([string]$clave) {
    if (-not (Test-Path $cred)) { return $null }
    $linea = Get-Content $cred -Encoding UTF8 | Where-Object { $_ -like ($clave + '=*') } | Select-Object -First 1
    if ($linea) { return $linea.Substring($clave.Length + 1) } else { return $null }
}

function Guardar-Credencial([string]$clave, [string]$valor) {
    $lineas = @()
    if (Test-Path $cred) { $lineas = @(Get-Content $cred -Encoding UTF8 | Where-Object { $_ -notlike ($clave + '=*') }) }
    $lineas += ($clave + '=' + $valor)
    Set-Content -Path $cred -Value $lineas -Encoding UTF8
}

function Psql([string]$usuario, [string]$clave, [string]$base, [string]$sql) {
    $env:PGPASSWORD = $clave
    $salida = & (Join-Path $pgBin 'psql.exe') -h localhost -p $Puerto -U $usuario -d $base -v ON_ERROR_STOP=1 -tAc $sql 2>&1
    $codigo = $LASTEXITCODE
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    if ($codigo -ne 0) { throw ('psql: ' + ($salida -join ' ')) }
    return $salida
}

function Responde {
    & (Join-Path $pgBin 'pg_isready.exe') -h localhost -p $Puerto -q 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Iniciar {
    if (Responde) { Write-Output ('PostgreSQL ya responde en localhost:' + $Puerto); return }
    # Sin redirigir la salida: el servidor hereda los handles y una tuberia nunca se cerraria.
    $argumentos = 'start -D "' + $data + '" -l "' + $log + '" -w -t 60 -o "-p ' + $Puerto + ' -c listen_addresses=localhost"'
    $p = Start-Process -FilePath (Join-Path $pgBin 'pg_ctl.exe') -ArgumentList $argumentos -WindowStyle Hidden -PassThru
    $p.WaitForExit(70000) | Out-Null
    if (-not (Responde)) { throw ('PostgreSQL no inicio. Revise ' + $log) }
    Write-Output ('PostgreSQL iniciado en localhost:' + $Puerto)
}

function Instalar-Binarios {
    if (Test-Path (Join-Path $pgBin 'postgres.exe')) { return }
    if (-not $Zip) {
        $Zip = Get-ChildItem (Join-Path $base 'descargas') -Filter 'postgresql-16*-windows-x64-binaries.zip' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $Zip -or -not (Test-Path $Zip)) {
        throw 'Falta el zip de PostgreSQL 16 portable. Descarguelo de https://www.enterprisedb.com/download-postgresql-binaries (Windows x86-64) en %LOCALAPPDATA%\M-INV\descargas o indique -Zip.'
    }
    Write-Output ('Extrayendo PostgreSQL de ' + $Zip + ' ...')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archivo = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        foreach ($e in $archivo.Entries) {
            $n = $e.FullName
            if (-not ($n.StartsWith('pgsql/bin/') -or $n.StartsWith('pgsql/lib/') -or $n.StartsWith('pgsql/share/'))) { continue }
            $destino = Join-Path $pgHome ($n.Substring(6).Replace('/', '\'))
            if ($n.EndsWith('/')) { New-Item -ItemType Directory -Force -Path $destino | Out-Null; continue }
            New-Item -ItemType Directory -Force -Path (Split-Path $destino -Parent) | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $destino, $true)
        }
    }
    finally { $archivo.Dispose() }
    Write-Output ('PostgreSQL extraido en ' + $pgHome)
}

function Crear-Cluster {
    if (Test-Path (Join-Path $data 'PG_VERSION')) { return }
    $claveSuper = Clave
    Guardar-Credencial 'postgres' $claveSuper
    $archivoClave = Join-Path $base 'initdb.tmp'
    Set-Content -Path $archivoClave -Value $claveSuper -Encoding ASCII -NoNewline
    try {
        & (Join-Path $pgBin 'initdb.exe') -D $data -U postgres --pwfile=$archivoClave -E UTF8 --locale=C -A scram-sha-256 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'initdb fallo.' }
    }
    finally { Remove-Item $archivoClave -Force -ErrorAction SilentlyContinue }
    Write-Output ('Cluster creado en ' + $data)
}

function Crear-Base([switch]$Borrar) {
    $claveSuper = Leer-Credencial 'postgres'
    $claveOwner = Leer-Credencial 'minv_owner'
    if (-not $claveOwner) { $claveOwner = Clave; Guardar-Credencial 'minv_owner' $claveOwner }
    Guardar-Credencial 'minv_app' 'minv-dev'
    $existe = Psql 'postgres' $claveSuper 'postgres' "SELECT 1 FROM pg_roles WHERE rolname = 'minv_owner'"
    if (-not $existe) { Psql 'postgres' $claveSuper 'postgres' ("CREATE ROLE minv_owner LOGIN PASSWORD '" + $claveOwner + "'") | Out-Null }
    else { Psql 'postgres' $claveSuper 'postgres' ("ALTER ROLE minv_owner PASSWORD '" + $claveOwner + "'") | Out-Null }
    $existe = Psql 'postgres' $claveSuper 'postgres' "SELECT 1 FROM pg_roles WHERE rolname = 'minv_app'"
    if (-not $existe) { Psql 'postgres' $claveSuper 'postgres' "CREATE ROLE minv_app LOGIN PASSWORD 'minv-dev'" | Out-Null }
    if ($Borrar) {
        Psql 'postgres' $claveSuper 'postgres' "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'minv' AND pid <> pg_backend_pid()" | Out-Null
        Psql 'postgres' $claveSuper 'postgres' 'DROP DATABASE IF EXISTS minv' | Out-Null
    }
    $existe = Psql 'postgres' $claveSuper 'postgres' "SELECT 1 FROM pg_database WHERE datname = 'minv'"
    if (-not $existe) {
        Psql 'postgres' $claveSuper 'postgres' "CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0" | Out-Null
        Write-Output 'Base "minv" creada.'
    }
    $cadena = 'Host=localhost;Port=' + $Puerto + ';Database=minv;Username=minv_owner;Password=' + $claveOwner
    Write-Output 'Aplicando las migraciones (97 tablas, 5FN, triggers, RLS, vistas) ...'
    dotnet run --project (Join-Path $root 'src/4. Tools/MINV.Cli') -c Release -- migrate --conexion $cadena
    if ($LASTEXITCODE -ne 0) { throw 'minv migrate fallo.' }
    $tablas = Psql 'minv_owner' $claveOwner 'minv' "SELECT count(*) FROM information_schema.tables WHERE table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting') AND table_type = 'BASE TABLE' AND table_name <> '__ef_migrations_history'"
    Write-Output ('Tablas de M-INV en la base: ' + $tablas)
    if (-not $SinDatos) {
        Write-Output 'Cargando datos de prueba (empresa, roles, usuarios, catalogo con imagenes, 60 dias de operacion) ...'
        dotnet run --project (Join-Path $root 'src/4. Tools/MINV.Cli') -c Release -- datos-prueba --conexion $cadena --credenciales $usuarios
        if ($LASTEXITCODE -ne 0) { throw 'minv datos-prueba fallo.' }
    }
}

function Autoinicio {
    $inicio = [Environment]::GetFolderPath('Startup')
    $vbs = Join-Path $inicio 'M-INV PostgreSQL local.vbs'
    $cmd = '"' + (Join-Path $pgBin 'pg_ctl.exe') + '" start -D "' + $data + '" -l "' + $log + '" -o "-p ' + $Puerto + ' -c listen_addresses=localhost"'
    $texto = 'CreateObject("WScript.Shell").Run "' + $cmd.Replace('"', '""') + '", 0, False'
    Set-Content -Path $vbs -Value $texto -Encoding ASCII
    Write-Output ('Autoinicio configurado: ' + $vbs)
}

switch ($Accion) {
    'estado' { if (Responde) { Write-Output ('PostgreSQL responde en localhost:' + $Puerto) } else { Write-Output 'PostgreSQL no responde.'; exit 1 } }
    'detener' { & (Join-Path $pgBin 'pg_ctl.exe') stop -D $data -m fast; exit $LASTEXITCODE }
    'iniciar' { Iniciar }
    'recrear' { Iniciar; Crear-Base -Borrar }
    default { Instalar-Binarios; Crear-Cluster; Iniciar; Crear-Base }
}
if ($Autoiniciar) { Autoinicio }
if ($Accion -in @('instalar', 'recrear')) {
    Write-Output ''
    Write-Output ('Claves de PostgreSQL (solo en este equipo): ' + $cred)
    Write-Output ('Usuarios de prueba de M-INV (empresa, correos y contrasenas): ' + $usuarios)
    Write-Output 'Abra M-INV.exe e ingrese con la empresa y un usuario de ese archivo.'
}

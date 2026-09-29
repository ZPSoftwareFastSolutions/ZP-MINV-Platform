<#
.SYNOPSIS
    M-INV V6 - Tienda publica en Docker Desktop de ESTE equipo (servidores + catalogo web + enlace trycloudflare).

.DESCRIPTION
    subir      Detiene los servidores locales (tools\servidores_locales.ps1) y lo que ocupe 5080/5090/5095/5173, apaga el
               catalogo V5 de prueba (minv-webcatalog-test) si esta encendido, escribe deploy\.env (NO se versiona) con el
               rol minv_server de la base local (via host.docker.internal) y las claves de %LOCALAPPDATA%\M-INV (sin
               mostrarlas), construye las imagenes 7.0.0-alpha.1 y levanta: servidor en la nube :5080, API Gateway :5090,
               simulador del SIN :5095, catalogo web :5173 y el TUNEL publico (Cloudflare). Al final muestra el enlace.
    reanudar   Lo que corre al iniciar sesion en Windows (tarea programada): enciende PostgreSQL, espera a Docker Desktop,
               levanta los contenedores (sin construir) y guarda el enlace nuevo del tunel.
    enlace     Muestra el enlace publico vigente (cambia cada vez que el tunel se reinicia) y lo guarda en
               %LOCALAPPDATA%\M-INV\enlace-publico.txt.
    estado     Contenedores y salud de cada servicio.
    arranque   Registra la tarea programada "M-INV Tienda publica" (al iniciar sesion ejecuta -Accion reanudar).
    bajar      Detiene y elimina los contenedores del proyecto "minv" (imagenes y volumen del simulador quedan).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir
#>
param(
    [ValidateSet('subir', 'reanudar', 'enlace', 'estado', 'arranque', 'bajar')][string]$Accion = 'subir',
    [int]$PuertoBd = 5432,
    [string]$EmpresaTienda = 'TECHZONE',
    [string]$SucursalTienda = 'CM',
    [switch]$ReiniciarSimulador
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Join-Path $env:LOCALAPPDATA 'M-INV'
$cred = Join-Path $base 'credenciales-bd-local.txt'
$claves = Join-Path $base 'claves-integracion.txt'
$estadoSimulador = Join-Path $base 'siat-simulador.json'
$archivoEnlace = Join-Path $base 'enlace-publico.txt'
$registro = Join-Path $base 'docker-local.log'
$compose = Join-Path $root 'deploy\docker-compose.yml'
$envFile = Join-Path $root 'deploy\.env'
$perfiles = @('--profile', 'siat-simulador', '--profile', 'publico')

function Leer([string]$archivo, [string]$clave) {
    if (-not (Test-Path $archivo)) { return $null }
    $linea = Get-Content $archivo -Encoding UTF8 | Where-Object { $_ -like ($clave + '=*') } | Select-Object -First 1
    if ($linea) { return $linea.Substring($clave.Length + 1) } else { return $null }
}

function Dc {
    & docker compose -f $compose --env-file $envFile @perfiles @args
    if ($LASTEXITCODE -ne 0) { throw ('docker compose ' + ($args -join ' ') + ' fallo (codigo ' + $LASTEXITCODE + ')') }
}

function DockerListo([int]$segundos) {
    $hasta = (Get-Date).AddSeconds($segundos)
    do {
        & docker version --format '{{.Server.Version}}' *> $null
        if ($LASTEXITCODE -eq 0) { return $true }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $hasta)
    return $false
}

function Responde([string]$url, [int]$segundos) {
    $hasta = (Get-Date).AddSeconds($segundos)
    do {
        try {
            $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
            if ($r.StatusCode -eq 200) { return $true }
        }
        catch { }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $hasta)
    return $false
}

# Write-Host: la funcion devuelve SOLO true/false (lo escrito con Write-Output se sumaria al resultado)
function Linea([string]$nombre, [string]$url, [int]$segundos) {
    if (Responde $url $segundos) { Write-Host ('OK  ' + $nombre.PadRight(24) + $url); return $true }
    Write-Host ('NO  ' + $nombre.PadRight(24) + $url + '  (no responde)')
    return $false
}

# Enlace https*.trycloudflare.com que imprime el tunel en su registro (el ultimo, por si se reinicio)
function Enlace([int]$segundos) {
    $hasta = (Get-Date).AddSeconds($segundos)
    do {
        $log = (& docker compose -f $compose --env-file $envFile @perfiles logs --no-color tunel 2>&1) -join "`n"
        $m = [regex]::Matches($log, 'https://[a-z0-9-]+\.trycloudflare\.com')
        if ($m.Count -gt 0) { return $m[$m.Count - 1].Value }
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $hasta)
    return $null
}

function GuardarEnlace {
    $url = Enlace 90
    if (-not $url) { Write-Host 'El tunel todavia no publico su enlace: vuelva a probar con -Accion enlace en un minuto.'; return $null }
    $texto = @(
        'M-INV V6 - Tienda publica (Tech Zone Gaming)',
        ('Enlace para los clientes: ' + $url),
        ('Actualizado: ' + (Get-Date -Format 'dd/MM/yyyy HH:mm')),
        'El enlace cambia cada vez que el tunel se reinicia (reinicio del equipo o de Docker Desktop).'
    )
    [IO.File]::WriteAllLines($archivoEnlace, $texto, (New-Object System.Text.UTF8Encoding($false)))
    return $url
}

function Estado {
    & docker compose -f $compose --env-file $envFile @perfiles ps | Out-Host
    $ok = $true
    $ok = (Linea 'Servidor en la nube' 'http://localhost:5080/api/v1/health' 5) -and $ok
    $ok = (Linea 'API Gateway' 'http://localhost:5090/health' 5) -and $ok
    $ok = (Linea 'Simulador del SIN' 'http://localhost:5095/control/estado' 5) -and $ok
    $ok = (Linea 'Catalogo web' 'http://localhost:5173/' 5) -and $ok
    $ok = (Linea 'Tienda (via el catalogo)' 'http://localhost:5173/storefront/v1/presets' 5) -and $ok
    $url = Enlace 5
    if ($url) { $ok = (Linea 'Enlace publico' ($url + '/storefront/v1/presets') 20) -and $ok }
    else { Write-Host 'NO  Enlace publico            (el tunel no esta encendido)'; $ok = $false }
    return $ok
}

# ------------------------------------------------------------------------------------------------------------------------
if ($Accion -eq 'arranque') {
    $script = Join-Path $PSScriptRoot 'docker_local.ps1'
    $accionTarea = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument ('-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $script + '" -Accion reanudar')
    $disparador = New-ScheduledTaskTrigger -AtLogOn -User ($env:USERDOMAIN + '\' + $env:USERNAME)
    $ajustes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
    Register-ScheduledTask -TaskName 'M-INV Tienda publica' -Action $accionTarea -Trigger $disparador -Settings $ajustes `
        -Description 'M-INV V6: al iniciar sesion enciende PostgreSQL, levanta los contenedores de Docker y guarda el enlace publico.' -Force | Out-Null
    Write-Output 'Tarea programada "M-INV Tienda publica" registrada: se ejecuta al iniciar sesion en Windows.'
    Write-Output ('El enlace nuevo queda en ' + $archivoEnlace)
    return
}

if ($Accion -eq 'reanudar') {
    Start-Transcript -Path $registro -Append | Out-Null
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'bd_local.ps1') -Accion iniciar | Out-Null
        # Docker Desktop: si no esta abierto (no arranca solo si su opcion "Start when you sign in" esta apagada), se abre
        if (-not (DockerListo 20)) {
            $exe = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
            if (Test-Path $exe) { Start-Process -FilePath $exe }
        }
        if (-not (DockerListo 600)) { throw 'Docker Desktop no arranco en 10 minutos.' }
        Dc up -d
        $url = GuardarEnlace
        if ($url) { Write-Output ('Enlace publico: ' + $url) }
    }
    finally { Stop-Transcript | Out-Null }
    return
}

if (-not (DockerListo 30)) { throw 'Docker Desktop no responde: abralo y espere a que diga "Engine running".' }

if ($Accion -eq 'bajar') {
    Dc down
    Write-Output 'Contenedores de M-INV detenidos y eliminados (la base local y las imagenes quedan).'
    return
}
if ($Accion -eq 'estado') { [void](Estado); return }
if ($Accion -eq 'enlace') {
    $url = GuardarEnlace
    if ($url) { Write-Output ('Enlace publico: ' + $url); Write-Output ('Guardado en ' + $archivoEnlace) }
    return
}

# --- subir -------------------------------------------------------------------------------------------------------------
$claveServer = Leer $cred 'minv_server'
$llaves = Leer $claves 'MINV_INTEGRATION_KEYS'
$tokenSiat = Leer $claves 'MINV_SIAT_TOKEN'
if (-not $claveServer -or -not $llaves) {
    throw 'Falta la base local (rol minv_server o claves de integracion). Ejecute antes: tools\bd_local.ps1 -Accion recrear'
}

# 1) La base local encendida y los puertos libres (servidores locales, web de desarrollo, catalogo V5 de prueba)
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'bd_local.ps1') -Accion iniciar | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'servidores_locales.ps1') -Accion detener | Out-Null
foreach ($puerto in 5080, 5090, 5095, 5173) {
    foreach ($c in @(Get-NetTCPConnection -LocalPort $puerto -State Listen -ErrorAction SilentlyContinue)) {
        $p = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
        if ($p -and $p.ProcessName -in @('node', 'dotnet')) {
            Stop-Process -Id $p.Id -Force
            Write-Output ('Detenido ' + $p.ProcessName + ' (proceso ' + $p.Id + ') que ocupaba el puerto ' + $puerto)
        }
    }
}
foreach ($viejo in 'minv-webcatalog-test', 'cloudflare-tunnel') {
    $corriendo = (& docker ps --filter ('name=^' + $viejo + '$') --format '{{.Names}}') 2>$null
    if ($corriendo) { & docker stop $viejo | Out-Null; Write-Output ('Detenido el contenedor anterior ' + $viejo + ' (V5).') }
}

# 2) deploy\.env (NO se versiona: .gitignore). Los contenedores llegan a la base local por host.docker.internal.
$lineas = @(
    '# Generado por tools\docker_local.ps1 para Docker Desktop de este equipo. NO lo versione.',
    ('MINV_DB=Host=host.docker.internal;Port=' + $PuertoBd + ';Database=minv;Username=minv_server;Password=' + $claveServer + ';SSL Mode=Disable'),
    'MINV_DB_READ=',
    ('MINV_INTEGRATION_KEYS=' + $llaves),
    'MINV_SIAT_BACKGROUND=true',
    ('MINV_SIAT_TOKEN=' + $tokenSiat),
    ('MINV_STOREFRONT_TENANT=' + $EmpresaTienda),
    ('MINV_STOREFRONT_BRANCH=' + $SucursalTienda),
    'MINV_STOREFRONT_ORIGIN=http://localhost:5173',
    'MINV_STOREFRONT_HOURS=48',
    'MINV_STOREFRONT_ENABLED=true',
    'MINV_FORWARDED_HEADERS=true',
    'MINV_WEB_API_URL=/',
    'MINV_BIND=127.0.0.1'
)
[IO.File]::WriteAllLines($envFile, $lineas, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'deploy\.env escrito con las claves de este equipo (no se muestra ni se versiona).'

# 3) Imagenes 7.0.0-alpha.1 (la primera vez tarda varios minutos)
Write-Output 'Construyendo las imagenes (nube, gateway, simulador y catalogo web)...'
Dc build

# 4) Estado del simulador del SIN de la carga de prueba (CUIS, CUFD y facturas) en su volumen, si no tiene uno propio
Dc create
$simulador = 'minv-siat-simulador-1'
if (Test-Path $estadoSimulador) {
    $tiene = (& docker run --rm -v minv_minv-siat:/data postgres:16-alpine sh -c 'test -f /data/siat-simulador.json && echo si') 2>$null
    if ($ReiniciarSimulador -or $tiene -ne 'si') {
        & docker stop $simulador 2>$null | Out-Null
        & docker cp $estadoSimulador ($simulador + ':/data/siat-simulador.json')
        if ($LASTEXITCODE -eq 0) { Write-Output 'Estado del simulador del SIN (carga de prueba) copiado al contenedor.' }
    }
}

# 5) Levantar todo y esperar (el estado copiado queda del usuario "app" del contenedor para que el simulador lo actualice)
Dc up -d
& docker exec -u 0 $simulador chown -R 1654:1654 /data 2>$null | Out-Null
Write-Output ''
Write-Output 'Esperando a que los servicios respondan...'
[void](Responde 'http://localhost:5090/health' 120)
$url = GuardarEnlace
$ok = Estado
Write-Output ''
if ($url) {
    Write-Output '================================================================================'
    Write-Output ('  ENLACE PUBLICO PARA TUS CLIENTES:  ' + $url)
    Write-Output '================================================================================'
}
Write-Output '  Catalogo en este equipo:  http://localhost:5173'
Write-Output '  Escritorio:               M-INV.exe -> Nube -> http://localhost:5080 -> empresa TECHZONE'
Write-Output '  Ver el enlace vigente:    powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion enlace'
Write-Output '  Detener todo:             powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion bajar'
if (-not $ok) { Write-Output 'Algun servicio no responde. Registros:  docker compose -f deploy\docker-compose.yml logs --tail 60'; exit 1 }

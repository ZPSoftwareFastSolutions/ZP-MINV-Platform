<#
.SYNOPSIS
    M-INV V4 - Simula la nube en este equipo: servidor en la nube (escritorio) y API Gateway (integraciones B2B).

.DESCRIPTION
    Acciones (-Accion):
      iniciar   (por defecto) Compila (Release) y arranca en segundo plano:
                  MINV.CloudServer  en http://localhost:5080  (el escritorio en modo "Nube" se conecta aqui)
                  MINV.ApiGateway   en http://localhost:5090  (API B2B: /v1/..., documentacion en /docs)
                contra la base LOCAL (tools\bd_local.ps1) con el rol minv_server, que NO puede saltarse la seguridad
                por filas. Lee las claves de %LOCALAPPDATA%\M-INV\credenciales-bd-local.txt y claves-integracion.txt.
      detener   Detiene ambos servidores.
      estado    Consulta /health de ambos.
    Registros: %LOCALAPPDATA%\M-INV\servidor-nube.log y api-gateway.log. Script ASCII a proposito (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1
    powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion estado
    powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
#>
param(
    [ValidateSet('iniciar', 'detener', 'estado')][string]$Accion = 'iniciar',
    [int]$PuertoNube = 5080,
    [int]$PuertoApi = 5090,
    [int]$PuertoBd = 5432
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Join-Path $env:LOCALAPPDATA 'M-INV'
$cred = Join-Path $base 'credenciales-bd-local.txt'
$claves = Join-Path $base 'claves-integracion.txt'
$pids = Join-Path $base 'servidores.pid'
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Leer([string]$archivo, [string]$clave) {
    if (-not (Test-Path $archivo)) { return $null }
    $linea = Get-Content $archivo -Encoding UTF8 | Where-Object { $_ -like ($clave + '=*') } | Select-Object -First 1
    if ($linea) { return $linea.Substring($clave.Length + 1) } else { return $null }
}

function Salud([string]$url) {
    try {
        $r = Invoke-RestMethod -Uri $url -TimeoutSec 3
        return ('OK - version ' + $r.version)
    }
    catch { return $null }
}

function Detener {
    if (Test-Path $pids) {
        foreach ($id in (Get-Content $pids)) {
            $p = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
            # /T: tambien el proceso dotnet que lanzo el PowerShell intermedio
            if ($p) { & taskkill.exe /PID $p.Id /T /F | Out-Null; Write-Output ('Detenido el proceso ' + $p.Id + ' y sus hijos') }
        }
        Remove-Item $pids -Force
    }
}

function Estado {
    $nube = Salud ('http://localhost:' + $PuertoNube + '/api/v1/health')
    $api = Salud ('http://localhost:' + $PuertoApi + '/health')
    # Write-Host: la funcion devuelve solo el resultado (true/false); los mensajes van a la consola
    if ($nube) { Write-Host ('Servidor en la nube  http://localhost:' + $PuertoNube + '  ' + $nube) } else { Write-Host ('Servidor en la nube  http://localhost:' + $PuertoNube + '  NO RESPONDE') }
    if ($api) { Write-Host ('API Gateway          http://localhost:' + $PuertoApi + '  ' + $api + '  (documentacion: /docs)') } else { Write-Host ('API Gateway          http://localhost:' + $PuertoApi + '  NO RESPONDE') }
    return ($nube -and $api)
}

function Iniciar {
    $claveServer = Leer $cred 'minv_server'
    $llaves = Leer $claves 'MINV_INTEGRATION_KEYS'
    if (-not $claveServer -or -not $llaves) {
        throw 'Falta la base local V4 (rol minv_server o claves de integracion). Ejecute antes: tools\bd_local.ps1 -Accion recrear'
    }
    Detener
    Write-Output 'Compilando los servidores (Release) ...'
    foreach ($proyecto in @('src/3. Presentation/MINV.CloudServer', 'src/3. Presentation/MINV.ApiGateway')) {
        dotnet build (Join-Path $root $proyecto) -c Release -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw ('No se pudo compilar ' + $proyecto) }
    }
    $env:MINV_DB = 'Host=localhost;Port=' + $PuertoBd + ';Database=minv;Username=minv_server;Password=' + $claveServer
    $env:MINV_INTEGRATION_KEYS = $llaves
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $lista = @()
    $servidores = @(
        @{ Nombre = 'MINV.CloudServer'; Puerto = $PuertoNube; Log = 'servidor-nube.log' },
        @{ Nombre = 'MINV.ApiGateway'; Puerto = $PuertoApi; Log = 'api-gateway.log' }
    )
    foreach ($s in $servidores) {
        $dll = Get-ChildItem (Join-Path $root ('src\3. Presentation\' + $s.Nombre + '\bin\Release')) -Filter ($s.Nombre + '.dll') -Recurse | Select-Object -First 1
        $env:ASPNETCORE_URLS = 'http://localhost:' + $s.Puerto
        $log = Join-Path $base $s.Log
        # Sin -Redirect*: Start-Process usa ShellExecute y el servidor NO hereda los handles de esta consola (si los
        # heredara, quien ejecuta este script quedaria esperando hasta que el servidor termine). La salida la redirige
        # un PowerShell oculto intermedio.
        $orden = "& dotnet '" + $dll.FullName + "' *> '" + $log + "'"
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -Command "' + $orden + '"') `
            -WorkingDirectory $dll.DirectoryName -WindowStyle Hidden -PassThru
        $lista += $p.Id
        Write-Output ('Iniciado ' + $s.Nombre + ' (proceso ' + $p.Id + ') en http://localhost:' + $s.Puerto)
    }
    Set-Content -Path $pids -Value $lista -Encoding ASCII
    Remove-Item Env:\MINV_DB, Env:\MINV_INTEGRATION_KEYS, Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        if ((Salud ('http://localhost:' + $PuertoNube + '/api/v1/health')) -and (Salud ('http://localhost:' + $PuertoApi + '/health'))) { break }
    }
    if (-not (Estado)) { throw ('Algun servidor no respondio. Revise ' + (Join-Path $base 'servidor-nube.log') + ' y api-gateway.log') }
    $token = Leer $claves 'MINV_API_KEY'
    Write-Output ''
    Write-Output ('Escritorio: abra M-INV.exe, elija "Nube (servidor M-INV)" y use el servidor http://localhost:' + $PuertoNube)
    Write-Output ('API B2B:    documentacion en http://localhost:' + $PuertoApi + '/docs')
    if ($token) {
        Write-Output ('            curl.exe -H "Authorization: Bearer <MINV_API_KEY de ' + $claves + '>" http://localhost:' + $PuertoApi + '/v1/catalog?pageSize=5')
    }
}

switch ($Accion) {
    'detener' { Detener }
    'estado' { if (-not (Estado)) { exit 1 } }
    default { Iniciar }
}

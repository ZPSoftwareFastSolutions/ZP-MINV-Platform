<#
.SYNOPSIS
    M-INV V4/V4.1/V6 - Simula la nube en este equipo: simulador del SIN (facturacion), servidor en la nube (escritorio) y
    API Gateway (integraciones B2B y, V6, la API publica de la tienda web /storefront/v1).

.DESCRIPTION
    Acciones (-Accion):
      iniciar   (por defecto) Compila (Release) y arranca en segundo plano, EN ESTE ORDEN:
                  MINV.SiatSimulator en http://localhost:5095  (V4.1: simulador del SIN; datos de SIMULACION sin valor
                                                                legal). Lee el estado que dejo la carga de datos de prueba
                                                                (%LOCALAPPDATA%\M-INV\siat-simulador.json: CUIS, CUFD,
                                                                facturas, eventos y paquetes) y acepta SOLO el token de
                                                                simulacion MINV_SIAT_TOKEN de claves-integracion.txt.
                  MINV.CloudServer   en http://localhost:5080  (el escritorio en modo "Nube" se conecta aqui). V4.1: corre
                                                                el trabajo automatico de la facturacion (envia las facturas
                                                                pendientes, recupera los cortes, pide el CUFD de cada dia).
                  MINV.ApiGateway    en http://localhost:5090  (API B2B: /v1/..., documentacion en /docs; V6: tienda web
                                                                /storefront/v1 de la empresa -EmpresaTienda, TECHZONE, sucursal
                                                                -SucursalTienda, CM, para el catalogo web en http://localhost:5173)
                contra la base LOCAL (tools\bd_local.ps1) con el rol minv_server, que NO puede saltarse la seguridad
                por filas. Lee las claves de %LOCALAPPDATA%\M-INV\credenciales-bd-local.txt y claves-integracion.txt.
      detener   Detiene los tres (el simulador guarda su estado en el archivo antes de cerrarse en cada operacion).
      estado    Consulta /health de los tres y si el simulador del SIN "tiene internet".
    -SinSimulador  no inicia el simulador del SIN (por ejemplo, si la facturacion apunta al SIN real del piloto).
    Corte de internet simulado (con los servidores iniciados):
      dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-apagar      (las cajas facturan fuera de linea)
      dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-encender    (el servidor en la nube recupera solo)
    Registros: %LOCALAPPDATA%\M-INV\simulador-sin.log, servidor-nube.log y api-gateway.log. Script ASCII a proposito
    (PowerShell 5.1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1
    powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion estado
    powershell -ExecutionPolicy Bypass -File tools\servidores_locales.ps1 -Accion detener
#>
param(
    [ValidateSet('iniciar', 'detener', 'estado')][string]$Accion = 'iniciar',
    [int]$PuertoNube = 5080,
    [int]$PuertoApi = 5090,
    [int]$PuertoSiat = 5095,
    [int]$PuertoBd = 5432,
    [string]$EmpresaTienda = 'TECHZONE',
    [string]$SucursalTienda = 'CM',
    [string]$OrigenTienda = 'http://localhost:5173',
    [switch]$SinSimulador
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Join-Path $env:LOCALAPPDATA 'M-INV'
$cred = Join-Path $base 'credenciales-bd-local.txt'
$claves = Join-Path $base 'claves-integracion.txt'
$estadoSimulador = Join-Path $base 'siat-simulador.json'
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

function SaludSimulador {
    try {
        $r = Invoke-RestMethod -Uri ('http://localhost:' + $PuertoSiat + '/control/estado') -TimeoutSec 3
        $texto = 'OK - ' + $r.documents + ' documentos, ' + $r.pointsOfSale + ' puntos de venta'
        if ($r.available) { return ($texto + ' (con internet)') }
        return ($texto + ' (APAGADO: corte de internet simulado)')
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
    $sim = SaludSimulador
    $nube = Salud ('http://localhost:' + $PuertoNube + '/api/v1/health')
    $api = Salud ('http://localhost:' + $PuertoApi + '/health')
    # Write-Host: la funcion devuelve solo el resultado (true/false); los mensajes van a la consola
    if ($sim) { Write-Host ('Simulador del SIN    http://localhost:' + $PuertoSiat + '  ' + $sim) } else { Write-Host ('Simulador del SIN    http://localhost:' + $PuertoSiat + '  NO RESPONDE') }
    if ($nube) { Write-Host ('Servidor en la nube  http://localhost:' + $PuertoNube + '  ' + $nube) } else { Write-Host ('Servidor en la nube  http://localhost:' + $PuertoNube + '  NO RESPONDE') }
    if ($api) { Write-Host ('API Gateway          http://localhost:' + $PuertoApi + '  ' + $api + '  (documentacion: /docs)') } else { Write-Host ('API Gateway          http://localhost:' + $PuertoApi + '  NO RESPONDE') }
    if ($SinSimulador) { return ($nube -and $api) }
    return ($sim -and $nube -and $api)
}

# Arranca un proyecto compilado en segundo plano con su registro; devuelve el id del PowerShell intermedio
function Arrancar([string]$carpeta, [string]$nombre, [int]$puerto, [string]$archivoLog) {
    $dll = Get-ChildItem (Join-Path $root ($carpeta + '\' + $nombre + '\bin\Release')) -Filter ($nombre + '.dll') -Recurse | Select-Object -First 1
    $env:ASPNETCORE_URLS = 'http://localhost:' + $puerto
    $log = Join-Path $base $archivoLog
    # Sin -Redirect*: Start-Process usa ShellExecute y el servidor NO hereda los handles de esta consola (si los
    # heredara, quien ejecuta este script quedaria esperando hasta que el servidor termine). La salida la redirige
    # un PowerShell oculto intermedio.
    $orden = "& dotnet '" + $dll.FullName + "' *> '" + $log + "'"
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -Command "' + $orden + '"') `
        -WorkingDirectory $dll.DirectoryName -WindowStyle Hidden -PassThru
    # Write-Host: la funcion devuelve SOLO el id del proceso (Write-Output lo agregaria al resultado)
    Write-Host ('Iniciado ' + $nombre + ' (proceso ' + $p.Id + ') en http://localhost:' + $puerto)
    return $p.Id
}

function Iniciar {
    $claveServer = Leer $cred 'minv_server'
    $llaves = Leer $claves 'MINV_INTEGRATION_KEYS'
    if (-not $claveServer -or -not $llaves) {
        throw 'Falta la base local V4 (rol minv_server o claves de integracion). Ejecute antes: tools\bd_local.ps1 -Accion recrear'
    }
    $tokenSiat = Leer $claves 'MINV_SIAT_TOKEN'
    Detener
    Write-Output 'Compilando los servidores (Release) ...'
    $proyectos = @('src/3. Presentation/MINV.CloudServer', 'src/3. Presentation/MINV.ApiGateway')
    if (-not $SinSimulador) { $proyectos = @('src/4. Tools/MINV.SiatSimulator') + $proyectos }
    foreach ($proyecto in $proyectos) {
        dotnet build (Join-Path $root $proyecto) -c Release -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw ('No se pudo compilar ' + $proyecto) }
    }
    $lista = @()
    $env:ASPNETCORE_ENVIRONMENT = 'Production'

    # V4.1 - 1) El simulador del SIN, ANTES del servidor en la nube (su trabajo automatico lo llama al arrancar). El
    # estado y el token van por variables de entorno (no en la linea de comandos, que ven otros procesos del equipo).
    if (-not $SinSimulador) {
        $env:Siat__StateFile = $estadoSimulador
        if ($tokenSiat) { $env:Siat__Tokens = $tokenSiat }
        else { Write-Output '[aviso] No hay MINV_SIAT_TOKEN en claves-integracion.txt (datos sin facturacion): el simulador acepta cualquier token de prueba.' }
        $lista += Arrancar 'src\4. Tools' 'MINV.SiatSimulator' $PuertoSiat 'simulador-sin.log'
        Remove-Item Env:\Siat__StateFile, Env:\Siat__Tokens -ErrorAction SilentlyContinue
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 1
            if (SaludSimulador) { break }
        }
    }

    # 2) Servidor en la nube y API Gateway (rol minv_server; claves maestras para descifrar el token del SIN y los webhooks)
    $env:MINV_DB = 'Host=localhost;Port=' + $PuertoBd + ';Database=minv;Username=minv_server;Password=' + $claveServer
    $env:MINV_INTEGRATION_KEYS = $llaves
    $lista += Arrancar 'src\3. Presentation' 'MINV.CloudServer' $PuertoNube 'servidor-nube.log'
    # V6 - La tienda web publica del gateway: empresa, sucursal y origen del catalogo web (Vite) por variables de entorno
    $env:Minv__Storefront__TenantCode = $EmpresaTienda
    $env:Minv__Storefront__BranchCode = $SucursalTienda
    $env:Minv__Storefront__AllowedOrigins__0 = $OrigenTienda
    $lista += Arrancar 'src\3. Presentation' 'MINV.ApiGateway' $PuertoApi 'api-gateway.log'
    Set-Content -Path $pids -Value $lista -Encoding ASCII
    Remove-Item Env:\MINV_DB, Env:\MINV_INTEGRATION_KEYS, Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue
    Remove-Item Env:\Minv__Storefront__TenantCode, Env:\Minv__Storefront__BranchCode, Env:\Minv__Storefront__AllowedOrigins__0 -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        if ((Salud ('http://localhost:' + $PuertoNube + '/api/v1/health')) -and (Salud ('http://localhost:' + $PuertoApi + '/health'))) { break }
    }
    if (-not (Estado)) {
        throw ('Algun servidor no respondio. Revise ' + (Join-Path $base 'servidor-nube.log') + ', api-gateway.log y simulador-sin.log')
    }
    $token = Leer $claves 'MINV_API_KEY'
    Write-Output ''
    Write-Output ('Escritorio: abra M-INV.exe, elija "Nube" y use el servidor http://localhost:' + $PuertoNube)
    Write-Output ('API B2B:    documentacion en http://localhost:' + $PuertoApi + '/docs')
    Write-Output ('Tienda web: http://localhost:' + $PuertoApi + '/storefront/v1/catalog  (empresa ' + $EmpresaTienda + ', sucursal ' + $SucursalTienda + '; sin llave)')
    Write-Output ('            catalogo web (Vite): VITE_API_URL=http://localhost:' + $PuertoApi + '  ->  npm run dev  en src\3. Presentation\MINV.WebCatalog (' + $OrigenTienda + ')')
    if ($token) {
        Write-Output ('            curl.exe -H "Authorization: Bearer <MINV_API_KEY de ' + $claves + '>" http://localhost:' + $PuertoApi + '/v1/catalog?pageSize=5')
    }
    if (-not $SinSimulador) {
        Write-Output ('SIN:        simulador en http://localhost:' + $PuertoSiat + ' (estado: ' + $estadoSimulador + ')')
        Write-Output '            corte de internet simulado: dotnet run --project "src/4. Tools/MINV.Cli" -- siat simulador-apagar  (y simulador-encender)'
    }
}

switch ($Accion) {
    'detener' { Detener }
    'estado' { if (-not (Estado)) { exit 1 } }
    default { Iniciar }
}

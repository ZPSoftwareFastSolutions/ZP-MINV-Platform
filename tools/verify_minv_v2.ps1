<#
.SYNOPSIS
    M-INV V2.1 - Verificacion del libro colaborativo en Excel real (Windows + Excel 2016/365).

.DESCRIPTION
    Abre el libro con las macros deshabilitadas (los Office Scripts se prueban con tests/office-scripts) y comprueba:
      1. Estructura: hojas y visibilidad, calculo automatico, proteccion por capa, 92_SESION sin proteger,
         rangos editables (Permitir editar rangos) y columnas de auditoria ocultas en 10A/10B.
      2. Recalculo completo sin errores de formula.
      3. Bitacoras oficiales: IDs unicos sin coordinacion, Usuario_O365 y Timestamp, CantidadNeta = Cantidad x Factor,
         signo segun el fragmento (10A: bodega, 10B: salidas).
      4. Instantanea: 15_STOCK = suma independiente de las bitacoras hasta stkActualizado; semaforo, salidas de
         30 dias, cobertura y ranking; alertas; pedido sugerido (18_PEDIDO); movimientos posteriores al calculo.
         Toma fisica (13_CONTEO), consulta por usuario (17_CONSULTA), actividad (14_ACTIVIDAD) y portada de
         Gerencia, cada una contra un calculo independiente y con pruebas en vivo (escribir y recalcular).
      5. Captura: filas asignadas por usuario, disponible exacto, validacion y POKA-YOKE (rojo sangre, blanco
         tachado) en la captura y en los rechazos de la bitacora.
      6. Interfaz web: solo graficos como objetos (sin formas con vinculos) y navegacion por celdas que funciona.
      7. (Opcional) PNG de cada hoja visible y guardado recalculado.
    Script ASCII a proposito (PowerShell 5.1 lee los .ps1 sin BOM como ANSI); los textos con tildes van con [char].

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\verify_minv_v2.ps1 -Path src\M-INV_V2_Colaborativo.xlsx -PreviewDir build\preview-v2 -Save
#>
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$Password = 'minv-dev',
    [string]$PreviewDir = '',
    [switch]$Save
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Path = (Resolve-Path $Path).Path
$script:fails = New-Object System.Collections.Generic.List[string]
function Fail([string]$m) { $script:fails.Add($m); Write-Output ("  [FALLA] " + $m) }
function Ok([string]$m) { Write-Output ("  [OK]    " + $m) }
function Info([string]$m) { Write-Output ("          " + $m) }
function Near($a, $b) { return [math]::Abs([double]$a - [double]$b) -lt 1e-6 }

$CHECK = [string][char]0x2714; $CROSS = [string][char]0x2716; $WARN = [string][char]0x26A0
$oA = [string][char]0xF3; $iA = [string][char]0xED
$VALID = 'Validaci' + $oA + 'n'; $REGISTRO = 'Registr' + $oA; $CAT = 'Categor' + $iA + 'a'
$ALERTAS = @('INCONSISTENTE', 'AGOTADO', ('CR' + [char]0xCD + 'TICO'), 'BAJO', 'SOBRESTOCK')
$ROJO_SANGRE = 138 + 3 * 256 + 3 * 65536     # #8A0303 en formato BGR de Excel
$visName = @{ -1 = 'visible'; 0 = 'oculta'; 2 = 'muy oculta' }
$UP = [string][char]0x25B2; $DOWN = [string][char]0x25BC; $DOT = [string][char]0x25CF; $MID = [string][char]0xB7
$REPONER = @('AGOTADO', ('CR' + [char]0xCD + 'TICO'), 'BAJO')
$ESPERADAS = @('00_PORTADA_BODEGA', '00_PORTADA_VENTAS', '00_PORTADA_GERENCIA', '01_CONFIG', '02_USUARIOS',
    '03_CATEGORIAS', '04_PROVEEDORES', '05_PRODUCTOS', '06_UNIDADES', '10A_ENTRADAS', '10B_SALIDAS', '13_CONTEO',
    '14_ACTIVIDAD', '15_STOCK', '16_ALERTAS', '17_CONSULTA', '18_PEDIDO', '90_LISTAS', '91_KPIS', '92_SESION', '99_AYUDA')

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$xl.AutomationSecurity = 3
try {
    Write-Output ("M-INV V2.1 verificacion :: " + (Split-Path $Path -Leaf) + "  (Excel " + $xl.Version + " build " + $xl.Build + ")")
    $wb = $xl.Workbooks.Open($Path, 0, $true)
    $xl.CalculateFull()
    $Nm = { param($nombre) $wb.Names.Item($nombre).RefersToRange }
    $demo = ([string](& $Nm 'cfgEmpresa').Value2) -ne 'NOMBRE DE SU EMPRESA'

    $formasNoGrafico = @()
    foreach ($ws in $wb.Worksheets) { foreach ($forma in $ws.Shapes) { if ($forma.Type -ne 3) { $formasNoGrafico += ($ws.Name + ":" + $forma.Name) } } }
    Write-Output "== 1. Estructura, calculo y proteccion"
    $nombres = @(); foreach ($ws in $wb.Worksheets) { $nombres += $ws.Name }
    if (($nombres -join '|') -eq ($ESPERADAS -join '|')) { Ok ("21 hojas en el orden esperado") } else { Fail ("Hojas: " + ($nombres -join ', ')) }
    if (-not $demo) {
        if ($wb.ProtectStructure) { Ok "Release: estructura del libro bloqueada y capas de motor muy ocultas" } else { Fail "Release: estructura sin bloquear" }
    }
    if ($xl.Calculation -eq -4105) { Ok "Calculo automatico (formulas livianas; la instantanea de stock es a demanda)" } else { Fail ("Modo de calculo: " + $xl.Calculation) }
    foreach ($ws in $wb.Worksheets) {
        Info ("{0,-18} {1,-11} protegida={2}" -f $ws.Name, $visName[[int]$ws.Visible], $ws.ProtectContents)
        $debe = $ws.Name -ne '92_SESION'
        if ([bool]$ws.ProtectContents -ne $debe) { Fail ("Proteccion inesperada en " + $ws.Name) }
    }
    foreach ($h in '10A_ENTRADAS', '10B_SALIDAS') {
        $ws = $wb.Worksheets.Item($h)
        $n = $ws.Protection.AllowEditRanges.Count
        $ocultas = $ws.Columns.Item(14).Hidden -and $ws.Columns.Item(15).Hidden -and $ws.Columns.Item(16).Hidden
        if ($n -ge 1 -and $ocultas) { Ok ("{0}: rango de captura editable ({1}) y columnas tecnicas N:P ocultas" -f $h, $ws.Protection.AllowEditRanges.Item(1).Title) }
        else { Fail ("{0}: rangos editables={1} columnas ocultas={2}" -f $h, $n, $ocultas) }
    }
    foreach ($par in @(@('13_CONTEO', 3), @('17_CONSULTA', 1))) {
        $n = $wb.Worksheets.Item($par[0]).Protection.AllowEditRanges.Count
        if ($n -ge $par[1]) { Ok ("{0}: {1} rango(s) editable(s) para la entrada colaborativa" -f $par[0], $n) }
        else { Fail ("{0}: rangos editables={1}, se esperaban {2}" -f $par[0], $n, $par[1]) }
    }

    Write-Output "== 2. Errores de formula (recalculo completo)"
    $totalF = 0
    foreach ($ws in $wb.Worksheets) {
        if ($ws.ProtectContents) { try { $ws.Unprotect($Password) } catch { Fail ("No se pudo desproteger " + $ws.Name + " con la contrasena indicada") } }
        try { $totalF += $ws.UsedRange.SpecialCells(-4123).Count } catch {}
        $err = $null
        try { $err = $ws.UsedRange.SpecialCells(-4123, 16) } catch {}
        if ($err) { Fail ("{0}: {1} celda(s) con error, p.ej. {2}" -f $ws.Name, $err.Count, $err.Cells.Item(1).Address(0, 0)) }
    }
    Ok ("{0:N0} formulas evaluadas sin errores" -f $totalF)

    Write-Output "== 3. Bitacoras oficiales (valores escritos por los scripts)"
    $sumas = @{}; $sumasCorte = @{}; $ultimo = @{}; $ventas30 = @{}; $porUsuario = @{}
    $corte = [double](& $Nm 'stkActualizado').Value2
    $mes = (Get-Date -Day 1).Date.ToOADate()
    $posteriores = 0; $totalRegistros = 0
    foreach ($par in @(@('10A_ENTRADAS', 'tblEntradas', 'E'), @('10B_SALIDAS', 'tblSalidas', 'S'))) {
        $lo = $wb.Worksheets.Item($par[0]).ListObjects.Item($par[1])
        $v = $lo.DataBodyRange.Value2
        $c = @{}; foreach ($n in 'ID', 'Tipo', 'Fecha', 'Cantidad', 'CantidadNeta', 'FactorStock', 'Estado', 'Usuario_O365', 'SKU', 'Timestamp', $REGISTRO) { $c[$n] = $lo.ListColumns.Item($n).Index }
        $ids = @{}; $malos = 0; $n = 0
        for ($i = 1; $i -le $v.GetLength(0); $i++) {
            $id = [string]$v[$i, $c['ID']]
            if ($id -eq '') { continue }
            $n++
            $tipo = [string]$v[$i, $c['Tipo']]; $f = [double]$v[$i, $c['FactorStock']]
            $esperado = if ($par[2] -eq 'S') { -1 } elseif ($tipo -eq 'AJUSTE (-)') { -1 } else { 1 }
            $ok = ($id -match ('^' + $par[2] + '-\d{8}-\d{6}-[0-9A-F]{4}$')) -and -not $ids.ContainsKey($id) -and
                  ([string]$v[$i, $c['Usuario_O365']]).Contains('@') -and ($v[$i, $c['Timestamp']] -is [double]) -and
                  ($f -eq $esperado) -and (Near $v[$i, $c['CantidadNeta']] ([double]$v[$i, $c['Cantidad']] * $f)) -and
                  ([string]$v[$i, $c[$REGISTRO]]) -ne ''
            if (($par[2] -eq 'S' -and $tipo -ne 'SALIDA') -or ($par[2] -eq 'E' -and $tipo -eq 'SALIDA')) { $ok = $false }
            if (-not $ok) { $malos++; if ($malos -le 3) { Fail ("{0} fila {1}: registro con datos de auditoria o signo incorrectos ({2})" -f $par[1], $i, $id) } }
            $ids[$id] = 1
            if (([string]$v[$i, $c['Estado']]).StartsWith($CHECK)) {
                $sku = [string]$v[$i, $c['SKU']]; $neta = [double]$v[$i, $c['CantidadNeta']]
                $sumas[$sku] = [double]$sumas[$sku] + $neta
                $ts = [double]$v[$i, $c['Timestamp']]
                $fecha = [double]$v[$i, $c['Fecha']]
                if ($ts -le $corte) {
                    $sumasCorte[$sku] = [double]$sumasCorte[$sku] + $neta
                    if ($tipo -eq 'SALIDA' -and $fecha -ge ([math]::Floor($corte) - 29)) { $ventas30[$sku] = [double]$ventas30[$sku] - $neta }
                } else { $posteriores++ }
                if ($fecha -ge $mes) { $correoU = ([string]$v[$i, $c['Usuario_O365']]).ToLower(); $porUsuario[$correoU] = [int]$porUsuario[$correoU] + 1 }
            }
        }
        $totalRegistros += $n
        if ($malos -eq 0) { Ok ("{0}: {1} registros con ID unico ({2}-...), correo, Timestamp y signo correctos" -f $par[1], $n, $par[2]) }
    }

    Write-Output "== 4. Instantanea de lectura (15_STOCK / 16_ALERTAS)"
    $ls = $wb.Worksheets.Item('15_STOCK').ListObjects.Item('tblStock')
    $sv = $ls.DataBodyRange.Value2
    $S = @{}; foreach ($n in 'SKU', 'Producto', 'StockActual', 'StockMin', 'StockMax', 'Estado', 'Activo', 'CostoUnitario', 'Salidas30d', 'CoberturaDias', 'RankSalidas30d') { $S[$n] = $ls.ListColumns.Item($n).Index }
    $margen = [double](& $Nm 'cfgMargenAlerta').Value2
    $prods = 0; $difs = 0; $nAlertasEsp = 0; $rankList = @(); $pedidoEsp = @{}
    for ($i = 1; $i -le $sv.GetLength(0); $i++) {
        $sku = [string]$sv[$i, $S['SKU']]
        if ($sku -eq '') { continue }
        $prods++
        $st = [double]$sv[$i, $S['StockActual']]; $mn = [double]$sv[$i, $S['StockMin']]; $mx = [double]$sv[$i, $S['StockMax']]
        if (-not (Near $st ([double]$sumasCorte[$sku]))) { $difs++; if ($difs -le 3) { Fail ("{0}: instantanea {1} vs bitacoras {2}" -f $sku, $st, [double]$sumasCorte[$sku]) } }
        $act = [string]$sv[$i, $S['Activo']] -ne 'NO'
        $e = if (-not $act) { 'INACTIVO' } elseif ($st -lt 0) { 'INCONSISTENTE' } elseif ($st -eq 0) { 'AGOTADO' } elseif ($st -le $mn) { $ALERTAS[2] } elseif ($st -le $mn * (1 + $margen)) { 'BAJO' } elseif ($mx -gt 0 -and $st -gt $mx) { 'SOBRESTOCK' } else { ([string][char]0xD3) + 'PTIMO' }
        if ([string]$sv[$i, $S['Estado']] -ne $e) { $difs++; Fail ("{0}: semaforo {1}, se esperaba {2}" -f $sku, $sv[$i, $S['Estado']], $e) }
        if ($act -and $ALERTAS -contains $e) { $nAlertasEsp++ }
        $v30 = [math]::Round([double]$ventas30[$sku], 6)
        $cob = $sv[$i, $S['CoberturaDias']]; $rk = $sv[$i, $S['RankSalidas30d']]
        if (-not (Near $sv[$i, $S['Salidas30d']] $v30)) { $difs++; if ($difs -le 3) { Fail ("{0}: salidas 30 d {1}, se esperaban {2}" -f $sku, $sv[$i, $S['Salidas30d']], $v30) } }
        if ($v30 -gt 0) {
            $cobEsp = [math]::Floor([math]::Max(0.0, $st) / ($v30 / 30))
            if (-not (Near $cob $cobEsp)) { $difs++; if ($difs -le 3) { Fail ("{0}: cobertura {1}, se esperaba {2}" -f $sku, $cob, $cobEsp) } }
            $rankList += , @($v30, $i, [double]$rk)
        } elseif ([string]$cob -ne '' -or [string]$rk -ne '') { $difs++; Fail ("{0}: sin ventas en 30 dias pero con cobertura/ranking" -f $sku) }
        if ($act -and $REPONER -contains $e) {
            $tope = if ($mx -gt 0) { $mx } else { 2 * $mn }
            $ap = [math]::Round($tope - [math]::Max(0.0, $st), 6)
            if ($ap -gt 0) { $pedidoEsp[$sku] = $ap }
        }
    }
    $orden = @($rankList | Sort-Object @{ Expression = { $_[0] }; Descending = $true }, @{ Expression = { $_[1] }; Ascending = $true })
    $pos = 0
    foreach ($x in $orden) { $pos++; if ($x[2] -ne $pos) { $difs++; if ($difs -le 3) { Fail ("Ranking de ventas: posicion {0} con valor {1}" -f $pos, $x[2]) } } }
    if ($demo) {
        if ($difs -eq 0) { Ok ("{0} productos: stock, semaforo, salidas 30 d, cobertura y ranking ({1} con ventas) = recalculo independiente hasta el corte" -f $prods, $orden.Count) }
        $la = $wb.Worksheets.Item('16_ALERTAS').ListObjects.Item('tblAlertas').DataBodyRange.Value2
        $nAl = 0; for ($i = 1; $i -le $la.GetLength(0); $i++) { if ([string]$la[$i, 3] -ne '') { $nAl++ } }
        if ($nAl -eq $nAlertasEsp) { Ok ("16_ALERTAS: {0} productos priorizados (activos en alerta o sobrestock)" -f $nAl) } else { Fail ("16_ALERTAS lista {0}, se esperaban {1}" -f $nAl, $nAlertasEsp) }
        $kn = [int](& $Nm 'kpiNuevosDesdeCalculo').Value2
        if ($kn -eq $posteriores -and $kn -gt 0) { Ok ("Frescura: {0} movimiento(s) posteriores al calculo -> '{1}'" -f $kn, (& $Nm 'txtFrescura').Text) } else { Fail ("kpiNuevosDesdeCalculo={0}, se esperaban {1}" -f $kn, $posteriores) }
    } else {
        if ($prods -eq 0 -and $totalRegistros -eq 0) { Ok ("Release limpio: sin catalogo, sin registros; frescura: '{0}'" -f (& $Nm 'txtFrescura').Text) } else { Fail ("Release con datos: {0} productos, {1} registros" -f $prods, $totalRegistros) }
    }

    Write-Output "== 4b. Pedido sugerido (18_PEDIDO)"
    $lp = $wb.Worksheets.Item('18_PEDIDO').ListObjects.Item('tblPedido')
    $pv = $lp.DataBodyRange.Value2
    $PC = @{}; foreach ($n in 'Proveedor', 'SKU', 'APedir', 'CostoUnitario', 'Subtotal') { $PC[$n] = $lp.ListColumns.Item($n).Index }
    $lineas = 0; $sumaSub = 0.0; $malas = 0; $provVistos = @{}; $provPrev = $null; $agrupado = $true
    for ($i = 1; $i -le $pv.GetLength(0); $i++) {
        $sku = [string]$pv[$i, $PC['SKU']]
        if ($sku -eq '') { continue }
        $lineas++
        $prov = [string]$pv[$i, $PC['Proveedor']]
        if ($prov -ne $provPrev) { if ($provVistos.ContainsKey($prov)) { $agrupado = $false }; $provVistos[$prov] = 1; $provPrev = $prov }
        $ap = [double]$pv[$i, $PC['APedir']]; $sub = [double]$pv[$i, $PC['Subtotal']]
        $sumaSub += $sub
        if (-not $pedidoEsp.ContainsKey($sku) -or -not (Near $ap $pedidoEsp[$sku]) -or -not (Near $sub ($ap * [double]$pv[$i, $PC['CostoUnitario']]))) {
            $malas++; if ($malas -le 3) { Fail ("18_PEDIDO {0}: a pedir {1} (esperado {2}), subtotal {3}" -f $sku, $ap, $pedidoEsp[$sku], $sub) }
        }
    }
    if ($lineas -ne $pedidoEsp.Count) { Fail ("18_PEDIDO: {0} lineas, se esperaban {1} (activos agotados, criticos y bajos)" -f $lineas, $pedidoEsp.Count) }
    elseif ($malas -eq 0) { Ok ("18_PEDIDO: {0} lineas = activos agotados/criticos/bajos hasta el maximo, subtotal = cantidad x costo" -f $lineas) }
    if ($agrupado) { Ok ("18_PEDIDO: agrupado por proveedor ({0} proveedor(es))" -f $provVistos.Count) } else { Fail "18_PEDIDO: las lineas de un proveedor no estan juntas" }
    $kt = [double](& $Nm 'kpiPedidoTotal').Value2
    if ([math]::Abs($kt - $sumaSub) -lt 0.01) { Ok ("kpiPedidoTotal = suma de subtotales ({0:N0})" -f $kt) } else { Fail ("kpiPedidoTotal {0} vs suma {1}" -f $kt, $sumaSub) }

    Write-Output "== 4c. Toma fisica colaborativa (13_CONTEO)"
    $lc = $wb.Worksheets.Item('13_CONTEO').ListObjects.Item('tblConteo')
    $cv = $lc.DataBodyRange.Value2
    $KC = @{}; foreach ($n in 'SKU', 'Unidad', 'Sistema', 'Conteo', 'Diferencia', 'Resultado', 'AjusteSugerido') { $KC[$n] = $lc.ListColumns.Item($n).Index }
    $contados = 0; $malos = 0; $filaUnd = 0; $filas = 0
    for ($i = 1; $i -le $cv.GetLength(0); $i++) {
        $sku = [string]$cv[$i, $KC['SKU']]
        if ($sku -eq '') { continue }
        $filas++
        if (-not (Near $cv[$i, $KC['Sistema']] ([double]$sumasCorte[$sku]))) { $malos++; if ($malos -le 3) { Fail ("13_CONTEO {0}: sistema {1} vs instantanea {2}" -f $sku, $cv[$i, $KC['Sistema']], [double]$sumasCorte[$sku]) } }
        if ([string]$cv[$i, $KC['Conteo']] -eq '') {
            if ($filaUnd -eq 0 -and [string]$cv[$i, $KC['Unidad']] -eq 'UND' -and [double]$sumasCorte[$sku] -gt 0) { $filaUnd = $i }
            continue
        }
        $contados++
        $dif = [double]$cv[$i, $KC['Conteo']] - [double]$cv[$i, $KC['Sistema']]
        $res = [string]$cv[$i, $KC['Resultado']]; $aj = [string]$cv[$i, $KC['AjusteSugerido']]
        $simb = if ($dif -gt 0) { $UP } elseif ($dif -lt 0) { $DOWN } else { $CHECK }
        $tipoAj = if ($dif -gt 0) { 'AJUSTE (+)' } else { 'AJUSTE (-)' }
        if ((Near $cv[$i, $KC['Diferencia']] $dif) -and $res.StartsWith($simb) -and ($dif -eq 0 -or $aj.StartsWith($tipoAj))) {
            Ok ("13_CONTEO {0}: contado {1} vs sistema {2} -> '{3}' / '{4}'" -f $sku, $cv[$i, $KC['Conteo']], $cv[$i, $KC['Sistema']], $res, $aj)
        } else { $malos++; Fail ("13_CONTEO {0}: diferencia {1}, resultado '{2}', ajuste '{3}'" -f $sku, $cv[$i, $KC['Diferencia']], $res, $aj) }
    }
    $kpiCont = [int](& $Nm 'kpiConteoContados').Value2
    if ($kpiCont -ne $contados) { Fail ("kpiConteoContados={0}, se esperaban {1}" -f $kpiCont, $contados) }
    if ($demo -and $contados -ne 2) { Fail ("13_CONTEO: se esperaban 2 conteos de ejemplo y hay {0}" -f $contados) }
    if (-not $demo -and ($contados -ne 0 -or $filas -ne 0)) { Fail "Release: 13_CONTEO debia estar vacio" }
    if ($malos -eq 0) { Ok ("13_CONTEO: {0} productos reflejados (sistema = instantanea), {1} contados" -f $filas, $contados) }
    if ($filaUnd -gt 0) {
        $celda = $lc.DataBodyRange.Cells.Item($filaUnd, $KC['Conteo'])
        $celda.Formula = '1.5'; $xl.Calculate()
        $r1 = [string]$lc.DataBodyRange.Cells.Item($filaUnd, $KC['Resultado']).Value2
        $celda.Formula = [string]$cv[$filaUnd, $KC['Sistema']]; $xl.Calculate()
        $r2 = [string]$lc.DataBodyRange.Cells.Item($filaUnd, $KC['Resultado']).Value2
        $celda.ClearContents(); $xl.Calculate()
        if ($r1.StartsWith($CROSS) -and ($r2.StartsWith($CHECK) -or $r2.StartsWith($DOT))) { Ok ("En vivo: 1,5 en UND -> '{0}'; conteo = sistema -> '{1}'" -f $r1, $r2) }
        else { Fail ("13_CONTEO en vivo: '{0}' / '{1}'" -f $r1, $r2) }
    }

    Write-Output "== 4d. Consulta de producto por usuario (17_CONSULTA)"
    $lq = $wb.Worksheets.Item('17_CONSULTA').ListObjects.Item('tblConsulta')
    $qv = $lq.DataBodyRange.Value2
    $QC = @{}; foreach ($n in 'Usuario', 'Producto', 'Disponible', 'Estado', 'UltimoMovimiento', 'Correo', 'SKU') { $QC[$n] = $lq.ListColumns.Item($n).Index }
    $asig = 0; $consultas = 0; $malos = 0; $filaLibre = 0
    for ($i = 1; $i -le $qv.GetLength(0); $i++) {
        $asignada = [string]$qv[$i, $QC['Correo']] -ne ''
        if ($asignada) { $asig++ }
        $sku = [string]$qv[$i, $QC['SKU']]
        if ($sku -eq '') { if ($asignada -and $filaLibre -eq 0) { $filaLibre = $i }; continue }
        $consultas++
        $disp = $qv[$i, $QC['Disponible']]
        if ((Near $disp ([double]$sumas[$sku])) -and [string]$qv[$i, $QC['UltimoMovimiento']] -ne '' -and [string]$qv[$i, $QC['Estado']] -ne '') {
            Ok ("17_CONSULTA ({0}): {1} -> disponible exacto {2} / {3}" -f $qv[$i, $QC['Usuario']], $sku, $disp, $qv[$i, $QC['Estado']])
        } else { $malos++; Fail ("17_CONSULTA fila {0}: disponible {1} vs bitacoras {2}; ultimo '{3}'" -f $i, $disp, [double]$sumas[$sku], $qv[$i, $QC['UltimoMovimiento']]) }
    }
    Info ("17_CONSULTA: {0} filas asignadas a usuarios, {1} consultas en curso" -f $asig, $consultas)
    if ($demo -and ($asig -ne 5 -or $consultas -ne 3)) { Fail ("17_CONSULTA: se esperaban 5 filas asignadas y 3 consultas") }
    if ($demo -and $filaLibre -gt 0) {
        $sku0 = [string]$sv[1, $S['SKU']]
        $celda = $lq.DataBodyRange.Cells.Item($filaLibre, $QC['Producto'])
        $celda.Value2 = $sku0 + ' ' + $MID + ' ' + [string]$sv[1, $S['Producto']]; $xl.Calculate()
        $d0 = $lq.DataBodyRange.Cells.Item($filaLibre, $QC['Disponible']).Value2
        $celda.ClearContents(); $xl.Calculate()
        if (Near $d0 ([double]$sumas[$sku0])) { Ok ("En vivo: al elegir {0} en una fila libre el disponible exacto es {1}" -f $sku0, $d0) }
        else { Fail ("17_CONSULTA en vivo: {0} -> {1}, se esperaba {2}" -f $sku0, $d0, [double]$sumas[$sku0]) }
    }

    Write-Output "== 4e. Registro de actividad (14_ACTIVIDAD)"
    $lact = $wb.Worksheets.Item('14_ACTIVIDAD').ListObjects.Item('tblActividad')
    $av = $lact.DataBodyRange.Value2
    $AC = @{}; foreach ($n in 'ID', 'Timestamp', 'Usuario_O365', 'Script', 'Resultado') { $AC[$n] = $lact.ListColumns.Item($n).Index }
    $nAct = 0; $malos = 0; $ids = @{}
    for ($i = 1; $i -le $av.GetLength(0); $i++) {
        $id = [string]$av[$i, $AC['ID']]
        if ($id -eq '') { continue }
        $nAct++
        $ok = ($id -match '^A-\d{8}-\d{6}-[0-9A-F]{4}$') -and -not $ids.ContainsKey($id) -and
              ([string]$av[$i, $AC['Usuario_O365']]).Contains('@') -and ($av[$i, $AC['Timestamp']] -is [double]) -and
              ([string]$av[$i, $AC['Script']]) -ne '' -and ([string]$av[$i, $AC['Resultado']]) -ne ''
        $ids[$id] = 1
        if (-not $ok) { $malos++; if ($malos -le 3) { Fail ("tblActividad fila {0}: registro incompleto ({1})" -f $i, $id) } }
    }
    $ke = [int](& $Nm 'kpiEjecuciones').Value2
    if ($demo) {
        if ($nAct -gt 0 -and $malos -eq 0 -and $ke -eq $nAct) { Ok ("14_ACTIVIDAD: {0} ejecuciones auditadas (ID unico, correo, hora, script, resultado); '{1}'" -f $nAct, (& $Nm 'txtUltimaEjecucion').Text) }
        else { Fail ("14_ACTIVIDAD: {0} filas, kpiEjecuciones={1}" -f $nAct, $ke) }
    } elseif ($nAct -eq 0 -and $ke -eq 0) { Ok ("Release: 14_ACTIVIDAD vacia; '{0}'" -f (& $Nm 'txtUltimaEjecucion').Text) }
    else { Fail ("Release con actividad: {0}" -f $nAct) }

    Write-Output "== 4f. Portada de Gerencia"
    $wsG = $wb.Worksheets.Item('00_PORTADA_GERENCIA')
    $nGraf = $wsG.ChartObjects().Count
    if ($nGraf -eq 2) { Ok "Gerencia: 2 graficos nativos (despachos por mes y valor por categoria)" } else { Fail ("Gerencia: {0} graficos" -f $nGraf) }
    $top = [string]$wsG.Range('C41').Text
    if ($demo) {
        $skuTop = [string]$sv[$orden[0][1], $S['SKU']]
        if ($top.StartsWith($skuTop)) { Ok ("Gerencia: el mas vendido en 30 dias es '{0}' (ranking de la instantanea)" -f $top) } else { Fail ("Gerencia top 1 '{0}', se esperaba {1}" -f $top, $skuTop) }
    } elseif ($top -eq '') { Ok "Release: top de ventas vacio" } else { Fail ("Release: top de ventas con datos '{0}'" -f $top) }
    $lu = $wb.Worksheets.Item('02_USUARIOS').ListObjects.Item('tblUsuarios')
    $uv = $lu.DataBodyRange.Value2; $cCorreo = $lu.ListColumns.Item('Correo').Index
    $malos = 0; $revisados = 0
    for ($u = 1; $u -le 10; $u++) {
        $correo = if ($u -le $uv.GetLength(0)) { ([string]$uv[$u, $cCorreo]).ToLower() } else { '' }
        $cel = $wsG.Range('K' + (40 + $u)).Value2
        if ($correo -eq '') { if ([string]$cel -ne '') { $malos++; Fail ("Gerencia fila {0}: registros sin usuario" -f (40 + $u)) }; continue }
        $revisados++
        if ([int]$cel -ne [int]$porUsuario[$correo]) { $malos++; Fail ("Gerencia {0}: {1} registros en el mes, se esperaban {2}" -f $correo, $cel, [int]$porUsuario[$correo]) }
    }
    if ($malos -eq 0) { Ok ("Gerencia: registros del mes de {0} usuario(s) = conteo independiente de las bitacoras" -f $revisados) }
    foreach ($par in @(@('E12', '16_ALERTAS'), @('H12', '18_PEDIDO'))) {
        $f = $wsG.Range($par[0]).Formula
        if ($f -like ("*HYPERLINK(*" + $par[1] + "*")) { Ok ("Gerencia: boton dinamico {0} -> {1}" -f $par[0], $par[1]) } else { Fail ("Gerencia {0}: {1}" -f $par[0], $f) }
    }

    Write-Output "== 5. Captura por usuario y poka-yoke"
    foreach ($par in @(@('10A_ENTRADAS', 'tblCapturaEntradas'), @('10B_SALIDAS', 'tblCapturaSalidas'))) {
        $ws = $wb.Worksheets.Item($par[0]); $lo = $ws.ListObjects.Item($par[1])
        $v = $lo.DataBodyRange.Value2
        $c = @{}; foreach ($n in 'Usuario', 'Correo', 'SKU', 'Cantidad', 'Disponible', $VALID) { $c[$n] = $lo.ListColumns.Item($n).Index }
        $asignadas = 0; $revisadas = 0
        for ($i = 1; $i -le $v.GetLength(0); $i++) {
            if ([string]$v[$i, $c['Correo']] -ne '') { $asignadas++ }
            $sku = [string]$v[$i, $c['SKU']]
            if ($sku -eq '') { continue }
            $revisadas++
            if (-not (Near $v[$i, $c['Disponible']] ([double]$sumas[$sku]))) { Fail ("{0} fila {1}: disponible {2} vs bitacoras {3}" -f $par[1], $i, $v[$i, $c['Disponible']], [double]$sumas[$sku]) }
            $celda = $lo.DataBodyRange.Cells.Item($i, $c['Cantidad'])
            $rojo = ($celda.DisplayFormat.Interior.Color -eq $ROJO_SANGRE) -and [bool]$celda.DisplayFormat.Font.Strikethrough -and ($celda.DisplayFormat.Font.Color -eq 16777215)
            $insuf = ([double]$v[$i, $c['Disponible']] - [double]$v[$i, $c['Cantidad']]) -lt 0 -and $par[0] -eq '10B_SALIDAS'
            $val = [string]$v[$i, $c[$VALID]]
            if ($insuf) {
                if ($rojo -and $val.StartsWith($CROSS)) { Ok ("{0} ({1}): salida mayor que el disponible -> rojo sangre, blanco tachado y '{2}'" -f $par[0], $v[$i, $c['Usuario']], $val) }
                else { Fail ("{0} fila {1}: poka-yoke no aplicado (rojo={2}, validacion='{3}')" -f $par[1], $i, $rojo, $val) }
            } elseif ($rojo -or -not $val.StartsWith($CHECK)) { Fail ("{0} fila {1}: validacion inesperada '{2}' (rojo={3})" -f $par[1], $i, $val, $rojo) }
            else { Ok ("{0} ({1}): '{2}'" -f $par[0], $v[$i, $c['Usuario']], $val) }
        }
        Info ("{0}: {1} filas asignadas a usuarios, {2} con datos pendientes" -f $par[1], $asignadas, $revisadas)
        if ($demo -and $asignadas -ne 3) { Fail ($par[1] + ": se esperaban 3 filas asignadas (ADMIN + 2 del rol)") }
    }
    if ($demo) {
        # Simula un intento de salida excesiva y un rechazo por concurrencia en la bitacora (sin guardar)
        $ws = $wb.Worksheets.Item('10B_SALIDAS'); $lo = $ws.ListObjects.Item('tblCapturaSalidas')
        $fila = 0; $v = $lo.DataBodyRange.Value2; $cq = $lo.ListColumns.Item('Cantidad').Index
        for ($i = 1; $i -le $v.GetLength(0); $i++) { if (([string]$v[$i, $lo.ListColumns.Item($VALID).Index]).StartsWith($CHECK)) { $fila = $i; break } }
        $celda = $lo.DataBodyRange.Cells.Item($fila, $cq); $orig = $celda.Formula
        $celda.Formula = '100000'; $xl.Calculate()
        $rojo = ($celda.DisplayFormat.Interior.Color -eq $ROJO_SANGRE) -and [bool]$celda.DisplayFormat.Font.Strikethrough
        $celda.Formula = $orig; $xl.Calculate()
        if ($rojo) { Ok "Al escribir una cantidad mayor que el disponible la celda se tine de rojo sangre con texto tachado" } else { Fail "El poka-yoke no reacciono a una cantidad excesiva" }
        $led = $ws.ListObjects.Item('tblSalidas'); $n = $led.ListRows.Count
        $est = $led.DataBodyRange.Cells.Item($n, $led.ListColumns.Item('Estado').Index); $origE = $est.Formula
        $est.Formula = $CROSS + ' Rechazado: prueba'; $xl.Calculate()
        $cc = $led.DataBodyRange.Cells.Item($n, $led.ListColumns.Item('Cantidad').Index)
        $rojo = ($cc.DisplayFormat.Interior.Color -eq $ROJO_SANGRE) -and [bool]$cc.DisplayFormat.Font.Strikethrough
        $est.Formula = $origE
        if ($rojo) { Ok "Bitacora 10B: un registro rechazado se muestra en rojo sangre con texto tachado" } else { Fail "Bitacora 10B: el rechazo no se resalta" }
    }

    Write-Output "== 6. Interfaz para Excel en la web"
    if ($formasNoGrafico.Count -eq 0) { Ok "Sin formas con vinculos: botones y navegacion son celdas (graficos nativos permitidos)" }
    else { Fail ("Formas que no son graficos (la V2 usa solo celdas): " + ($formasNoGrafico -join ", ")) }
    $seen = @{}; $malos = 0
    foreach ($ws in $wb.Worksheets) {
        if ($ws.Visible -ne -1) { continue }
        foreach ($h in $ws.Hyperlinks) {
            $sub = $h.SubAddress
            if (-not $sub -or $seen.ContainsKey($sub)) { continue }
            try { $ws.Activate(); $h.Follow(); $seen[$sub] = $xl.ActiveSheet.Name + '!' + $xl.ActiveCell.Address(0, 0) }
            catch { $malos++; Fail ("Vinculo '{0}' en {1} no navega: {2}" -f $sub, $ws.Name, $_.Exception.Message) }
        }
    }
    if ($malos -eq 0) { Ok ("{0} destinos de navegacion por celdas verificados: {1}" -f $seen.Count, (($seen.Values | Sort-Object -Unique) -join ', ')) }
    $f = $wb.Worksheets.Item('00_PORTADA_BODEGA').Range('K12').Formula
    if ($f -like "*HYPERLINK(*16_ALERTAS*") { Ok "Boton dinamico de alertas: HIPERVINCULO a 16_ALERTAS" } else { Fail ("Boton de alertas: " + $f) }

    if ($PreviewDir) {
        Write-Output "== 7. Vistas previas"
        New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
        $PreviewDir = (Resolve-Path $PreviewDir).Path
        $stem = [IO.Path]::GetFileNameWithoutExtension($Path)
        $rows = @{ '00_PORTADA_BODEGA' = 44; '00_PORTADA_VENTAS' = 44; '00_PORTADA_GERENCIA' = 52; '99_AYUDA' = 72;
            '10A_ENTRADAS' = 40; '10B_SALIDAS' = 40; '13_CONTEO' = 30; '14_ACTIVIDAD' = 30; '17_CONSULTA' = 30; '18_PEDIDO' = 30 }
        foreach ($sh in $wb.Worksheets) {
            if ($sh.Visible -ne -1) { continue }
            $sh.Activate()
            $lastRow = 36; if ($rows.ContainsKey($sh.Name)) { $lastRow = $rows[$sh.Name] }
            $lastCol = [math]::Min($sh.UsedRange.Column + $sh.UsedRange.Columns.Count, 18)
            $rng = $sh.Range($sh.Cells.Item(1, 1), $sh.Cells.Item($lastRow, $lastCol))
            $file = Join-Path $PreviewDir ("{0}__{1}.png" -f $stem, $sh.Name)
            $done = $false
            for ($try = 1; $try -le 4 -and -not $done; $try++) {
                try {
                    [void]$rng.CopyPicture(1, 2)
                    Start-Sleep -Milliseconds (250 * $try)
                    $co = $sh.ChartObjects().Add(0, 0, $rng.Width, $rng.Height)
                    [void]$co.Activate()
                    $co.Chart.Paste()
                    if ($co.Chart.Shapes.Count -gt 0) { [void]$co.Chart.Export($file); $done = $true }
                    $co.Delete()
                } catch { Start-Sleep -Milliseconds 400 }
            }
            if ($done) { Ok ("PNG " + $file) } else { Fail ("No se pudo exportar la vista previa de " + $sh.Name) }
        }
    }
    $wb.Close($false)

    if ($Save) {
        Write-Output "== 8. Guardado recalculado (valores en cache)"
        $wb = $xl.Workbooks.Open($Path, 0, $false)
        $xl.CalculateFull()
        $wb.Worksheets.Item('00_PORTADA_BODEGA').Activate()
        $wb.Save()
        $wb.Close($false)
        Ok "Libro recalculado y guardado"
    }
}
catch {
    Fail ("Excepcion inesperada: " + $_.Exception.Message + " en " + $_.InvocationInfo.PositionMessage)
}
finally {
    $xl.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
Write-Output ""
if ($script:fails.Count -gt 0) { Write-Output ("RESULTADO: {0} falla(s)" -f $script:fails.Count); exit 1 }
Write-Output "RESULTADO: sin fallas"

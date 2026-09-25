<#
.SYNOPSIS
    M-INV - Verificacion del libro en Excel real (Windows + Excel 2016/365).

.DESCRIPTION
    Abre el libro con las macros deshabilitadas (se verifica el motor de formulas; el VBA se prueba en
    tools/build_xlsm.ps1) y comprueba:
      1. Estructura: hojas, visibilidad por capa, proteccion de hojas y de estructura.
      2. Recalculo completo sin errores de formula (se ignoran los #N/A intencionales de NA()).
      3. Integridad CQRS: StockActual = suma independiente de CantidadNeta por SKU; conservacion global.
      4. KPIs, asistente de proximo paso y lista de alertas.
      5. Listas: cascada Categoria -> Producto y busqueda por texto.
      6. Consulta de producto (kardex), pedido sugerido (total independiente y filtro), toma fisica y formulario.
      7. Navegacion: sigue cada hipervinculo de botones.
      8. (Opcional) Exporta PNG de cada hoja visible y guarda el libro recalculado.
    El script es ASCII a proposito (PowerShell 5.1 lee los .ps1 sin BOM como ANSI); los textos con tildes se
    escriben con [char]0xXXXX.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\verify_minv.ps1 -Path src\M-INV_V1_Core.xlsx -PreviewDir build\preview -Save
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
function U([string]$s) { return [regex]::Unescape($s) }
# Nota: los numeros se escriben con .Formula; PowerShell fija el tipo del setter COM Value2 tras asignar texto.
function Near($a, $b) { return [math]::Abs([double]$a - [double]$b) -lt 1e-6 }

$CHECK = [string][char]0x2714; $WARN = [string][char]0x26A0; $CROSS = [string][char]0x2716; $DOT = ' ' + [char]0xB7 + ' '
$CAT = 'Categor' + [char]0xED + 'a'
$REPONER = @('AGOTADO', ('CR' + [char]0xCD + 'TICO'), 'BAJO')
$visName = @{ -1 = 'visible'; 0 = 'oculta'; 2 = 'muy oculta' }

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$xl.AutomationSecurity = 3   # macros deshabilitadas: se verifica solo el motor de formulas
try {
    Write-Output ("M-INV verificacion :: " + (Split-Path $Path -Leaf) + "  (Excel " + $xl.Version + " build " + $xl.Build + ")")
    $wb = $xl.Workbooks.Open($Path, 0, $true)
    $xl.CalculateFull()
    $Nm = { param($nombre) $wb.Names.Item($nombre).RefersToRange }
    $hasSheet = { param($n) foreach ($s in $wb.Worksheets) { if ($s.Name -eq $n) { return $true } }; return $false }

    Write-Output "== 1. Estructura y capas"
    Info ("Proteccion de estructura del libro: " + $wb.ProtectStructure)
    foreach ($ws in $wb.Worksheets) {
        Info ("{0,-16} {1,-11} protegida={2}" -f $ws.Name, $visName[[int]$ws.Visible], $ws.ProtectContents)
    }

    Write-Output "== 2. Errores de formula (recalculo completo)"
    $totalFormulas = 0; $intencionales = 0
    foreach ($ws in $wb.Worksheets) {
        try { $ws.Unprotect($Password) } catch { Fail ("No se pudo desproteger " + $ws.Name + " con la contrasena indicada") }
        try { $totalFormulas += $ws.UsedRange.SpecialCells(-4123).Count } catch {}
        $err = $null
        try { $err = $ws.UsedRange.SpecialCells(-4123, 16) } catch {}
        if ($err) {
            $reales = 0; $ejemplo = ''
            foreach ($c in $err.Cells) {
                if ($c.Formula -match 'NA\(\)') { $intencionales++ } else { $reales++; if (-not $ejemplo) { $ejemplo = $c.Address(0, 0) } }
            }
            if ($reales -gt 0) { Fail ("{0}: {1} celda(s) con error, p.ej. {2}" -f $ws.Name, $reales, $ejemplo) }
        }
    }
    Ok ("{0:N0} formulas evaluadas sin errores ({1} #N/A intencionales de series de grafico)" -f $totalFormulas, $intencionales)

    Write-Output "== 3. Integridad CQRS (proyeccion vs. bitacora)"
    $lm = $wb.Worksheets.Item('10_MOVIMIENTOS').ListObjects.Item('tblMovimientos')
    $ls = $wb.Worksheets.Item('15_STOCK').ListObjects.Item('tblStock')
    $mv = $lm.DataBodyRange.Value2
    $sv = $ls.DataBodyRange.Value2
    $cSku = $lm.ListColumns.Item('SKU').Index; $cNet = $lm.ListColumns.Item('CantidadNeta').Index
    $cEst = $lm.ListColumns.Item('Estado').Index; $cSal = $lm.ListColumns.Item('Saldo').Index
    $sums = @{}; $estados = @{}; $netTotal = 0.0; $rowsUsed = 0; $minSaldo = [double]::MaxValue
    for ($i = 1; $i -le $mv.GetLength(0); $i++) {
        $sku = [string]$mv[$i, $cSku]
        if ($sku -eq '') { continue }
        $rowsUsed++
        $net = [double]$mv[$i, $cNet]; $netTotal += $net
        if (-not $sums.ContainsKey($sku)) { $sums[$sku] = 0.0 }
        $sums[$sku] += $net
        $e = ([string]$mv[$i, $cEst]).Substring(0, 1); $estados[$e] = 1 + [int]$estados[$e]
        if ([double]$mv[$i, $cSal] -lt $minSaldo) { $minSaldo = [double]$mv[$i, $cSal] }
    }
    $S = @{}; foreach ($n in 'SKU', 'Producto', 'StockActual', 'StockMin', 'StockMax', 'Estado', 'CostoUnitario', 'Proveedor', 'Activo', 'Entradas', 'Salidas') { $S[$n] = $ls.ListColumns.Item($n).Index }
    $stockTotal = 0.0; $mismatch = 0; $dist = @{}; $prods = 0; $stockBySku = @{}
    for ($i = 1; $i -le $sv.GetLength(0); $i++) {
        $sku = [string]$sv[$i, $S['SKU']]
        if ($sku -eq '') { continue }
        $prods++
        $act = [double]$sv[$i, $S['StockActual']]; $stockTotal += $act; $stockBySku[$sku] = $i
        $exp = 0.0; if ($sums.ContainsKey($sku)) { $exp = $sums[$sku] }
        if (-not (Near $act $exp)) { $mismatch++; Fail ("{0}: StockActual={1} pero la bitacora suma {2}" -f $sku, $act, $exp) }
        $dist[[string]$sv[$i, $S['Estado']]] = 1 + [int]$dist[[string]$sv[$i, $S['Estado']]]
    }
    if ($mismatch -eq 0) { Ok ("{0} productos: StockActual coincide con la suma independiente de la bitacora" -f $prods) }
    if (-not (Near $stockTotal $netTotal)) { Fail ("Conservacion: stock total {0} <> suma de movimientos {1}" -f $stockTotal, $netTotal) }
    else { Ok ("Conservacion: stock total = suma de CantidadNeta = {0:N2} ({1} registros)" -f $stockTotal, $rowsUsed) }
    if ($rowsUsed -gt 0) {
        if ($minSaldo -lt 0) { Fail ("Hay saldos negativos en la bitacora (min {0})" -f $minSaldo) } else { Ok "Ningun saldo acumulado negativo" }
        $okRows = [int]$estados[$CHECK]
        if ($okRows -eq $rowsUsed) { Ok ("Estado de la bitacora: {0}/{1} registros '{2} Registrado'" -f $okRows, $rowsUsed, $CHECK) }
        else { Fail ("Estado de la bitacora: {0} ok, {1} advertencias, {2} errores" -f $okRows, [int]$estados[$WARN], [int]$estados[$CROSS]) }
    }
    Info ("Semaforo: " + (($dist.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0}={1}" -f $_.Name, $_.Value }) -join '  '))

    Write-Output "== 4. KPIs, asistente y alertas"
    foreach ($k in 'kpiActivos', 'kpiEnAlerta', 'kpiSinRotacion', 'kpiValor', 'kpiValorSinRot', 'kpiRegistros', 'kpiMovMes', 'kpiProveedores', 'kpiConSaldoInicial', 'kpiPaso', 'txtPaso', 'txtIntegridad') {
        Info ("{0,-18} {1}" -f $k, (& $Nm $k).Text)
    }
    $al = $wb.Worksheets.Item('16_ALERTAS')
    $nAl = 0
    for ($r = 8; $r -le 507; $r++) { if ([string]$al.Cells.Item($r, 2).Text -ne '') { $nAl++ } else { break } }
    $expAl = [int](& $Nm 'kpiEnAlerta').Value2 + [int](& $Nm 'kpiSobrestock').Value2
    if ($nAl -eq $expAl) { Ok ("16_ALERTAS lista {0} productos = en alerta + sobrestock" -f $nAl) } else { Fail ("16_ALERTAS lista {0} pero se esperaban {1}" -f $nAl, $expAl) }

    Write-Output "== 5. Listas: cascada y busqueda por texto"
    $ws = $wb.Worksheets.Item('10_MOVIMIENTOS')
    $cProd = $lm.ListColumns.Item('Producto').Index; $cCat = $lm.ListColumns.Item($CAT).Index
    $lp = $wb.Worksheets.Item('05_PRODUCTOS').ListObjects.Item('tblProductos')
    $pv = $lp.DataBodyRange.Value2
    $pSku = $lp.ListColumns.Item('SKU').Index; $pAct = $lp.ListColumns.Item('Activo').Index; $pLab = $lp.ListColumns.Item('Etiqueta').Index
    $inactivos = @{}; $primerLabel = ''
    for ($i = 1; $i -le $pv.GetLength(0); $i++) {
        if ([string]$pv[$i, $pAct] -eq 'NO') { $inactivos[[string]$pv[$i, $pSku]] = $true }
        if (-not $primerLabel -and [string]$pv[$i, $pLab] -ne '') { $primerLabel = [string]$pv[$i, $pLab] }
    }
    $checked = 0; $bad = 0; $hist = 0
    for ($i = 1; $i -le $rowsUsed; $i += 5) {
        $cell = $lm.DataBodyRange.Cells.Item($i, $cProd)
        $res = $ws.Evaluate($cell.Validation.Formula1)
        $vals = @(); foreach ($c in $res.Cells) { $vals += [string]$c.Value2 }
        if ($vals -notcontains [string]$cell.Value2) {
            $skuI = [string]$mv[$i, $cSku]
            if ($inactivos.ContainsKey($skuI)) { $hist++ } else { $bad++; Fail ("Fila {0}: '{1}' no esta en la lista de su categoria" -f $cell.Row, $cell.Value2) }
        }
        $checked++
    }
    if ($checked -gt 0 -and $bad -eq 0) { Ok ("Cascada: {0} filas muestreadas en su lista ({1} historicas de inactivos, esperado)" -f $checked, $hist) }
    if ($primerLabel) {
        $needle = ($primerLabel.Split(' ') | Select-Object -Last 1)
        $wb.Worksheets.Item('17_KARDEX').Range('E8').Value2 = $needle
        $xl.Calculate()
        $lista = $wb.Names.Item('lfKardex').RefersToRange
        $malos = 0; $n = 0
        foreach ($c in $lista.Cells) { $n++; if (([string]$c.Value2).ToLower().IndexOf($needle.ToLower()) -lt 0) { $malos++ } }
        if ($malos -eq 0) { Ok ("Busqueda por texto '{0}': {1} resultado(s), todos coinciden" -f $needle, $n) } else { Fail ("Busqueda '{0}': {1} resultado(s) no coinciden" -f $needle, $malos) }
        $wb.Worksheets.Item('17_KARDEX').Range('E8').Value2 = ''
    }

    Write-Output "== 6. Consulta, pedido, toma fisica y formulario"
    if ($rowsUsed -gt 0) {
        $kx = $wb.Worksheets.Item('17_KARDEX')
        $kxOriginal = [string]$kx.Range('H8').Value2
        $kx.Range('H8').Value2 = $primerLabel
        $xl.Calculate()
        $skuK = [string](& $Nm 'kxSKU').Value2
        $nK = [int](& $Nm 'kxN').Value2; $mostr = [int](& $Nm 'kxMostrados').Value2
        $saldo1 = [double]$kx.Range('G22').Value2
        $act = [double]$sv[$stockBySku[$skuK], $S['StockActual']]
        if (($mostr -eq [math]::Min($nK, 100)) -and (Near $saldo1 $act)) { Ok ("Kardex {0}: {1} movimientos; el saldo del mas reciente = stock actual ({2})" -f $skuK, $nK, $act) }
        else { Fail ("Kardex {0}: mostrados={1} de {2}, saldo reciente={3}, stock={4}" -f $skuK, $mostr, $nK, $saldo1, $act) }
        $kx.Range('H8').Value2 = $kxOriginal   # la consulta vuelve al producto con que abre el libro
    }
    # Pedido: total independiente desde 15_STOCK
    $pd = $wb.Worksheets.Item('18_PEDIDO')
    $pd.Range('D10').Value2 = '(Todos)'; $xl.Calculate()
    $expL = 0; $expT = 0.0; $provCount = @{}
    for ($i = 1; $i -le $sv.GetLength(0); $i++) {
        if ([string]$sv[$i, $S['SKU']] -eq '' -or [string]$sv[$i, $S['Activo']] -ne 'SI') { continue }
        if ($REPONER -notcontains [string]$sv[$i, $S['Estado']]) { continue }
        $st = [double]$sv[$i, $S['StockActual']]; $mn = [double]$sv[$i, $S['StockMin']]; $mx = [double]$sv[$i, $S['StockMax']]
        $tope = if ($mx -gt 0) { $mx } else { 2 * $mn }
        $sug = [math]::Max(0, $tope - [math]::Max(0, $st))
        $expL++; $expT += $sug * [double]$sv[$i, $S['CostoUnitario']]
        $pk = [string]$sv[$i, $S['Proveedor']]; $provCount[$pk] = 1 + [int]$provCount[$pk]
    }
    $lin = [int](& $Nm 'kpiPedidoLineas').Value2; $tot = [double](& $Nm 'kpiPedidoTotal').Value2
    if ($lin -eq $expL -and (Near $tot $expT)) { Ok ("Pedido (Todos): {0} lineas, total $ {1:N0} = calculo independiente" -f $lin, $tot) }
    else { Fail ("Pedido (Todos): {0} lineas / $ {1:N0}; se esperaban {2} / $ {3:N0}" -f $lin, $tot, $expL, $expT) }
    $provF = ($provCount.Keys | Where-Object { $_ -ne '' } | Select-Object -First 1)
    if ($provF) {
        $pd.Range('D10').Value2 = $provF; $xl.Calculate()
        $lin2 = [int](& $Nm 'kpiPedidoLineas').Value2; $otros = 0
        for ($r = 15; $r -lt 15 + $lin2; $r++) { if ([string]$pd.Cells.Item($r, 3).Value2 -ne $provF) { $otros++ } }
        if ($lin2 -eq $provCount[$provF] -and $otros -eq 0) { Ok ("Filtro por proveedor '{0}': {1} lineas, todas de ese proveedor" -f $provF, $lin2) }
        else { Fail ("Filtro '{0}': {1} lineas ({2} de otros), se esperaban {3}" -f $provF, $lin2, $otros, $provCount[$provF]) }
        $pd.Range('D10').Value2 = '(Todos)'
    }
    # Toma fisica
    if ($prods -ge 2) {
        $ct = $wb.Worksheets.Item('13_CONTEO')
        $s1 = [double]$ct.Range('H8').Value2; $s2 = [double]$ct.Range('H9').Value2
        $ct.Range('I8').Formula = [string]($s1 + 2); $ct.Range('I9').Formula = [string]([math]::Max(0, $s2 - 1)); $xl.Calculate()
        $d1 = [double]$ct.Range('J8').Value2; $d2 = [double]$ct.Range('J9').Value2
        $expD2 = [math]::Max(0, $s2 - 1) - $s2
        if ((Near $d1 2) -and (Near $d2 $expD2) -and [int](& $Nm 'kpiConteoContados').Value2 -eq 2) { Ok ("Toma fisica: diferencias +{0} y {1}; resultado '{2}' / '{3}'" -f $d1, $d2, $ct.Range('L8').Text, $ct.Range('L9').Text) }
        else { Fail ("Toma fisica: diferencias {0} / {1} (esperadas 2 / {2})" -f $d1, $d2, $expD2) }
        $ct.Range('I8:I9').ClearContents()
    }
    # Formulario (solo edicion Plus)
    if ((& $hasSheet '12_REGISTRO') -and $rowsUsed -gt 0) {
        $fr = $wb.Worksheets.Item('12_REGISTRO')
        $idx = $stockBySku[$skuK]; $act = [double]$sv[$idx, $S['StockActual']]
        $fr.Range('D8').Value2 = 'SALIDA'; $fr.Range('D12').Value2 = $primerLabel; $fr.Range('D14').Value2 = $wb.Worksheets.Item('01_CONFIG').ListObjects.Item('tblResponsables').DataBodyRange.Cells.Item(1, 1).Value2
        $fr.Range('D13').Formula = '1'; $xl.Calculate()
        $v1 = [bool](& $Nm 'frmValido').Value2; $desp = [double](& $Nm 'frmDespues').Value2
        $fr.Range('D13').Formula = [string]($act + 5); $xl.Calculate()
        $v2 = [bool](& $Nm 'frmValido').Value2; $chk4 = [string](& $Nm 'frmChk4').Value2
        if ($v1 -and (Near $desp ($act - 1)) -and -not $v2 -and $chk4.StartsWith($CROSS)) { Ok ("Formulario: SALIDA 1 valida (quedan {0}); SALIDA {1} bloqueada: {2}" -f $desp, ($act + 5), $chk4) }
        else { Fail ("Formulario: valido1={0} despues={1} valido2={2} chk4={3}" -f $v1, $desp, $v2, $chk4) }
        $fr.Range('D11').Value2 = $needle; $xl.Calculate()
        $malos = 0; foreach ($c in $wb.Names.Item('lfForm').RefersToRange.Cells) { if (([string]$c.Value2).ToLower().IndexOf($needle.ToLower()) -lt 0) { $malos++ } }
        if ($malos -eq 0) { Ok ("Formulario: la busqueda '{0}' filtra la lista de productos" -f $needle) } else { Fail "Formulario: la busqueda no filtra correctamente" }
        $fr.Range('D8:G16').ClearContents()
    }

    Write-Output "== 7. Navegacion (hipervinculos de botones)"
    $seen = @{}
    foreach ($sh in $wb.Worksheets) {
        if ($sh.Visible -ne -1) { continue }
        foreach ($s in $sh.Shapes) {
            $sub = $null
            try { $sub = $s.Hyperlink.SubAddress } catch { continue }
            if (-not $sub -or $seen.ContainsKey($sub)) { continue }
            try {
                $sh.Activate()
                $s.Hyperlink.Follow()
                $seen[$sub] = $xl.ActiveSheet.Name + '!' + $xl.ActiveCell.Address(0, 0)
                Ok ("{0,-22} -> {1}" -f $sub, $seen[$sub])
            } catch { $seen[$sub] = 'ERROR'; Fail ("El vinculo '{0}' ({1}) no navega: {2}" -f $sub, $sh.Name, $_.Exception.Message) }
        }
    }

    if ($PreviewDir) {
        Write-Output "== 8. Vistas previas"
        New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
        $PreviewDir = (Resolve-Path $PreviewDir).Path
        $stem = [IO.Path]::GetFileNameWithoutExtension($Path)
        # Edicion Plus: las vistas previas muestran el libro como se ve con macros (sin el aviso "sin macros")
        foreach ($sh in $wb.Worksheets) {
            foreach ($s in $sh.Shapes) { if ($s.AlternativeText -like 'Aviso sin macros*') { $s.Visible = 0 } }
        }
        $rows = @{ '00_PORTADA' = 31; '99_AYUDA' = 80; '12_REGISTRO' = 23; '17_KARDEX' = 45; '18_PEDIDO' = 36 }
        foreach ($sh in $wb.Worksheets) {
            if ($sh.Visible -ne -1) { continue }
            $sh.Activate()
            $lastRow = 34; if ($rows.ContainsKey($sh.Name)) { $lastRow = $rows[$sh.Name] }
            $lastCol = $sh.UsedRange.Column + $sh.UsedRange.Columns.Count
            $rng = $sh.Range($sh.Cells.Item(1, 1), $sh.Cells.Item($lastRow, [math]::Min($lastCol, 20)))
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
        Write-Output "== 9. Guardado recalculado (valores en cache para vistas previas)"
        $wb = $xl.Workbooks.Open($Path, 0, $false)
        $xl.CalculateFull()
        $wb.Worksheets.Item('00_PORTADA').Activate()
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

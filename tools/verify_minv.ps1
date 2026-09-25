<#
.SYNOPSIS
    M-INV V1 - Verificacion del libro en Excel real (Windows + Excel 2016/365).

.DESCRIPTION
    1. Estructura: hojas, visibilidad por capa, proteccion de hojas y de estructura.
    2. Recalculo completo y busqueda de errores de formula en todas las hojas.
    3. Integridad CQRS: StockActual de 15_STOCK = suma independiente de CantidadNeta por SKU.
    4. KPIs del tablero (nombres kpi*).
    5. Lista en cascada: el producto de cada fila pertenece a la lista filtrada por su categoria.
    6. Navegacion: sigue cada hipervinculo de formas (botones) y reporta el destino.
    7. (Opcional) Exporta PNG de cada hoja visible y guarda el libro recalculado.

    El script es ASCII a proposito (PowerShell 5.1 lee los .ps1 sin BOM como ANSI).

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

$CHECK = [string][char]0x2714   # check
$WARN = [string][char]0x26A0    # warning
$CROSS = [string][char]0x2716   # cross
$visName = @{ -1 = 'visible'; 0 = 'oculta'; 2 = 'muy oculta' }

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
try {
    Write-Output ("M-INV verificacion :: " + (Split-Path $Path -Leaf) + "  (Excel " + $xl.Version + " build " + $xl.Build + ")")
    $wb = $xl.Workbooks.Open($Path, 0, $true)
    $xl.CalculateFull()

    Write-Output "== 1. Estructura y capas"
    Info ("Proteccion de estructura del libro: " + $wb.ProtectStructure)
    foreach ($ws in $wb.Worksheets) {
        Info ("{0,-16} {1,-11} protegida={2}" -f $ws.Name, $visName[[int]$ws.Visible], $ws.ProtectContents)
    }

    Write-Output "== 2. Errores de formula (recalculo completo)"
    $totalFormulas = 0
    foreach ($ws in $wb.Worksheets) {
        try { $ws.Unprotect($Password) } catch { Fail ("No se pudo desproteger " + $ws.Name + " con la contrasena indicada") }
        $n = 0
        try { $n = $ws.UsedRange.SpecialCells(-4123).Count } catch {}
        $totalFormulas += $n
        try {
            $err = $ws.UsedRange.SpecialCells(-4123, 16)
            Fail ("{0}: {1} celda(s) con error, p.ej. {2}" -f $ws.Name, $err.Count, $err.Areas.Item(1).Address(0, 0))
        } catch { }
    }
    Ok ("{0:N0} formulas evaluadas" -f $totalFormulas)

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
        $net = [double]$mv[$i, $cNet]
        $netTotal += $net
        if (-not $sums.ContainsKey($sku)) { $sums[$sku] = 0.0 }
        $sums[$sku] += $net
        $e = ([string]$mv[$i, $cEst]).Substring(0, 1)
        $estados[$e] = 1 + [int]$estados[$e]
        if ([double]$mv[$i, $cSal] -lt $minSaldo) { $minSaldo = [double]$mv[$i, $cSal] }
    }
    $sSku = $ls.ListColumns.Item('SKU').Index; $sAct = $ls.ListColumns.Item('StockActual').Index
    $sEst = $ls.ListColumns.Item('Estado').Index
    $stockTotal = 0.0; $mismatch = 0; $dist = @{}; $prods = 0
    for ($i = 1; $i -le $sv.GetLength(0); $i++) {
        $sku = [string]$sv[$i, $sSku]
        if ($sku -eq '') { continue }
        $prods++
        $act = [double]$sv[$i, $sAct]
        $stockTotal += $act
        $exp = 0.0; if ($sums.ContainsKey($sku)) { $exp = $sums[$sku] }
        if ([math]::Abs($act - $exp) -gt 1e-9) { $mismatch++; Fail ("{0}: StockActual={1} pero la bitacora suma {2}" -f $sku, $act, $exp) }
        $dist[[string]$sv[$i, $sEst]] = 1 + [int]$dist[[string]$sv[$i, $sEst]]
    }
    if ($mismatch -eq 0) { Ok ("{0} productos: StockActual coincide con la suma independiente de la bitacora" -f $prods) }
    if ([math]::Abs($stockTotal - $netTotal) -gt 1e-6) { Fail ("Conservacion: stock total {0} <> suma de movimientos {1}" -f $stockTotal, $netTotal) }
    else { Ok ("Conservacion: stock total = suma de CantidadNeta = {0:N2} ({1} registros)" -f $stockTotal, $rowsUsed) }
    if ($rowsUsed -gt 0) {
        if ($minSaldo -lt 0) { Fail ("Hay saldos negativos en la bitacora (min {0})" -f $minSaldo) } else { Ok "Ningun saldo acumulado negativo" }
        $okRows = [int]$estados[$CHECK]
        if ($okRows -eq $rowsUsed) { Ok ("Estado de la bitacora: {0}/{1} registros '{2} Registrado'" -f $okRows, $rowsUsed, $CHECK) }
        else { Fail ("Estado de la bitacora: {0} ok, {1} advertencias, {2} errores" -f $okRows, [int]$estados[$WARN], [int]$estados[$CROSS]) }
    }
    Info ("Semaforo: " + (($dist.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0}={1}" -f $_.Name, $_.Value }) -join '  '))

    Write-Output "== 4. KPIs del tablero"
    $kpis = 'kpiCatalogo', 'kpiActivos', 'kpiAgotados', 'kpiCriticos', 'kpiBajos', 'kpiOptimos', 'kpiSobrestock', 'kpiInactivos',
            'kpiEnAlerta', 'kpiValor', 'kpiRegistros', 'kpiMovMes', 'kpiIngMes', 'kpiEgrMes', 'kpiUltFecha', 'kpiFilaLibreMov',
            'kpiFilaLibreProd', 'txtIntegridad', 'txtAlertasBtn'
    foreach ($k in $kpis) { Info ("{0,-18} {1}" -f $k, $wb.Names.Item($k).RefersToRange.Text) }
    $al = $wb.Worksheets.Item('16_ALERTAS')
    $nAl = 0
    for ($r = 8; $r -le 507; $r++) { if ([string]$al.Cells.Item($r, 2).Text -ne '') { $nAl++ } else { break } }
    Info ("16_ALERTAS lista {0} producto(s); primeros:" -f $nAl)
    for ($r = 8; $r -lt 8 + [math]::Min($nAl, 12); $r++) {
        Info ("  #{0,-3} {1,-14} {2,-8} stock={3,-7} sugerido={4}" -f $al.Cells.Item($r, 2).Text, $al.Cells.Item($r, 3).Text, $al.Cells.Item($r, 4).Text, $al.Cells.Item($r, 7).Text, $al.Cells.Item($r, 12).Text)
    }
    $expAl = [int]$wb.Names.Item('kpiEnAlerta').RefersToRange.Value2 + [int]$wb.Names.Item('kpiSobrestock').RefersToRange.Value2
    if ($nAl -eq $expAl) { Ok ("16_ALERTAS = en alerta + sobrestock = {0}" -f $expAl) } else { Fail ("16_ALERTAS lista {0} pero se esperaban {1}" -f $nAl, $expAl) }

    Write-Output "== 5. Lista en cascada (Categoria -> Producto)"
    $ws = $wb.Worksheets.Item('10_MOVIMIENTOS')
    $cProd = $lm.ListColumns.Item('Producto').Index; $cCat = $lm.ListColumns.Item('Categor' + [char]0xED + 'a').Index
    # SKUs inactivos: sus movimientos historicos son validos aunque ya no aparezcan en la lista
    $lp = $wb.Worksheets.Item('05_PRODUCTOS').ListObjects.Item('tblProductos')
    $pv = $lp.DataBodyRange.Value2
    $pSku = $lp.ListColumns.Item('SKU').Index; $pAct = $lp.ListColumns.Item('Activo').Index
    $inactivos = @{}
    for ($i = 1; $i -le $pv.GetLength(0); $i++) { if ([string]$pv[$i, $pAct] -eq 'NO') { $inactivos[[string]$pv[$i, $pSku]] = $true } }
    $checked = 0; $bad = 0; $hist = 0
    for ($i = 1; $i -le $rowsUsed; $i += 3) {
        $cell = $lm.DataBodyRange.Cells.Item($i, $cProd)
        $res = $ws.Evaluate($cell.Validation.Formula1)
        $vals = @(); foreach ($c in $res.Cells) { $vals += [string]$c.Value2 }
        if ($vals -notcontains [string]$cell.Value2) {
            $skuI = [string]$mv[$i, $cSku]
            if ($inactivos.ContainsKey($skuI)) { $hist++ }
            else { $bad++; Fail ("Fila {0}: '{1}' no esta en la lista de '{2}'" -f $cell.Row, $cell.Value2, $lm.DataBodyRange.Cells.Item($i, $cCat).Value2) }
        }
        $checked++
    }
    if ($checked -gt 0 -and $bad -eq 0) { Ok ("{0} filas muestreadas: cada producto pertenece a la lista filtrada por su categoria ({1} historicas de productos inactivos, esperado)" -f $checked, $hist) }
    $probe = $lm.DataBodyRange.Cells.Item([math]::Min($rowsUsed + 1, $lm.ListRows.Count), $cProd)
    $res = $ws.Evaluate($probe.Validation.Formula1)
    try { Ok ("Fila libre sin categoria: la lista ofrece {0} opcion(es); primera: '{1}'" -f $res.Cells.Count, $res.Cells.Item(1).Text) } catch { Info ("Fila libre sin categoria: " + $res) }

    Write-Output "== 6. Navegacion (hipervinculos de botones)"
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
        Write-Output "== 7. Vistas previas"
        New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
        $PreviewDir = (Resolve-Path $PreviewDir).Path
        $stem = [IO.Path]::GetFileNameWithoutExtension($Path)
        foreach ($sh in $wb.Worksheets) {
            if ($sh.Visible -ne -1) { continue }
            $sh.Activate()
            $lastRow = 34; if ($sh.Name -eq '00_PORTADA') { $lastRow = 29 } elseif ($sh.Name -eq '99_AYUDA') { $lastRow = 60 }
            $lastCol = $sh.UsedRange.Column + $sh.UsedRange.Columns.Count
            $rng = $sh.Range($sh.Cells.Item(1, 1), $sh.Cells.Item($lastRow, [math]::Min($lastCol, 18)))
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
        Write-Output "== 8. Guardado recalculado (valores en cache para vistas previas)"
        $wb = $xl.Workbooks.Open($Path, 0, $false)
        $xl.CalculateFull()
        $wb.Worksheets.Item('00_PORTADA').Activate()
        $wb.Save()
        $wb.Close($false)
        Ok "Libro recalculado y guardado"
    }
}
finally {
    $xl.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
    [GC]::Collect()
}
Write-Output ""
if ($script:fails.Count -gt 0) { Write-Output ("RESULTADO: {0} falla(s)" -f $script:fails.Count); exit 1 }
Write-Output "RESULTADO: sin fallas"

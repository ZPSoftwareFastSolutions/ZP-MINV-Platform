<#
.SYNOPSIS
    M-INV V1.2 - Genera la edicion Plus (.xlsm) desde las bases build\*.plus.xlsx y la prueba en Excel.

.DESCRIPTION
    Para cada libro (Core con datos demo y Release limpio):
      1. Abre la base generada por tools\build_minv.py, instala el VBA de src\macros (modulos .bas, codigo del
         libro y de las hojas) con la contrasena de proteccion inyectada y guarda el .xlsm (formato 52).
      2. Lo reabre con macros: Workbook_Open reaplica la proteccion; se sella la bitacora (demo en gris y
         bloqueada) y se guarda: el aviso "sin macros" del formulario queda visible en el archivo.
      3. Prueba una copia temporal con macros habilitadas y en modo silencioso (sin cuadros de dialogo):
         modo app, proteccion, botones, formulario (valido e invalido), sellado, reposicion, consulta,
         toma fisica -> ajustes, guardado, cierre y reapertura sin macros.

    Los fuentes VBA estan en UTF-8 y se instalan con AddFromString (el Editor de VBA los guarda en
    Windows-1252); el script rechaza caracteres fuera de ese juego (use ChrW en el VBA).
    Requiere Excel con "Confiar en el acceso al modelo de objetos de proyectos de VBA" activado (activelo solo
    mientras construye). Restaura la barra de formulas del usuario al terminar.
    Script ASCII a proposito: PowerShell 5.1 lee los .ps1 sin BOM como ANSI.

    Contrasenas: MINV_PASSWORD (Core, por defecto minv-dev) y MINV_RELEASE_PASSWORD (Release; si falta usa la
    del Core), igual que tools\build_minv.py.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build_xlsm.ps1
    powershell -ExecutionPolicy Bypass -File tools\build_xlsm.ps1 -Solo core -SinPruebas
#>
param(
    [ValidateSet('', 'core', 'release')][string]$Solo = '',
    [switch]$SinPruebas
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
$macros = Join-Path $root 'src\macros'

$CHECK = [string][char]0x2714; $CROSS = [string][char]0x2716
$MACROS_BOTONES = @('RegistrarMovimiento', 'LimpiarFormulario', 'GenerarAjustesConteo', 'RegistrarDesdeKardex')
$corePwd = if ($env:MINV_PASSWORD) { $env:MINV_PASSWORD } else { 'minv-dev' }
$relPwd = if ($env:MINV_RELEASE_PASSWORD) { $env:MINV_RELEASE_PASSWORD } else { $corePwd }
$libros = @(
    @{ Id = 'core'; Base = 'build\M-INV_V1_Core.plus.xlsx'; Destino = 'src\M-INV_V1_Core.xlsm'; Clave = $corePwd; Demo = $true },
    @{ Id = 'release'; Base = 'build\M-INV_V1_Produccion_Bloqueado.plus.xlsx'
       Destino = 'releases\M-INV_V1_Produccion_Bloqueado.xlsm'; Clave = $relPwd; Demo = $false }
)
if (-not $env:MINV_RELEASE_PASSWORD -and $Solo -ne 'core') {
    Write-Output "[aviso]  MINV_RELEASE_PASSWORD no definida: el release usa la contrasena de desarrollo."
}

$script:fallas = 0
function Ok([string]$m) { Write-Output ("  [OK]    " + $m) }
function Fail([string]$m) { $script:fallas++; Write-Output ("  [FALLA] " + $m) }
function Check([bool]$cond, [string]$okMsg, [string]$failMsg) { if ($cond) { Ok $okMsg } else { Fail $failMsg } }
function Near($a, $b) { return [math]::Abs([double]$a - [double]$b) -lt 1e-6 }
function Prueba([string]$nombre, [scriptblock]$cuerpo) {
    try { & $cuerpo } catch { Fail ("{0}: {1}" -f $nombre, $_.Exception.Message) }
}

# ---------------------------------------------------------------- instalacion del VBA
$cp1252 = [Text.Encoding]::GetEncoding(1252, [Text.EncoderFallback]::ExceptionFallback,
    [Text.DecoderFallback]::ExceptionFallback)

function Leer-Fuente([string]$ruta, [string]$clave) {
    $t = [IO.File]::ReadAllText($ruta, [Text.Encoding]::UTF8)
    $t = ($t -replace "`r?`n", "`r`n").TrimEnd()
    if ($clave) { $t = $t.Replace('__MINV_PASSWORD__', $clave.Replace('"', '""')) }
    try { [void]$cp1252.GetBytes($t) }
    catch { throw ((Split-Path $ruta -Leaf) + ': contiene caracteres fuera de Windows-1252 (use ChrW en el VBA)') }
    return $t
}

function Poner-Codigo($cm, [string]$codigo) {
    if ($cm.CountOfLines -gt 0) { $cm.DeleteLines(1, $cm.CountOfLines) }   # quita el Option Explicit automatico
    $cm.AddFromString($codigo)
}

function Componente-De-Hoja($wb, $vbp, [string]$nombre) {
    $cn = $wb.Worksheets.Item($nombre).CodeName
    if ($cn) { return $vbp.VBComponents.Item($cn) }
    foreach ($c in $vbp.VBComponents) {
        if ($c.Type -ne 100) { continue }
        try { if ($c.Properties.Item('Name').Value -eq $nombre) { return $c } } catch { }
    }
    throw ("No se encontro el modulo de la hoja " + $nombre)
}

function Instalar-Vba($wb, [string]$clave) {
    try { $vbp = $wb.VBProject; [void]$vbp.VBComponents.Count }
    catch {
        throw ("Excel no permite acceder al proyecto VBA. Active 'Confiar en el acceso al modelo de objetos de " +
            "proyectos de VBA' (Centro de confianza > Configuracion de macros) mientras construye.")
    }
    $n = 0
    foreach ($f in (Get-ChildItem $macros -Filter '*.bas' | Sort-Object Name)) {
        $lineas = (Leer-Fuente $f.FullName $clave) -split "`r`n"
        $m = [regex]::Match($lineas[0], '^Attribute VB_Name = "(\w+)"')
        if (-not $m.Success) { throw ($f.Name + ': la primera linea debe ser Attribute VB_Name = "...".') }
        $codigo = ($lineas | Select-Object -Skip 1 | Where-Object { $_ -notmatch '^Attribute ' }) -join "`r`n"
        $comp = $vbp.VBComponents.Add(1)
        $comp.Name = $m.Groups[1].Value
        Poner-Codigo $comp.CodeModule $codigo
        $n++
    }
    Poner-Codigo $vbp.VBComponents.Item($wb.CodeName).CodeModule (Leer-Fuente (Join-Path $macros 'ThisWorkbook.txt') '')
    $h = 0
    foreach ($f in (Get-ChildItem $macros -Filter 'Hoja_*.txt' | Sort-Object Name)) {
        $comp = Componente-De-Hoja $wb $vbp $f.BaseName.Substring(5)
        Poner-Codigo $comp.CodeModule (Leer-Fuente $f.FullName '')
        $h++
    }
    return ("{0} modulos, ThisWorkbook y {1} hojas" -f $n, $h)
}

function Avisos-Visibles($wb) {
    $vis = @()
    foreach ($s in $wb.Worksheets.Item('12_REGISTRO').Shapes) {
        if ($s.AlternativeText -like 'Aviso sin macros*') { $vis += [int]$s.Visible }
    }
    return $vis
}

# ---------------------------------------------------------------- pruebas
# La coma evita que PowerShell enumere (desenrolle) un rango de varias celdas al devolverlo.
function N([string]$nombre) { return , $script:wb.Names.Item($nombre).RefersToRange }
function V([string]$nombre) { return (N $nombre).Value2 }
function M0([string]$m) { return $script:xl.Run($script:pre + $m) }
function M1([string]$m, $a) { return $script:xl.Run($script:pre + $m, $a) }
function M2([string]$m, $a, $b) { return $script:xl.Run($script:pre + $m, $a, $b) }
function StockDe([string]$sku) { return [double](M2 'DatoStock' $sku 'StockActual') }

function Probar-Libro($libro, [string]$ruta) {
    $tmp = Join-Path $env:TEMP ('minv-prueba-{0}-{1}.xlsm' -f $libro.Id, [guid]::NewGuid().ToString('N').Substring(0, 8))
    Copy-Item $ruta $tmp -Force
    try {
        $xl.AutomationSecurity = 1
        $xl.EnableEvents = $true
        $xl.DisplayFormulaBar = $true
        $script:wb = $xl.Workbooks.Open($tmp)
        $script:pre = "'" + $wb.Name + "'!"
        [void](M1 'ActivarModoSilencioso' $true)
        $ws10 = $wb.Worksheets.Item('10_MOVIMIENTOS')
        $lm = $ws10.ListObjects.Item('tblMovimientos')
        $hdr = $lm.HeaderRowRange.Row
        $cEst = $lm.ListColumns.Item('Estado').Range.Column
        $cFec = $lm.ListColumns.Item('Fecha').Range.Column

        Prueba 'Apertura' {
            Check ($xl.ActiveSheet.Name -eq '00_PORTADA' -and -not $xl.DisplayFormulaBar) `
                'Workbook_Open: abre en 00_PORTADA con la barra de formulas oculta' `
                ('Workbook_Open: hoja={0} barra={1}' -f $xl.ActiveSheet.Name, $xl.DisplayFormulaBar)
            $av = Avisos-Visibles $wb
            Check ($av.Count -gt 0 -and ($av | Where-Object { $_ -ne 0 }).Count -eq 0) `
                'Aviso "sin macros" oculto mientras las macros estan activas' ('Avisos sin macros: ' + ($av -join ','))
            $prot = $ws10.ProtectContents -and $wb.Worksheets.Item('12_REGISTRO').ProtectContents
            Check ($prot -and $wb.Worksheets.Item('12_REGISTRO').EnableSelection -eq 1) `
                'Proteccion reaplicada (UserInterfaceOnly); en el formulario solo se seleccionan campos' `
                'La proteccion de 10_MOVIMIENTOS/12_REGISTRO no quedo como se esperaba'
            if (-not $libro.Demo) {
                Check ([bool]$wb.ProtectStructure) 'Release: estructura del libro protegida' 'Release: estructura sin proteger'
            }
        }

        Prueba 'Botones' {
            $asignadas = @{}
            foreach ($sh in $wb.Worksheets) {
                foreach ($s in $sh.Shapes) {
                    $oa = [string]$s.OnAction
                    if (-not $oa) { continue }
                    $nombre = ($oa -split '!')[-1]
                    if ($oa -match 'plus\.xlsx' -or $MACROS_BOTONES -notcontains $nombre) {
                        Fail ("Boton '{0}' en {1} apunta a '{2}'" -f $s.Name, $sh.Name, $oa)
                    }
                    $asignadas[$nombre] = 1
                }
            }
            Check ($asignadas.Count -eq $MACROS_BOTONES.Count) ('Botones con macro: ' + (($asignadas.Keys | Sort-Object) -join ', ')) `
                ('Faltan botones con macro: ' + (($MACROS_BOTONES | Where-Object { -not $asignadas.ContainsKey($_) }) -join ', '))
        }

        if ($libro.Demo) {
            $ult = [int](V 'kpiUltimoID')
            Prueba 'Sellado demo' {
                Check ([int](V 'cfgSelladoHasta') -eq $ult -and $ws10.Cells.Item($hdr + 1, $cFec).Locked -and `
                        $ws10.Cells.Item($hdr + $ult, $cFec).Locked -and -not $ws10.Cells.Item($hdr + $ult + 1, $cFec).Locked) `
                    ('Bitacora demo sellada: {0} registros bloqueados; la fila libre sigue abierta' -f $ult) `
                    ('Sellado demo: cfgSelladoHasta={0} ultimo ID={1}' -f (V 'cfgSelladoHasta'), $ult)
            }

            # Productos de prueba: activos, con stock entero >= 3
            $ls = $wb.Worksheets.Item('15_STOCK').ListObjects.Item('tblStock')
            $d = $ls.DataBodyRange.Value2
            $cS = $ls.ListColumns.Item('SKU').Index; $cA = $ls.ListColumns.Item('Activo').Index
            $cQ = $ls.ListColumns.Item('StockActual').Index
            $cand = @()
            for ($i = 1; $i -le $d.GetLength(0); $i++) {
                if ([string]$d[$i, $cS] -eq '' -or [string]$d[$i, $cA] -ne 'SI') { continue }
                $q = [double]$d[$i, $cQ]
                if ($q -ge 3 -and $q -eq [math]::Floor($q)) { $cand += [string]$d[$i, $cS] }
            }
            $skuA = $cand[0]; $skuB = $cand[1]; $skuC = $cand[2]
            $resp = [string](N 'lstResponsables').Cells.Item(1, 1).Value2

            Prueba 'Formulario valido' {
                $antes = [int](V 'kpiRegistros'); $st = StockDe $skuA
                (N 'frmTipo').Value2 = 'SALIDA'
                (N 'frmProducto').Value2 = [string](M2 'DatoProducto' $skuA 'Etiqueta')
                (N 'frmCantidad').Formula = '1'
                (N 'frmResponsable').Value2 = $resp
                [void](M0 'RegistrarMovimiento')
                $id = [int](V 'kpiUltimoID'); $fila = $hdr + $id
                $okReg = ([int](V 'kpiRegistros') -eq $antes + 1) -and (Near (StockDe $skuA) ($st - 1))
                Check $okReg ("RegistrarMovimiento: SALIDA 1 de {0} -> registro #{1}, stock {2} -> {3}" -f $skuA, $id, $st, (StockDe $skuA)) `
                    ("RegistrarMovimiento no registro: registros {0}->{1}, mensaje '{2}'" -f $antes, (V 'kpiRegistros'), (V 'frmMensaje'))
                $est = [string]$ws10.Cells.Item($fila, $cEst).Value2
                Check ($est.StartsWith($CHECK) -and $ws10.Cells.Item($fila, $cFec).Locked -and [int](V 'cfgSelladoHasta') -eq $id) `
                    ('Registro #{0} verificado y sellado ({1})' -f $id, $est) ('Registro #{0}: estado={1} bloqueado={2} sellado={3}' -f `
                        $id, $est, $ws10.Cells.Item($fila, $cFec).Locked, (V 'cfgSelladoHasta'))
                Check (([string](V 'frmMensaje')).StartsWith($CHECK) -and [string](V 'frmProducto') -eq '' -and `
                        [string](V 'frmTipo') -eq 'SALIDA' -and [string](V 'frmResponsable') -eq $resp) `
                    'Formulario: mensaje de exito; conserva tipo y responsable, limpia producto y cantidad' `
                    ("Formulario tras registrar: mensaje='{0}' producto='{1}' tipo='{2}'" -f (V 'frmMensaje'), (V 'frmProducto'), (V 'frmTipo'))
            }

            Prueba 'Formulario invalido' {
                $antes = [int](V 'kpiRegistros'); $st = StockDe $skuA
                (N 'frmProducto').Value2 = [string](M2 'DatoProducto' $skuA 'Etiqueta')
                (N 'frmCantidad').Formula = [string]([int]$st + 1000)
                [void](M0 'RegistrarMovimiento')
                Check ([int](V 'kpiRegistros') -eq $antes -and ([string](V 'frmMensaje')).StartsWith($CROSS)) `
                    ('SALIDA mayor que el stock rechazada sin escribir: ' + [string](V 'frmChk4')) `
                    ("SALIDA invalida: registros {0}->{1}, mensaje '{2}'" -f $antes, (V 'kpiRegistros'), (V 'frmMensaje'))
                [void](M0 'LimpiarFormulario')
                Check ([string](V 'frmTipo') -eq '' -and [string](V 'frmProducto') -eq '' -and [string](V 'frmMensaje') -eq '') `
                    'LimpiarFormulario vacia todos los campos' 'LimpiarFormulario dejo datos en el formulario'
            }

            Prueba 'Reposicion' {
                $ws16 = $wb.Worksheets.Item('16_ALERTAS')
                $skuAl = [string](M2 'SkuDeFila' $ws16 8)
                $mx = [double](M2 'DatoStock' $skuAl 'StockMax'); $mn = [double](M2 'DatoStock' $skuAl 'StockMin')
                $st = StockDe $skuAl
                $esperado = [math]::Max(0, $(if ($mx -gt 0) { $mx } else { 2 * $mn }) - [math]::Max(0, $st))
                [void](M1 'AbrirFormularioReposicion' $skuAl)
                Check ($xl.ActiveSheet.Name -eq '12_REGISTRO' -and [string](V 'frmTipo') -eq 'ENTRADA' -and `
                        ([string](V 'frmProducto')).StartsWith($skuAl) -and (Near (V 'frmCantidad') $esperado) -and $esperado -gt 0) `
                    ('Doble clic en alerta {0}: formulario ENTRADA con la cantidad sugerida ({1})' -f $skuAl, $esperado) `
                    ("Reposicion {0}: hoja={1} tipo='{2}' cantidad={3} esperado={4}" -f $skuAl, $xl.ActiveSheet.Name, (V 'frmTipo'), (V 'frmCantidad'), $esperado)
                (N 'frmResponsable').Value2 = $resp
                [void](M0 'RegistrarMovimiento')
                Check (Near (StockDe $skuAl) ($st + $esperado)) ('Reposicion registrada: stock {0} -> {1}' -f $st, (StockDe $skuAl)) `
                    ("Reposicion no registrada: mensaje '{0}'" -f (V 'frmMensaje'))
            }

            Prueba 'Doble clic' {
                $ws18 = $wb.Worksheets.Item('18_PEDIDO')
                $a = [string](M2 'SkuDeFila' $ws18 15); $b = [string]$ws18.Cells.Item(15, 4).Value2
                $c = [string](M2 'SkuDeFila' $ws10 ($hdr + 1)); $e = [string]$ws10.Cells.Item($hdr + 1, $lm.ListColumns.Item('SKU').Range.Column).Value2
                $z = [string](M2 'SkuDeFila' $ws10 3)
                Check ($a -eq $b -and $a -ne '' -and $c -eq $e -and $z -eq '') ('SkuDeFila: pedido={0}, bitacora={1}, encabezado=(vacio)' -f $a, $c) `
                    ("SkuDeFila: pedido '{0}' vs '{1}', bitacora '{2}' vs '{3}', encabezado '{4}'" -f $a, $b, $c, $e, $z)
                [void](M1 'AbrirKardex' $skuB)
                Check ($xl.ActiveSheet.Name -eq '17_KARDEX' -and [string](V 'kxSKU') -eq $skuB) ('AbrirKardex: consulta de ' + $skuB) `
                    ("AbrirKardex: hoja={0} kxSKU='{1}'" -f $xl.ActiveSheet.Name, (V 'kxSKU'))
                [void](M0 'RegistrarDesdeKardex')
                Check ($xl.ActiveSheet.Name -eq '12_REGISTRO' -and ([string](V 'frmProducto')).StartsWith($skuB)) `
                    'RegistrarDesdeKardex: formulario con el producto consultado' ("RegistrarDesdeKardex: producto='{0}'" -f (V 'frmProducto'))
                [void](M0 'LimpiarFormulario')
            }

            Prueba 'Toma fisica' {
                $lc = $wb.Worksheets.Item('13_CONTEO').ListObjects.Item('tblConteo')
                $skus = $lc.ListColumns.Item('SKU').DataBodyRange.Value2
                $conteo = $lc.ListColumns.Item('Conteo').DataBodyRange
                $stB = StockDe $skuB; $stC = StockDe $skuC
                for ($i = 1; $i -le $skus.GetLength(0); $i++) {
                    if ([string]$skus[$i, 1] -eq $skuB) { $conteo.Cells.Item($i, 1).Formula = [string]($stB + 2) }
                    if ([string]$skus[$i, 1] -eq $skuC) { $conteo.Cells.Item($i, 1).Formula = [string]($stC - 1) }
                }
                (N 'ctResponsable').Value2 = $resp
                $antes = [int](V 'kpiRegistros')
                [void](M0 'GenerarAjustesConteo')
                $id = [int](V 'kpiUltimoID')
                $okAj = ([int](V 'kpiRegistros') -eq $antes + 2) -and (Near (StockDe $skuB) ($stB + 2)) -and (Near (StockDe $skuC) ($stC - 1))
                Check $okAj ('GenerarAjustesConteo: AJUSTE (+) 2 de {0} y AJUSTE (-) 1 de {1}' -f $skuB, $skuC) `
                    ("Ajustes del conteo: registros {0}->{1}; aviso '{2}'" -f $antes, (V 'kpiRegistros'), (M0 'LeerUltimoAviso'))
                $e1 = [string]$ws10.Cells.Item($hdr + $id - 1, $cEst).Value2; $e2 = [string]$ws10.Cells.Item($hdr + $id, $cEst).Value2
                Check ($e1.StartsWith($CHECK) -and $e2.StartsWith($CHECK) -and $ws10.Cells.Item($hdr + $id, $cFec).Locked -and `
                        [int](V 'kpiConteoContados') -eq 0 -and [int](V 'cfgSelladoHasta') -eq $id) `
                    'Ajustes verificados y sellados; el conteo quedo vacio' `
                    ("Ajustes: estados '{0}' / '{1}', contados={2}, sellado={3}" -f $e1, $e2, (V 'kpiConteoContados'), (V 'cfgSelladoHasta'))
            }
        }
        else {
            Prueba 'Libro limpio' {
                [void](M0 'RegistrarMovimiento')
                Check ([int](V 'kpiRegistros') -eq 0 -and ([string](V 'frmMensaje')).StartsWith($CROSS)) `
                    'Formulario vacio: no registra y lo explica' ("Formulario vacio: mensaje '{0}'" -f (V 'frmMensaje'))
                Check ([int](M0 'FilaLibre') -eq $hdr + 1) 'La primera fila libre de la bitacora es la primera de la tabla' `
                    ('FilaLibre=' + (M0 'FilaLibre'))
                [void](M0 'LimpiarFormulario')
            }
        }

        Prueba 'Guardado y cierre' {
            $wb.Save()
            $av = Avisos-Visibles $wb
            Check (($av | Where-Object { $_ -ne 0 }).Count -eq 0) 'Tras guardar, el aviso "sin macros" vuelve a ocultarse (AfterSave)' `
                ('Avisos tras guardar: ' + ($av -join ','))
            $wb.Worksheets.Item('00_PORTADA').Activate()
            $ocultaEnPortada = -not $xl.DisplayFormulaBar
            $wb.Worksheets.Item('15_STOCK').Activate()
            $visibleEnStock = $xl.DisplayFormulaBar
            $wb.Worksheets.Item('00_PORTADA').Activate()
            $wb.Close($false)
            $script:wb = $null
            Check ($ocultaEnPortada -and $visibleEnStock -and $xl.DisplayFormulaBar) `
                'Modo app: barra oculta en la portada, visible en otras hojas y restaurada al cerrar' `
                ('Barra de formulas: portada oculta={0} stock visible={1} tras cerrar={2}' -f $ocultaEnPortada, $visibleEnStock, $xl.DisplayFormulaBar)
        }

        Prueba 'Reapertura sin macros' {
            $xl.AutomationSecurity = 3
            $script:wb = $xl.Workbooks.Open($tmp, 0, $true)
            $av = Avisos-Visibles $wb
            $sell = [int](V 'cfgSelladoHasta'); $ult = [int](V 'kpiUltimoID')
            Check ($av.Count -gt 0 -and ($av | Where-Object { $_ -ne -1 }).Count -eq 0 -and $sell -eq $ult) `
                ('Archivo guardado: aviso "sin macros" visible y bitacora sellada hasta el ID {0}' -f $sell) `
                ('Archivo guardado: avisos={0} sellado={1} ultimo={2}' -f ($av -join ','), $sell, $ult)
        }
    }
    finally {
        if ($script:wb) { try { $script:wb.Close($false) } catch { } ; $script:wb = $null }
        $xl.AutomationSecurity = 1
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- principal
$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$barOriginal = $xl.DisplayFormulaBar
$script:wb = $null
try {
    Write-Output ("M-INV edicion Plus :: Excel " + $xl.Version + " build " + $xl.Build)
    foreach ($libro in $libros) {
        if ($Solo -and $Solo -ne $libro.Id) { continue }
        $base = Join-Path $root $libro.Base
        $destino = Join-Path $root $libro.Destino
        if (-not (Test-Path $base)) { throw ("No existe {0}: ejecute primero tools\build_minv.py" -f $libro.Base) }
        Write-Output ("== {0}: {1} -> {2}" -f $libro.Id, $libro.Base, $libro.Destino)

        # 1. Instalar el VBA y guardar como .xlsm (sin eventos)
        $xl.EnableEvents = $false
        $xl.AutomationSecurity = 1
        $script:wb = $xl.Workbooks.Open($base, 0, $true)
        $resumen = Instalar-Vba $wb $libro.Clave
        if (Test-Path $destino) { Remove-Item $destino -Force }
        $wb.SaveAs($destino, 52)
        $wb.Close($false); $script:wb = $null
        Ok ("VBA instalado ({0})" -f $resumen)

        # 2. Primera apertura con macros: proteccion para macros, sellado y guardado
        $xl.EnableEvents = $true
        $script:wb = $xl.Workbooks.Open($destino)
        $script:pre = "'" + $wb.Name + "'!"
        [void](M1 'ActivarModoSilencioso' $true)
        [void](M0 'SellarBitacora')
        $wb.Worksheets.Item('00_PORTADA').Activate()
        $wb.Save()
        $sell = V 'cfgSelladoHasta'
        $wb.Close($false); $script:wb = $null
        Ok ("Generado {0} (bitacora sellada hasta el ID {1})" -f $libro.Destino, $sell)

        # 3. Pruebas sobre una copia temporal
        if (-not $SinPruebas) { Probar-Libro $libro $destino }
    }
}
catch {
    $script:fallas++
    Write-Output ("[ERROR] " + $_.Exception.Message)
    Write-Output $_.InvocationInfo.PositionMessage
}
finally {
    if ($script:wb) { try { $script:wb.Close($false) } catch { } }
    $xl.EnableEvents = $true
    $xl.DisplayFormulaBar = $barOriginal
    $xl.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
}
if ($script:fallas -gt 0) { Write-Output ("RESULTADO: {0} falla(s)" -f $script:fallas); exit 1 }
Write-Output "RESULTADO: sin fallas"

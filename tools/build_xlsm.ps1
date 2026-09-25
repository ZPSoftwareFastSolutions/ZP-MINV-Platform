<#
.SYNOPSIS
    M-INV V1.1 - Genera la variante .xlsm (modo app con VBA) a partir de un .xlsx generado y la prueba.

.DESCRIPTION
    1. Abre el .xlsx, lo guarda como .xlsm (formato 52).
    2. Importa src/macros/modAppMode.bas y pega src/macros/ThisWorkbook.txt en el modulo ThisWorkbook.
    3. Prueba: reabre el .xlsm con macros habilitadas y comprueba que la barra de formulas
       se oculta en 00_PORTADA y reaparece en las demas hojas y al cerrar.
    Requiere: Excel con "Confiar en el acceso al modelo de objetos de proyectos de VBA" activado.
    Restaura siempre la barra de formulas del usuario al terminar.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build_xlsm.ps1 -Source src\M-INV_V1_Core.xlsx
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Target = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Source = (Resolve-Path $Source).Path
if (-not $Target) { $Target = [IO.Path]::ChangeExtension($Source, '.xlsm') }
$bas = Join-Path $root 'src\macros\modAppMode.bas'
$evt = Join-Path $root 'src\macros\ThisWorkbook.txt'
$fails = 0

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false
$xl.DisplayAlerts = $false
$barOriginal = $xl.DisplayFormulaBar
try {
    # --- 1 y 2: construir
    $wb = $xl.Workbooks.Open($Source, 0, $true)
    try { $vbp = $wb.VBProject; [void]$vbp.VBComponents.Count }
    catch { throw "Excel no permite acceder al proyecto VBA. Active 'Confiar en el acceso al modelo de objetos de proyectos de VBA'." }
    [void]$vbp.VBComponents.Import($bas)
    $cm = $vbp.VBComponents.Item($wb.CodeName).CodeModule
    if ($cm.CountOfLines -gt 0) { $cm.DeleteLines(1, $cm.CountOfLines) }
    $cm.AddFromString([IO.File]::ReadAllText($evt))
    if (Test-Path $Target) { Remove-Item $Target -Force }
    $wb.SaveAs($Target, 52)
    $wb.Close($false)
    Write-Output ("[OK]    Generado " + $Target)

    # --- 3: probar con eventos habilitados
    $xl.EnableEvents = $true
    $xl.AutomationSecurity = 1   # msoAutomationSecurityLow: habilita macros del archivo abierto por automatizacion
    $xl.DisplayFormulaBar = $true
    $wb = $xl.Workbooks.Open($Target)
    $checks = @(
        @('Al abrir (Workbook_Open -> portada)', $null, $false),
        @('En 10_MOVIMIENTOS', '10_MOVIMIENTOS', $true),
        @('En 15_STOCK', '15_STOCK', $true),
        @('De vuelta en 00_PORTADA', '00_PORTADA', $false)
    )
    foreach ($c in $checks) {
        if ($c[1]) { $wb.Worksheets.Item($c[1]).Activate() }
        $ok = ($xl.DisplayFormulaBar -eq $c[2]) -and ($c[1] -or $xl.ActiveSheet.Name -eq '00_PORTADA')
        $estado = if ($ok) { '[OK]   ' } else { $fails++; '[FALLA]' }
        Write-Output ("{0} {1}: hoja={2} barra de formulas visible={3}" -f $estado, $c[0], $xl.ActiveSheet.Name, $xl.DisplayFormulaBar)
    }
    $wb.Worksheets.Item('00_PORTADA').Activate()
    $wb.Close($false)
    $ok = $xl.DisplayFormulaBar -eq $true
    if (-not $ok) { $fails++ }
    Write-Output ("{0} Al cerrar (Workbook_BeforeClose): barra de formulas visible={1}" -f ($(if ($ok) { '[OK]   ' } else { '[FALLA]' })), $xl.DisplayFormulaBar)
}
finally {
    $xl.DisplayFormulaBar = $barOriginal
    $xl.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
}
if ($fails -gt 0) { Write-Output "RESULTADO: $fails falla(s)"; exit 1 }
Write-Output "RESULTADO: sin fallas"

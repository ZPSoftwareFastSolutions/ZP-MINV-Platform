Attribute VB_Name = "modAppMode"
'==============================================================================
' M-INV V1 - Modo aplicacion (OPCIONAL: requiere guardar el libro como .xlsm)
' Z&P Software Fast Solutions
'
' Oculta la barra de formulas mientras la portada (00_PORTADA) esta activa y la
' restaura al cambiar de hoja, de libro o al cerrar. La cuadricula y los
' encabezados de fila/columna ya vienen ocultos desde el .xlsx; este modulo
' completa el mandato UX, porque la barra de formulas es una opcion de la
' APLICACION y no puede guardarse en un .xlsx.
'
' Instalacion: ver src/macros/README.md
'==============================================================================
Option Explicit

Public Const HOJA_PORTADA As String = "00_PORTADA"

' Barra de formulas oculta solo en la portada.
Public Sub AplicarModoApp(ByVal hoja As Object)
    On Error Resume Next
    Application.DisplayFormulaBar = (hoja.Name <> HOJA_PORTADA)
End Sub

' Deja Excel como lo encontro el usuario.
Public Sub RestaurarModoExcel()
    On Error Resume Next
    Application.DisplayFormulaBar = True
End Sub

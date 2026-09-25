Attribute VB_Name = "modAppMode"
'==============================================================================
' M-INV V1.2 - Modo aplicación (edición Plus)
' Z&P Software Fast Solutions
'
' Oculta la barra de fórmulas mientras la portada (00_PORTADA) está activa y la
' restaura al cambiar de hoja, de libro o al cerrar. La cuadrícula y los
' encabezados de fila/columna ya vienen ocultos desde el generador; este módulo
' completa el mandato UX, porque la barra de fórmulas es una opción de la
' APLICACIÓN y no puede guardarse en un .xlsx.
'==============================================================================
Option Explicit

Public Const HOJA_PORTADA As String = "00_PORTADA"

' Barra de fórmulas oculta solo en la portada.
Public Sub AplicarModoApp(ByVal sh As Object)
    On Error Resume Next
    Application.DisplayFormulaBar = (sh.Name <> HOJA_PORTADA)
End Sub

' Deja Excel como lo encontró el usuario.
' Durante el cierre del libro Excel ignora la asignación directa de
' DisplayFormulaBar (probado en Microsoft 365), por eso primero se usa el
' comando de la cinta (Vista > Barra de fórmulas), que sí se aplica.
Public Sub RestaurarModoExcel()
    On Error Resume Next
    If Not Application.CommandBars.GetPressedMso("ViewFormulaBar") Then
        Application.CommandBars.ExecuteMso "ViewFormulaBar"
    End If
    Application.DisplayFormulaBar = True
End Sub

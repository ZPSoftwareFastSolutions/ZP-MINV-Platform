Attribute VB_Name = "modNavegacion"
'==============================================================================
' M-INV V1.2 - Atajos de doble clic y apertura de la consulta (edición Plus)
' Z&P Software Fast Solutions
'
' Los módulos de hoja (Hoja_*.txt) llaman a estas rutinas desde Worksheet_BeforeDoubleClick.
'==============================================================================
Option Explicit

' SKU de la fila indicada: busca el encabezado «SKU» en la parte superior de la hoja.
Public Function SkuDeFila(ByVal ws As Worksheet, ByVal fila As Long) As String
    Dim zona As Variant, r As Long, c As Long
    zona = ws.Range("A1:AD30").Value
    For r = 1 To 30
        For c = 1 To 30
            If VarType(zona(r, c)) = vbString Then
                If zona(r, c) = "SKU" Then
                    If fila > r Then SkuDeFila = Trim$(CStr(ws.Cells(fila, c).Value))
                    Exit Function
                End If
            End If
        Next c
    Next r
End Function

' Abre 17_KARDEX con el producto indicado.
Public Sub AbrirKardex(ByVal sku As String)
    Dim etiqueta As String
    On Error GoTo Falla
    etiqueta = CStr(DatoProducto(sku, "Etiqueta"))
    If Len(etiqueta) = 0 Then Exit Sub
    Hoja(HOJA_KARDEX).Activate
    Rng("kxCategoria").MergeArea.ClearContents
    Rng("kxBuscar").MergeArea.ClearContents
    Rng("kxProducto").Value = etiqueta
    Rng("kxProducto").Select
    Exit Sub
Falla:
    Avisar "No se pudo abrir la consulta del producto: " & Err.Description, vbExclamation
End Sub

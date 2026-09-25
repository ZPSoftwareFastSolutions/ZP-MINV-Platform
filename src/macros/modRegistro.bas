Attribute VB_Name = "modRegistro"
'==============================================================================
' M-INV V1.2 - Registro guiado y sellado de la bitácora (edición Plus)
' Z&P Software Fast Solutions
'
' 12_REGISTRO solo captura y valida (fórmulas frm* de 91_KPIS). Estas macros copian el
' movimiento a la siguiente fila libre de 10_MOVIMIENTOS, comprueban el Estado que calcula
' la propia bitácora y sellan la fila. La bitácora sigue siendo la única capa de escritura
' del inventario (regla R-02) y sus validaciones son las mismas con o sin macros.
'==============================================================================
Option Explicit

Private Const ENTRADAS As String = "Fecha|Tipo|Categoría|Producto|Cantidad|Documento|Responsable|Observaciones"
Private Const CAMPOS_TODOS As String = "frmTipo|frmFecha|frmCategoria|frmBuscar|frmProducto|frmCantidad|frmResponsable|frmDocumento|frmObservaciones"
' Tipo, categoría y responsable se conservan para registrar varios movimientos seguidos.
Private Const CAMPOS_POR_REGISTRO As String = "frmFecha|frmBuscar|frmProducto|frmCantidad|frmDocumento|frmObservaciones"

'------------------------------------------------------------------------------
' Botones del formulario
'------------------------------------------------------------------------------
' Botón «REGISTRAR MOVIMIENTO».
Public Sub RegistrarMovimiento()
    Dim fila As Long, estado As String, fecha As Date, id As Variant, resumen As String
    On Error GoTo Falla
    Application.Calculate
    If Not EsVerdadero(Rng("frmValido").Value) Then
        Mensaje CRUZ() & " No se registró: complete los campos marcados con " & PENDIENTE() & " o " & CRUZ() & _
            " en la VALIDACIÓN."
        Avisar "Aún no se puede registrar: revise la VALIDACIÓN (" & PENDIENTE() & " falta un dato, " & CRUZ() & _
            " dato con error).", vbExclamation
        Exit Sub
    End If
    fila = FilaLibre()
    If fila = 0 Then
        Mensaje CRUZ() & " La bitácora está llena."
        Avisar "La bitácora llegó a su capacidad máxima: es momento del cierre de período (vea la Guía).", vbCritical
        Exit Sub
    End If
    If IsDate(Rng("frmFecha").Value) Then fecha = CDate(Rng("frmFecha").Value) Else fecha = Date
    resumen = Rng("frmTipo").Value & " de " & Rng("frmCantidad").Value & " " & Rng("frmUnidad").Value & _
        " · " & SkuDe(CStr(Rng("frmProducto").Value))

    Application.ScreenUpdating = False
    EscribirFila fila, CStr(Rng("frmTipo").Value), fecha, CStr(Rng("frmProducto").Value), _
        CDbl(Rng("frmCantidad").Value), Rng("frmDocumento").Value, Rng("frmResponsable").Value, _
        Rng("frmObservaciones").Value
    Application.Calculate
    estado = EstadoFila(fila)
    If Left$(estado, 1) <> CHK() Then
        BorrarFila fila
        Application.Calculate
        Application.ScreenUpdating = True
        Mensaje CRUZ() & " No se registró: la bitácora respondió «" & estado & "»."
        Avisar "La bitácora rechazó el movimiento: " & estado, vbExclamation
        Exit Sub
    End If
    id = Bitacora().Parent.Cells(fila, ColHoja(Bitacora(), "ID")).Value
    SellarBitacora
    LimpiarCampos CAMPOS_POR_REGISTRO
    Application.ScreenUpdating = True
    Mensaje CHK() & " Movimiento #" & id & " registrado y sellado: " & resumen
    SeleccionarCampo "frmBuscar"
    Exit Sub
Falla:
    Application.ScreenUpdating = True
    Mensaje CRUZ() & " Error: " & Err.Description
    Avisar "No se pudo registrar el movimiento: " & Err.Description, vbCritical
End Sub

' Botón «Limpiar».
Public Sub LimpiarFormulario()
    On Error GoTo Falla
    LimpiarCampos CAMPOS_TODOS
    Mensaje ""
    SeleccionarCampo "frmTipo"
    Exit Sub
Falla:
    Avisar "No se pudo limpiar el formulario: " & Err.Description, vbExclamation
End Sub

'------------------------------------------------------------------------------
' Accesos directos al formulario
'------------------------------------------------------------------------------
' Doble clic en 16_ALERTAS o 18_PEDIDO: formulario listo para registrar la reposición.
Public Sub AbrirFormularioReposicion(ByVal sku As String)
    Dim etiqueta As String, resp As Variant, sugerido As Double
    On Error GoTo Falla
    etiqueta = CStr(DatoProducto(sku, "Etiqueta"))
    If Len(etiqueta) = 0 Then Exit Sub
    resp = Rng("frmResponsable").Value
    Hoja(HOJA_REG).Activate
    LimpiarCampos CAMPOS_TODOS
    Rng("frmTipo").Value = "ENTRADA"
    Rng("frmProducto").Value = etiqueta
    sugerido = CantidadSugerida(sku)
    If sugerido > 0 Then Rng("frmCantidad").Value = sugerido
    If Len(CStr(resp)) > 0 Then Rng("frmResponsable").Value = resp
    Rng("frmObservaciones").Value = "Reposición sugerida (" & CStr(DatoStock(sku, "Estado")) & ")"
    Mensaje "Reposición de " & sku & ": confirme la cantidad recibida y pulse REGISTRAR MOVIMIENTO."
    If Len(CStr(resp)) = 0 Then SeleccionarCampo "frmResponsable" Else SeleccionarCampo "frmCantidad"
    Exit Sub
Falla:
    Avisar "No se pudo preparar la reposición: " & Err.Description, vbExclamation
End Sub

' Botón de 17_KARDEX: registrar un movimiento del producto consultado.
Public Sub RegistrarDesdeKardex()
    Dim etiqueta As String, resp As Variant, tipo As Variant
    On Error GoTo Falla
    etiqueta = CStr(Rng("kxProducto").Value)
    If Len(etiqueta) = 0 Or Len(CStr(DatoProducto(SkuDe(etiqueta), "Etiqueta"))) = 0 Then
        Avisar "Primero elija un producto en la consulta.", vbInformation
        Exit Sub
    End If
    resp = Rng("frmResponsable").Value
    tipo = Rng("frmTipo").Value
    Hoja(HOJA_REG).Activate
    LimpiarCampos CAMPOS_TODOS
    If Len(CStr(tipo)) > 0 Then Rng("frmTipo").Value = tipo
    If Len(CStr(resp)) > 0 Then Rng("frmResponsable").Value = resp
    Rng("frmProducto").Value = etiqueta
    Mensaje "Producto " & SkuDe(etiqueta) & " elegido desde la consulta: complete el tipo y la cantidad."
    If Len(CStr(tipo)) = 0 Then SeleccionarCampo "frmTipo" Else SeleccionarCampo "frmCantidad"
    Exit Sub
Falla:
    Avisar "No se pudo abrir el formulario: " & Err.Description, vbExclamation
End Sub

'------------------------------------------------------------------------------
' Sellado de la bitácora
'------------------------------------------------------------------------------
' Bloquea todas las filas registradas correctamente (Estado «Registrado») y actualiza cfgSelladoHasta
' (la bitácora las muestra en gris). Se ejecuta al registrar desde el formulario, al generar
' los ajustes del conteo y antes de cada guardado.
Public Sub SellarBitacora()
    Dim lo As ListObject, datos As Variant, i As Long, n As Long, cId As Long, cEst As Long
    Dim inicio As Long, maxId As Double, sellar As Boolean
    Set lo = Bitacora()
    datos = lo.DataBodyRange.Value
    n = UBound(datos, 1)
    cId = lo.ListColumns("ID").Index
    cEst = lo.ListColumns("Estado").Index
    maxId = Num(Rng("cfgSelladoHasta").Value)
    For i = 1 To n + 1
        sellar = False
        If i <= n Then
            If Num(datos(i, cId)) > 0 Then sellar = (Left$(CStr(datos(i, cEst)), 1) = CHK())
        End If
        If sellar Then
            If inicio = 0 Then inicio = i
            If Num(datos(i, cId)) > maxId Then maxId = Num(datos(i, cId))
        ElseIf inicio > 0 Then
            lo.DataBodyRange.Rows(inicio).Resize(i - inicio).Locked = True
            inicio = 0
        End If
    Next i
    If maxId <> Num(Rng("cfgSelladoHasta").Value) Then Rng("cfgSelladoHasta").Value = maxId
End Sub

'------------------------------------------------------------------------------
' Escritura en la bitácora (compartida con modConteo)
'------------------------------------------------------------------------------
' Fila (de hoja) libre al final de la bitácora; 0 si está llena.
Public Function FilaLibre() As Long
    Dim lo As ListObject, r As Long, primera As Long, ultima As Long
    Set lo = Bitacora()
    primera = lo.DataBodyRange.Row
    ultima = primera + lo.ListRows.Count - 1
    r = CLng(Num(Rng("kpiFilaLibreMov").Value))
    If r < primera Or r > ultima Then Exit Function
    If Not FilaVacia(r) Then Exit Function
    FilaLibre = r
End Function

' Última fila (de hoja) de la bitácora.
Public Function UltimaFilaBitacora() As Long
    With Bitacora()
        UltimaFilaBitacora = .DataBodyRange.Row + .ListRows.Count - 1
    End With
End Function

Public Function FilaVacia(ByVal fila As Long) As Boolean
    Dim lo As ListObject, nombre As Variant
    Set lo = Bitacora()
    For Each nombre In Split(ENTRADAS, "|")
        If Len(CStr(lo.Parent.Cells(fila, ColHoja(lo, CStr(nombre))).Value)) > 0 Then Exit Function
    Next nombre
    FilaVacia = True
End Function

Public Sub EscribirFila(ByVal fila As Long, ByVal tipo As String, ByVal fecha As Date, ByVal producto As String, _
                       ByVal cantidad As Double, ByVal documento As Variant, ByVal responsable As Variant, _
                       ByVal observaciones As Variant)
    Dim lo As ListObject, ws As Worksheet
    Set lo = Bitacora()
    Set ws = lo.Parent
    ws.Cells(fila, ColHoja(lo, "Fecha")).Value = fecha
    ws.Cells(fila, ColHoja(lo, "Tipo")).Value = tipo
    ws.Cells(fila, ColHoja(lo, "Categoría")).Value = TextoSeguro(CStr(DatoProducto(SkuDe(producto), "Categoría")))
    ws.Cells(fila, ColHoja(lo, "Producto")).Value = TextoSeguro(producto)
    ws.Cells(fila, ColHoja(lo, "Cantidad")).Value = cantidad
    If Len(CStr(documento)) > 0 Then ws.Cells(fila, ColHoja(lo, "Documento")).Value = TextoSeguro(documento)
    ws.Cells(fila, ColHoja(lo, "Responsable")).Value = TextoSeguro(responsable)
    If Len(CStr(observaciones)) > 0 Then ws.Cells(fila, ColHoja(lo, "Observaciones")).Value = TextoSeguro(observaciones)
End Sub

Public Function EstadoFila(ByVal fila As Long) As String
    EstadoFila = CStr(Bitacora().Parent.Cells(fila, ColHoja(Bitacora(), "Estado")).Value)
End Function

' Cantidad para volver al máximo (o a 2 × mínimo si no hay máximo): la misma regla de 16_ALERTAS.
Public Function CantidadSugerida(ByVal sku As String) As Double
    Dim st As Double, tope As Double
    st = Num(DatoStock(sku, "StockActual"))
    tope = Num(DatoStock(sku, "StockMax"))
    If tope <= 0 Then tope = 2 * Num(DatoStock(sku, "StockMin"))
    If st < 0 Then st = 0
    If tope > st Then CantidadSugerida = tope - st
End Function

Private Sub BorrarFila(ByVal fila As Long)
    Dim lo As ListObject, nombre As Variant
    Set lo = Bitacora()
    For Each nombre In Split(ENTRADAS, "|")
        lo.Parent.Cells(fila, ColHoja(lo, CStr(nombre))).ClearContents
    Next nombre
End Sub

'------------------------------------------------------------------------------
' Utilidades del formulario
'------------------------------------------------------------------------------
Private Sub LimpiarCampos(ByVal lista As String)
    Dim nombre As Variant
    For Each nombre In Split(lista, "|")
        Rng(CStr(nombre)).MergeArea.ClearContents
    Next nombre
End Sub

Private Sub Mensaje(ByVal texto As String)
    Rng("frmMensaje").Value = TextoSeguro(texto)
End Sub

Private Sub SeleccionarCampo(ByVal nombre As String)
    On Error Resume Next
    If ActiveSheet.Name = HOJA_REG Then Rng(nombre).Select
End Sub

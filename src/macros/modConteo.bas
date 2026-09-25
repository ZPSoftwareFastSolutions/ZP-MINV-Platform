Attribute VB_Name = "modConteo"
'==============================================================================
' M-INV V1.2 - Toma física: de las diferencias del conteo a los ajustes (edición Plus)
' Z&P Software Fast Solutions
'
' El conteo es un comando: nunca modifica el stock. Esta macro registra en la bitácora un
' AJUSTE (+) o AJUSTE (-) por cada producto contado con diferencia, lo sella y vacía la
' columna Conteo. Sin macros, la columna AjusteSugerido indica qué registrar a mano.
'==============================================================================
Option Explicit

' Botón «Generar ajustes» de 13_CONTEO.
Public Sub GenerarAjustesConteo()
    Dim lo As ListObject, datos As Variant, i As Long, n As Long
    Dim cSku As Long, cCont As Long, cDif As Long, cSis As Long
    Dim fecha As Date, resp As String, doc As String, sku As String, dif As Double
    Dim contados As Long, ajustes As Long, fila As Long, primera As Long, k As Long
    Dim estado As String, errores As String
    On Error GoTo Falla
    Set lo = Hoja(HOJA_CONTEO).ListObjects("tblConteo")
    Application.Calculate

    resp = CStr(Rng("ctResponsable").Value)
    If Len(resp) = 0 Then
        Avisar "Indique el Responsable del conteo (arriba a la derecha) antes de generar los ajustes.", vbExclamation
        Exit Sub
    End If
    If IsDate(Rng("ctFecha").Value) Then fecha = CDate(Rng("ctFecha").Value) Else fecha = Date
    If fecha > Date Then
        Avisar "La fecha del conteo no puede ser futura.", vbExclamation
        Exit Sub
    End If

    datos = lo.DataBodyRange.Value
    n = UBound(datos, 1)
    cSku = lo.ListColumns("SKU").Index
    cCont = lo.ListColumns("Conteo").Index
    cDif = lo.ListColumns("Diferencia").Index
    cSis = lo.ListColumns("StockSistema").Index
    For i = 1 To n
        If Len(CStr(datos(i, cSku))) > 0 And Len(CStr(datos(i, cCont))) > 0 Then
            contados = contados + 1
            If Num(datos(i, cDif)) <> 0 Then ajustes = ajustes + 1
        End If
    Next i
    If contados = 0 Then
        Avisar "No hay cantidades contadas: escriba lo que contó en la columna Conteo y vuelva a intentarlo.", vbInformation
        Exit Sub
    End If
    If ajustes = 0 Then
        lo.ListColumns("Conteo").DataBodyRange.ClearContents
        Avisar CHK() & " Conteo cerrado: los " & contados & " producto(s) contados coinciden con el sistema. " & _
            "No se requieren ajustes.", vbInformation
        Exit Sub
    End If

    fila = FilaLibre()
    If fila = 0 Or fila + ajustes - 1 > UltimaFilaBitacora() Then
        Avisar "La bitácora no tiene espacio para " & ajustes & " ajuste(s): es momento del cierre de período.", vbCritical
        Exit Sub
    End If
    If Not Confirmar("Se registrarán " & ajustes & " ajuste(s) en la bitácora con fecha " & Format$(fecha, "dd/mm/yyyy") & _
            " (valor neto de las diferencias: $ " & Format$(Num(Rng("kpiConteoValor").Value), "#,##0") & ")." & _
            vbCrLf & vbCrLf & "Los registros quedarán sellados y el conteo se vaciará. ¿Desea continuar?") Then Exit Sub

    Application.ScreenUpdating = False
    doc = "CF-" & Format$(fecha, "yyyymmdd")
    primera = fila
    For i = 1 To n
        sku = CStr(datos(i, cSku))
        If Len(sku) > 0 And Len(CStr(datos(i, cCont))) > 0 Then
            dif = Num(datos(i, cDif))
            If dif <> 0 Then
                EscribirFila fila, IIf(dif > 0, "AJUSTE (+)", "AJUSTE (-)"), fecha, CStr(DatoProducto(sku, "Etiqueta")), _
                    Abs(dif), doc, resp, "Conteo físico del " & Format$(fecha, "dd/mm/yyyy") & ": sistema " & _
                    datos(i, cSis) & ", contado " & datos(i, cCont)
                fila = fila + 1
            End If
        End If
    Next i
    Application.Calculate
    For k = primera To fila - 1
        estado = EstadoFila(k)
        If Left$(estado, 1) <> CHK() Then errores = errores & vbCrLf & "  Fila " & k & ": " & estado
    Next k
    SellarBitacora
    lo.ListColumns("Conteo").DataBodyRange.ClearContents
    Application.ScreenUpdating = True

    If Len(errores) > 0 Then
        Avisar "Se registraron " & (fila - primera) & " ajuste(s), pero estos quedaron por revisar en la bitácora:" & _
            errores, vbExclamation
    Else
        Avisar CHK() & " Conteo cerrado: " & contados & " producto(s) contados y " & (fila - primera) & _
            " ajuste(s) registrados y sellados en la bitácora.", vbInformation
    End If
    Exit Sub
Falla:
    Application.ScreenUpdating = True
    Avisar "No se pudieron generar los ajustes: " & Err.Description, vbCritical
End Sub

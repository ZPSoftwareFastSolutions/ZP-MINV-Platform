Attribute VB_Name = "modConfig"
'==============================================================================
' M-INV V1.2 - Configuración y utilidades compartidas (edición Plus)
' Z&P Software Fast Solutions
'
' Fuente en UTF-8. tools/build_xlsm.ps1 instala el código con AddFromString y el
' Editor de VBA lo guarda en Windows-1252; los símbolos de estado, que no existen
' en ese juego de caracteres, se generan con ChrW (funciones CHK, CRUZ, PENDIENTE).
'==============================================================================
Option Explicit

' tools/build_xlsm.ps1 reemplaza el marcador por la contraseña de protección del libro.
Public Const CLAVE As String = "__MINV_PASSWORD__"

Public Const HOJA_CONFIG As String = "01_CONFIG"
Public Const HOJA_PROD As String = "05_PRODUCTOS"
Public Const HOJA_MOV As String = "10_MOVIMIENTOS"
Public Const HOJA_REG As String = "12_REGISTRO"
Public Const HOJA_CONTEO As String = "13_CONTEO"
Public Const HOJA_STOCK As String = "15_STOCK"
Public Const HOJA_KARDEX As String = "17_KARDEX"

' Las pruebas automáticas lo activan para que los avisos no abran cuadros de diálogo.
Public ModoSilencioso As Boolean
Public UltimoAviso As String

Public Sub ActivarModoSilencioso(Optional ByVal activo As Boolean = True)
    ModoSilencioso = activo
End Sub

Public Function LeerUltimoAviso() As String
    LeerUltimoAviso = UltimoAviso
End Function

Public Sub Avisar(ByVal texto As String, Optional ByVal icono As VbMsgBoxStyle = vbInformation)
    UltimoAviso = texto
    If Not ModoSilencioso Then MsgBox texto, icono, "M-INV"
End Sub

' Pregunta Sí/No con «No» como botón predeterminado (las acciones confirmadas no se deshacen).
Public Function Confirmar(ByVal texto As String) As Boolean
    UltimoAviso = texto
    If ModoSilencioso Then
        Confirmar = True
    Else
        Confirmar = (MsgBox(texto, vbQuestion + vbYesNo + vbDefaultButton2, "M-INV") = vbYes)
    End If
End Function

'------------------------------------------------------------------------------
' Símbolos de estado (iguales a los de las fórmulas del libro)
'------------------------------------------------------------------------------
Public Function CHK() As String
    CHK = ChrW(&H2714)
End Function

Public Function CRUZ() As String
    CRUZ = ChrW(&H2716)
End Function

Public Function PENDIENTE() As String
    PENDIENTE = ChrW(&H25CB)
End Function

Public Function SEPARADOR() As String
    SEPARADOR = " " & ChrW(&HB7) & " "
End Function

'------------------------------------------------------------------------------
' Acceso a hojas, nombres y tablas
'------------------------------------------------------------------------------
Public Function Hoja(ByVal nombre As String) As Worksheet
    Set Hoja = ThisWorkbook.Worksheets(nombre)
End Function

Public Function Rng(ByVal nombre As String) As Range
    Set Rng = ThisWorkbook.Names(nombre).RefersToRange
End Function

Public Function Bitacora() As ListObject
    Set Bitacora = Hoja(HOJA_MOV).ListObjects("tblMovimientos")
End Function

' Columna (de hoja) de una columna de tabla.
Public Function ColHoja(ByVal lo As ListObject, ByVal columna As String) As Long
    ColHoja = lo.ListColumns(columna).Range.Column
End Function

Public Function Num(ByVal v As Variant) As Double
    If IsError(v) Then Exit Function
    If IsNumeric(v) Then
        If Len(CStr(v)) > 0 Then Num = CDbl(v)
    End If
End Function

Public Function EsVerdadero(ByVal v As Variant) As Boolean
    If VarType(v) = vbBoolean Then EsVerdadero = v
End Function

' Texto que Excel no debe interpretar como fórmula al escribirlo en una celda.
Public Function TextoSeguro(ByVal v As Variant) As Variant
    TextoSeguro = v
    If VarType(v) = vbString Then
        If Len(v) > 0 Then
            If InStr(1, "=+-@", Left$(v, 1)) > 0 Then TextoSeguro = "'" & v
        End If
    End If
End Function

'------------------------------------------------------------------------------
' Protección
'------------------------------------------------------------------------------
' UserInterfaceOnly no se guarda en el archivo: se reaplica en cada apertura para que las
' macros escriban y sellen mientras el usuario sigue viendo las hojas protegidas. Solo se
' reprotegen las hojas que las macros modifican, con los mismos permisos del generador.
Public Sub ProtegerParaMacros()
    Dim nombre As Variant
    For Each nombre In Array(HOJA_MOV, HOJA_CONTEO)
        With Hoja(CStr(nombre))
            .Protect Password:=CLAVE, UserInterfaceOnly:=True, AllowFormattingColumns:=True, AllowFiltering:=True
            .EnableSelection = xlNoRestrictions
        End With
    Next nombre
    With Hoja(HOJA_CONFIG)
        .Protect Password:=CLAVE, UserInterfaceOnly:=True
        .EnableSelection = xlNoRestrictions
    End With
    With Hoja(HOJA_REG)
        .Protect Password:=CLAVE, UserInterfaceOnly:=True
        .EnableSelection = xlUnlockedCells
    End With
End Sub

' El aviso «sin macros» del formulario se guarda visible (lo ve quien abre el libro con las
' macros deshabilitadas) y se oculta mientras las macros están activas.
Public Sub MostrarAvisosSinMacros(ByVal mostrar As Boolean)
    Dim s As Shape
    For Each s In Hoja(HOJA_REG).Shapes
        If Left$(s.AlternativeText, 16) = "Aviso sin macros" Then s.Visible = mostrar
    Next s
End Sub

'------------------------------------------------------------------------------
' Catálogo y stock
'------------------------------------------------------------------------------
' SKU de una etiqueta «SKU · Producto».
Public Function SkuDe(ByVal etiqueta As String) As String
    Dim p As Long
    p = InStr(1, etiqueta, SEPARADOR())
    If p > 0 Then
        SkuDe = Trim$(Left$(etiqueta, p - 1))
    Else
        SkuDe = Trim$(etiqueta)
    End If
End Function

' Valor de una columna de tblProductos para un SKU ("" si no existe).
Public Function DatoProducto(ByVal sku As String, ByVal columna As String) As Variant
    DatoProducto = BuscarPorSku(Hoja(HOJA_PROD).ListObjects("tblProductos"), sku, columna)
End Function

' Valor de una columna de tblStock para un SKU ("" si no existe).
Public Function DatoStock(ByVal sku As String, ByVal columna As String) As Variant
    DatoStock = BuscarPorSku(Hoja(HOJA_STOCK).ListObjects("tblStock"), sku, columna)
End Function

Private Function BuscarPorSku(ByVal lo As ListObject, ByVal sku As String, ByVal columna As String) As Variant
    Dim r As Variant
    BuscarPorSku = ""
    If Len(sku) = 0 Then Exit Function
    r = Application.Match(sku, lo.ListColumns("SKU").DataBodyRange, 0)
    If Not IsError(r) Then BuscarPorSku = lo.ListColumns(columna).DataBodyRange.Cells(CLng(r), 1).Value
End Function

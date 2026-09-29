using System.Windows;

namespace MINV.DesktopClient.Services;

/// <summary>Cuadros de diálogo de archivos de Windows.</summary>
public static class FileDialogs
{
    /// <summary>Pide dónde guardar un CSV (null si el usuario cancela). V7: lo usa <see cref="DialogService.AskCsvPath"/>, que las
    /// pruebas reemplazan para no abrir el cuadro del sistema.</summary>
    public static string? SaveCsv(string suggestedName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar a Excel (CSV)",
            FileName = suggestedName,
            DefaultExt = ".csv",
            Filter = "Archivo CSV para Excel (*.csv)|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

/// <summary>Portapapeles con reintento (otra aplicación puede tenerlo abierto un instante).</summary>
public static class ClipboardText
{
    public static bool TrySet(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60);
            }
        }
        return false;
    }
}

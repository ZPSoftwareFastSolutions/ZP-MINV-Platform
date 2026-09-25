using System.IO.Ports;
using System.Text;
using MINV.Application.Abstractions;

namespace MINV.Hardware.Scanners;

/// <summary>Lector de códigos de barras por puerto serie (RS-232 o USB-CDC): cada lectura termina en CR o LF.</summary>
public sealed class SerialBarcodeScanner(string portName, int baudRate = 9600) : IBarcodeScanner
{
    private readonly StringBuilder _buffer = new();
    private SerialPort? _port;

    public event EventHandler<BarcodeScannedEventArgs>? Scanned;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _port = new SerialPort(portName, baudRate) { Encoding = Encoding.ASCII, NewLine = "\r" };
        _port.DataReceived += (_, _) => Feed(_port.ReadExisting());
        _port.Open();
        return Task.CompletedTask;
    }

    /// <summary>Procesa los caracteres recibidos (público para poder probarlo sin hardware).</summary>
    public void Feed(string chunk)
    {
        foreach (var ch in chunk)
        {
            if (ch is '\r' or '\n')
            {
                Emit();
            }
            else if (!char.IsControl(ch))
            {
                _buffer.Append(ch);
            }
        }
    }

    private void Emit()
    {
        if (_buffer.Length == 0)
        {
            return;
        }
        var code = _buffer.ToString().Trim();
        _buffer.Clear();
        Scanned?.Invoke(this, new BarcodeScannedEventArgs(code, DateTimeOffset.UtcNow));
    }

    public ValueTask DisposeAsync()
    {
        _port?.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Lector en modo teclado (el más común en USB-HID): distingue una lectura del tecleo humano por la velocidad (todas
/// las teclas llegan en pocos milisegundos) y la tecla Enter final. La interfaz le pasa cada tecla con su instante.
/// </summary>
public sealed class KeyboardWedgeDetector(int minLength = 4, int maxMillisecondsBetweenKeys = 35)
{
    private readonly StringBuilder _buffer = new();
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public event EventHandler<BarcodeScannedEventArgs>? Scanned;

    /// <summary>Devuelve true si la tecla forma parte de una lectura (la interfaz puede entonces ignorarla).</summary>
    public bool OnKey(char key, DateTimeOffset at)
    {
        var fast = (at - _last).TotalMilliseconds <= maxMillisecondsBetweenKeys;
        _last = at;
        if (key is '\r' or '\n')
        {
            var isScan = _buffer.Length >= minLength;
            if (isScan)
            {
                Scanned?.Invoke(this, new BarcodeScannedEventArgs(_buffer.ToString(), at));
            }
            _buffer.Clear();
            return isScan;
        }
        if (!fast)
        {
            _buffer.Clear();
        }
        _buffer.Append(key);
        return _buffer.Length > 1;
    }
}

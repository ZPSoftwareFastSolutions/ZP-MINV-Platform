using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MINV.Infrastructure.Importing.V21;

/// <summary>
/// Lector mínimo de libros .xlsx (Office Open XML) sin dependencias: lee las tablas de Excel (ListObjects) y los nombres
/// definidos de una celda con sus valores en caché (lo que muestra Excel). Suficiente para migrar el libro colaborativo
/// de la V2.1, cuyas tablas son la fuente de verdad (tblUsuarios, tblProductos, tblEntradas…).
/// </summary>
public sealed partial class XlsxTableReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly Dictionary<string, XlsxTable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object?> _names = new(StringComparer.OrdinalIgnoreCase);

    private XlsxTableReader()
    {
    }

    public IReadOnlyDictionary<string, XlsxTable> Tables => _tables;

    public static XlsxTableReader Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Open(stream);
    }

    public static XlsxTableReader Open(Stream stream)
    {
        var reader = new XlsxTableReader();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var shared = ReadSharedStrings(zip);
        var workbook = Load(zip, "xl/workbook.xml");
        var wbRels = Relationships(zip, "xl/_rels/workbook.xml.rels", "xl/");
        var sheetPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Root!.Element(Main + "sheets")!.Elements(Main + "sheet"))
        {
            var rid = (string)sheet.Attribute(Rel + "id")!;
            sheetPaths[(string)sheet.Attribute("name")!] = wbRels[rid];
        }
        var cellCache = new Dictionary<string, Dictionary<(int Row, int Col), object?>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<(int, int), object?> Cells(string sheetPath)
        {
            if (!cellCache.TryGetValue(sheetPath, out var cells))
            {
                cells = ReadCells(Load(zip, sheetPath), shared);
                cellCache[sheetPath] = cells;
            }
            return cells;
        }

        foreach (var (_, sheetPath) in sheetPaths)
        {
            var dir = sheetPath[..(sheetPath.LastIndexOf('/') + 1)];
            var relsPath = dir + "_rels/" + sheetPath[(sheetPath.LastIndexOf('/') + 1)..] + ".rels";
            if (zip.GetEntry(relsPath) is null)
            {
                continue;
            }
            var sheetXml = Load(zip, sheetPath);
            var parts = sheetXml.Root!.Element(Main + "tableParts")?.Elements(Main + "tablePart").ToList() ?? [];
            if (parts.Count == 0)
            {
                continue;
            }
            var rels = Relationships(zip, relsPath, dir);
            foreach (var part in parts)
            {
                var tableXml = Load(zip, rels[(string)part.Attribute(Rel + "id")!]).Root!;
                var name = (string)tableXml.Attribute("displayName")! ?? (string)tableXml.Attribute("name")!;
                var (r0, c0, r1, c1) = ParseRange((string)tableXml.Attribute("ref")!);
                var headers = tableXml.Element(Main + "tableColumns")!.Elements(Main + "tableColumn")
                    .Select(tc => (string)tc.Attribute("name")!).ToList();
                var cells = Cells(sheetPath);
                var rows = new List<IReadOnlyDictionary<string, object?>>();
                for (var r = r0 + 1; r <= r1; r++)
                {
                    var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (var c = c0; c <= c1 && c - c0 < headers.Count; c++)
                    {
                        row[headers[c - c0]] = cells.GetValueOrDefault((r, c));
                    }
                    rows.Add(row);
                }
                reader._tables[name] = new XlsxTable(name, headers, rows);
            }
        }

        foreach (var dn in workbook.Root!.Element(Main + "definedNames")?.Elements(Main + "definedName") ?? [])
        {
            var m = CellRef().Match(dn.Value.Replace("$", string.Empty, StringComparison.Ordinal));
            if (!m.Success || !sheetPaths.TryGetValue(m.Groups["sheet"].Value.Trim('\''), out var path))
            {
                continue;
            }
            var (row, col) = ParseCell(m.Groups["cell"].Value);
            reader._names[(string)dn.Attribute("name")!] = Cells(path).GetValueOrDefault((row, col));
        }
        return reader;
    }

    public XlsxTable Table(string name) =>
        _tables.TryGetValue(name, out var t) ? t : throw new InvalidDataException($"El libro no tiene la tabla {name}: ¿es un libro M-INV V2.1?");

    public bool HasTable(string name) => _tables.ContainsKey(name);

    public object? Name(string name) => _names.GetValueOrDefault(name);

    public static DateTime FromSerial(double serial) => DateTime.FromOADate(serial);

    private static XDocument Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException($"Falta la parte {path} en el .xlsx.");
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static Dictionary<string, string> Relationships(ZipArchive zip, string relsPath, string baseDir)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in Load(zip, relsPath).Root!.Elements(PkgRel + "Relationship"))
        {
            var target = (string)r.Attribute("Target")!;
            map[(string)r.Attribute("Id")!] = Resolve(baseDir, target);
        }
        return map;
    }

    private static string Resolve(string baseDir, string target)
    {
        if (target.StartsWith('/'))
        {
            return target[1..];
        }
        var parts = (baseDir + target).Split('/').ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] == "..")
            {
                parts.RemoveAt(i);
                parts.RemoveAt(i - 1);
                i -= 2;
            }
        }
        return string.Join('/', parts);
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }
        using var s = entry.Open();
        return XDocument.Load(s).Root!.Elements(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToList();
    }

    private static Dictionary<(int, int), object?> ReadCells(XDocument sheet, List<string> shared)
    {
        var cells = new Dictionary<(int, int), object?>();
        foreach (var c in sheet.Descendants(Main + "c"))
        {
            var r = (string?)c.Attribute("r");
            if (r is null)
            {
                continue;
            }
            var t = (string?)c.Attribute("t");
            var v = c.Element(Main + "v")?.Value;
            object? value = t switch
            {
                "s" => v is null ? null : shared[int.Parse(v, CultureInfo.InvariantCulture)],
                "str" => v,
                "inlineStr" => string.Concat(c.Descendants(Main + "t").Select(x => x.Value)),
                "b" => v == "1",
                "e" => null,
                _ => v is null ? null : double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture),
            };
            cells[ParseCell(r)] = value is string str && str.Length == 0 ? null : value;
        }
        return cells;
    }

    private static (int Row, int Col) ParseCell(string reference)
    {
        var m = CellParts().Match(reference);
        var col = 0;
        foreach (var ch in m.Groups[1].Value)
        {
            col = col * 26 + (ch - 'A' + 1);
        }
        return (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), col - 1);
    }

    private static (int R0, int C0, int R1, int C1) ParseRange(string range)
    {
        var parts = range.Split(':');
        var (r0, c0) = ParseCell(parts[0]);
        var (r1, c1) = ParseCell(parts.Length > 1 ? parts[1] : parts[0]);
        return (r0, c0, r1, c1);
    }

    [GeneratedRegex("^([A-Z]+)([0-9]+)$")]
    private static partial Regex CellParts();

    [GeneratedRegex("^(?<sheet>'[^']+'|[^!]+)!(?<cell>[A-Z]+[0-9]+)$")]
    private static partial Regex CellRef();
}

/// <summary>Tabla de Excel leída: encabezados y filas (valor en caché de cada celda: double, string, bool o null).</summary>
public sealed record XlsxTable(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

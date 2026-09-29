using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MINV.Cli.WebContract;

/// <summary>
/// V7 · Tipos del contrato ya nombrados (<see cref="TypeScriptTypes.Build"/>) y cómo se escriben en TypeScript. La
/// salida es determinista: orden alfabético (ordinal), fin de línea LF y nada que dependa de la fecha o del equipo.
/// </summary>
internal sealed partial class TsCatalog(IReadOnlyList<TsDeclaration> declarations, IReadOnlyDictionary<Type, string> names)
{
    /// <summary>Declaraciones ordenadas por nombre.</summary>
    public IReadOnlyList<TsDeclaration> Declarations => declarations;

    /// <summary>Nombre en TypeScript de un tipo declarado.</summary>
    public string NameOf(Type type) => names[type];

    public TsDeclaration Declaration(Type type) => declarations.Single(d => d.Source == type);

    /// <summary>Expresión de tipo: <c>string</c>, <c>X | null</c>, <c>X[]</c>, <c>Record&lt;string, X&gt;</c>, <c>{ item1: X }</c>…</summary>
    public string Render(TsType type)
    {
        switch (type)
        {
            case TsPrimitive primitive:
                return primitive.Name;
            case TsNullable { Inner: TsPrimitive { Name: "unknown" } }:
                return "unknown";   // unknown ya incluye null
            case TsNullable nullable:
                return Render(nullable.Inner) + " | null";
            case TsArray array:
                var element = Render(array.Element);
                return (array.Element is TsNullable && element.EndsWith(" | null", StringComparison.Ordinal) ? "(" + element + ")" : element) + "[]";
            case TsDictionary dictionary:
                return "Record<string, " + Render(dictionary.Value) + ">";
            case TsTuple tuple:
                return "{ " + string.Join("; ", tuple.Items.Select(Property)) + " }";
            case TsReference reference:
                return names[reference.Target];
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de TypeScript desconocido.");
        }
    }

    /// <summary>Una declaración completa, con el nombre del tipo de .NET como comentario.</summary>
    public void Write(StringBuilder text, TsDeclaration declaration)
    {
        text.Append("/** ").Append(TypeScriptTypes.Display(declaration.Source)).Append(" */\n");
        switch (declaration)
        {
            case TsEnum enumeration:
                text.Append("export type ").Append(enumeration.Name).Append(" = ")
                    .Append(enumeration.Members.Count == 0 ? "never" : string.Join(" | ", enumeration.Members.Select(Quote))).Append(";\n");
                break;
            case TsInterface { Properties.Count: 0 } empty:
                text.Append("export type ").Append(empty.Name).Append(" = Record<string, never>;\n");
                break;
            case TsInterface model:
                text.Append("export interface ").Append(model.Name).Append(" {\n");
                foreach (var property in model.Properties)
                {
                    text.Append("  ").Append(Property(property)).Append(";\n");
                }
                text.Append("}\n");
                break;
        }
    }

    /// <summary><c>nombre: tipo</c> o <c>nombre?: tipo</c> (el nombre va entre comillas si no es un identificador).</summary>
    public string Property(TsProperty property) =>
        (Identifier().IsMatch(property.Name) ? property.Name : Quote(property.Name)) + (property.Optional ? "?" : string.Empty) + ": " + Render(property.Type);

    /// <summary>Texto entre comillas simples con los escapes de TypeScript.</summary>
    public static string Quote(string value)
    {
        var text = new StringBuilder("'");
        foreach (var c in value)
        {
            text.Append(c switch
            {
                '\\' => @"\\",
                '\'' => @"\'",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                _ when char.IsControl(c) || c is (char)0x2028 or (char)0x2029 => @"\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }
        return text.Append('\'').ToString();
    }

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex Identifier();
}

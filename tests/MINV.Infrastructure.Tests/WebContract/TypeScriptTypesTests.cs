using System.Text;
using System.Text.Json;
using MINV.Application.Remote;
using MINV.Cli.WebContract;
using MINV.Infrastructure.Tests.WebContract.Muestras;
using Compras = MINV.Infrastructure.Tests.WebContract.Compras;
using OtrasVentas = MINV.Otra.Ventas;
using Ventas = MINV.Infrastructure.Tests.WebContract.Ventas;

namespace MINV.Infrastructure.Tests.WebContract;

/// <summary>
/// V7 · Pruebas de unidad del traductor de tipos del contrato de la web (minv contrato-web, regla P-07). Cada regla se
/// comprueba contra el JSON que de verdad escriben y leen <see cref="RpcJson.Options"/>: nada se da por supuesto.
/// </summary>
public sealed class TypeScriptTypesTests
{
    private static (TypeScriptTypes Types, TsCatalog Catalog) Translate(params (Type Type, TsSide Side)[] roots)
    {
        var types = new TypeScriptTypes(RpcJson.Options);
        foreach (var (type, side) in roots)
        {
            types.AddRoot(type, Nullness.NotNull, side, "prueba");
        }
        return (types, types.Build());
    }

    private static string Declaration(TsCatalog catalog, Type type)
    {
        var text = new StringBuilder();
        catalog.Write(text, catalog.Declaration(type));
        return text.ToString();
    }

    /// <summary>La muestra completa y la mínima (nulos) de un tipo de respuesta, serializadas con RpcJson.Options, cumplen su tipo.</summary>
    private static void AssertSerializationMatches(TsCatalog catalog, Type type)
    {
        foreach (var full in new[] { true, false })
        {
            var sample = new ContractSamples(full).Create(type, Nullness.NotNull);
            var json = JsonSerializer.SerializeToElement(sample, type, RpcJson.Options);
            var errors = ContractJson.Check(json, new TsReference(type), catalog, type.Name);
            Assert.True(errors.Count == 0, $"{json.GetRawText()}\n{string.Join("\n", errors)}");
        }
    }

    [Fact]
    public void Traduce_cada_tipo_como_lo_escribe_RpcJson_incluidas_las_propiedades_calculadas()
    {
        var (_, catalog) = Translate((typeof(Ficha), TsSide.Output));

        Assert.Equal(
            "/** MINV.Infrastructure.Tests.WebContract.Muestras.Ficha */\n" +
            "export interface Ficha {\n" +
            "  activo: boolean;\n" +
            "  cantidad: number | null;\n" +
            "  color: Color;\n" +
            "  colorOpcional: Color | null;\n" +
            "  corto: number;\n" +
            "  dia: string;\n" +
            "  doble: number;\n" +
            "  duracion: string;\n" +
            "  entero: number;\n" +
            "  etiquetas: (string | null)[];\n" +
            "  fecha: string;\n" +
            "  hijas: Ficha[] | null;\n" +
            "  hora: string;\n" +
            "  id: string;\n" +
            "  imagen: string;\n" +
            "  largo: number;\n" +
            "  letra: string;\n" +
            "  momento: string;\n" +
            "  montos: Record<string, number>;\n" +
            "  par: { item1: string; item2: number };\n" +
            "  precio: number;\n" +
            "  resumen: string;\n" +
            "  texto: string;\n" +
            "  textoOpcional: string | null;\n" +
            "  valor: unknown;\n" +
            "}\n",
            Declaration(catalog, typeof(Ficha)));
        AssertSerializationMatches(catalog, typeof(Ficha));
    }

    [Fact]
    public void Las_enumeraciones_son_la_union_de_los_textos_que_escribe_el_serializador()
    {
        var (_, catalog) = Translate((typeof(Ficha), TsSide.Output));

        Assert.Equal("/** MINV.Infrastructure.Tests.WebContract.Muestras.Color */\nexport type Color = 'Azul' | 'Rojo' | 'Verde';\n",
            Declaration(catalog, typeof(Color)));
        Assert.Equal("\"Verde\"", JsonSerializer.Serialize(Color.Verde, RpcJson.Options));
        // Una combinación de [Flags] viaja como «Leer, Escribir»: no se adivina su tipo
        var error = Assert.Throws<NotSupportedException>(() => Translate((typeof(ConAcceso), TsSide.Output)));
        Assert.Contains("[Flags]", error.Message);
    }

    [Fact]
    public void En_una_peticion_los_parametros_con_valor_por_defecto_son_opcionales_y_las_calculadas_no_viajan()
    {
        var (_, catalog) = Translate((typeof(Pedido), TsSide.Input));

        Assert.Equal(
            "/** MINV.Infrastructure.Tests.WebContract.Muestras.Pedido */\n" +
            "export interface Pedido {\n" +
            "  cantidad?: number;\n" +
            "  codigo: string;\n" +
            "  color?: Color;\n" +
            "  desde?: string | null;\n" +
            "  lineas: Linea[];\n" +
            "  nota?: string | null;\n" +
            "}\n",
            Declaration(catalog, typeof(Pedido)));
        Assert.Equal("/** MINV.Infrastructure.Tests.WebContract.Muestras.Linea */\nexport interface Linea {\n  cantidad?: number;\n  sku: string;\n}\n",
            Declaration(catalog, typeof(Linea)));

        // Lo mínimo que permite el tipo (sin opcionales) lo lee RpcJson y aplica los valores por defecto del servidor
        var minimal = ContractJson.Build(new TsReference(typeof(Pedido)), typeof(Pedido), catalog, full: false)!.ToJsonString();
        Assert.Equal("{\"codigo\":\"texto\",\"lineas\":[{\"sku\":\"texto\"}]}", minimal);
        var read = JsonSerializer.Deserialize<Pedido>(minimal, RpcJson.Options)!;
        Assert.Equal((1, Color.Azul, (string?)null, (DateOnly?)null, 1), (read.Cantidad, read.Color, read.Nota, read.Desde, read.Lineas[0].Cantidad));

        // Todo lo que permite el tipo vuelve igual al leerlo (nombres, enumeración como texto, fecha)
        var full = ContractJson.Build(new TsReference(typeof(Pedido)), typeof(Pedido), catalog, full: true)!.AsObject();
        var back = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<Pedido>(full.ToJsonString(), RpcJson.Options), RpcJson.Options)!;
        Assert.True(ContractJson.IsSubset(full, back), $"{full.ToJsonString()} → {back.ToJsonString()}");
        // …y la propiedad calculada, que el servidor sí escribe, no forma parte de la petición
        Assert.True(back.AsObject().ContainsKey("auditDetails"));
        Assert.False(full.ContainsKey("auditDetails"));
    }

    [Fact]
    public void Un_tipo_que_viaja_en_la_peticion_y_en_la_respuesta_se_declara_completo()
    {
        var (_, catalog) = Translate((typeof(Pedido), TsSide.Input), (typeof(Recibo), TsSide.Output));

        Assert.Equal("/** MINV.Infrastructure.Tests.WebContract.Muestras.Linea */\nexport interface Linea {\n  cantidad: number;\n  sku: string;\n}\n",
            Declaration(catalog, typeof(Linea)));
        Assert.Contains("  cantidad?: number;\n", Declaration(catalog, typeof(Pedido)));
        AssertSerializationMatches(catalog, typeof(Recibo));
    }

    [Fact]
    public void Un_generico_cerrado_tiene_nombre_estable_y_distingue_T_de_T_anulable()
    {
        var (_, catalog) = Translate((typeof(ConPaginas), TsSide.Output));

        Assert.Equal("PaginaOfProducto", catalog.NameOf(typeof(Pagina<Producto>)));
        Assert.Equal("/** MINV.Infrastructure.Tests.WebContract.Muestras.Pagina<MINV.Infrastructure.Tests.WebContract.Muestras.Producto> */\n" +
                     "export interface PaginaOfProducto {\n  items: Producto[];\n  numero: number;\n  primero: Producto | null;\n}\n",
            Declaration(catalog, typeof(Pagina<Producto>)));
        // T? con un argumento por valor es el mismo valor: nunca viaja null
        Assert.Contains("export interface PaginaOfInt32 {\n  items: number[];\n  numero: number;\n  primero: number;\n}\n", Declaration(catalog, typeof(Pagina<int>)));
        Assert.Contains("export interface PaginaOfColor {\n  items: Color[];\n  numero: number;\n  primero: Color;\n}\n", Declaration(catalog, typeof(Pagina<Color>)));
        Assert.Equal("  colores: PaginaOfColor;\n  numeros: PaginaOfInt32;\n  productos: PaginaOfProducto;\n",
            string.Concat(Declaration(catalog, typeof(ConPaginas)).Split('\n').Where(l => l.StartsWith("  ", StringComparison.Ordinal)).Select(l => l + "\n")));
        AssertSerializationMatches(catalog, typeof(ConPaginas));

        // El nombre no distingue Pagina<Producto?> de Pagina<Producto>: un argumento anulable no se adivina
        var error = Assert.Throws<NotSupportedException>(() => Translate((typeof(ConPaginaAnulable), TsSide.Output)));
        Assert.Contains("anulable", error.Message);
    }

    [Fact]
    public void Si_dos_tipos_chocan_se_antepone_el_ultimo_segmento_de_su_espacio_de_nombres()
    {
        var (_, catalog) = Translate((typeof(Ventas.Fila), TsSide.Output), (typeof(Compras.Fila), TsSide.Output), (typeof(OtrasVentas.Fila), TsSide.Output),
            (typeof(Muestras.Record), TsSide.Output), (typeof(Producto), TsSide.Output));

        Assert.Equal("ComprasFila", catalog.NameOf(typeof(Compras.Fila)));
        // Los dos de «Ventas» siguen chocando: se antepone un segmento más
        Assert.Equal("WebContractVentasFila", catalog.NameOf(typeof(Ventas.Fila)));
        Assert.Equal("OtraVentasFila", catalog.NameOf(typeof(OtrasVentas.Fila)));
        // «Record» lo usa el propio archivo generado (Record<string, never>)
        Assert.Equal("MuestrasRecord", catalog.NameOf(typeof(Muestras.Record)));
        Assert.Equal("Producto", catalog.NameOf(typeof(Producto)));
        Assert.Equal(new[] { "ComprasFila", "MuestrasRecord", "OtraVentasFila", "Producto", "WebContractVentasFila" }, catalog.Declarations.Select(d => d.Name));

        // El sobre del RPC conserva su nombre: la web lo pide así
        var pinned = new TypeScriptTypes(RpcJson.Options, [typeof(Ventas.Fila)]);
        pinned.AddRoot(typeof(Ventas.Fila), Nullness.NotNull, TsSide.Output, "prueba");
        pinned.AddRoot(typeof(Compras.Fila), Nullness.NotNull, TsSide.Output, "prueba");
        var names = pinned.Build();
        Assert.Equal(("Fila", "ComprasFila"), (names.NameOf(typeof(Ventas.Fila)), names.NameOf(typeof(Compras.Fila))));
    }

    [Fact]
    public void Dos_operaciones_con_el_mismo_nombre_corto_hacen_fallar_al_generador_con_un_mensaje_claro()
    {
        IReadOnlyList<WebOperation> operations =
        [
            new(typeof(Ventas.Consultar), typeof(Ventas.Fila), Nullness.NotNull, false, [], [], false),
            new(typeof(Compras.Consultar), typeof(Compras.Fila), Nullness.NotNull, false, [], [], false),
        ];

        var error = Assert.Throws<InvalidOperationException>(() => WebContractGenerator.Generate(operations));

        Assert.Equal("Dos operaciones del RPC tienen el mismo nombre corto «Consultar»: MINV.Infrastructure.Tests.WebContract.Compras.Consultar y " +
                     "MINV.Infrastructure.Tests.WebContract.Ventas.Consultar. La web nombra cada operación por su nombre corto (RpcOperations y " +
                     "RPC_META del contrato generado): cambie el nombre de una de ellas.", error.Message);
    }

    [Fact]
    public void Respeta_JsonPropertyName_y_JsonIgnore()
    {
        var (_, catalog) = Translate((typeof(ConNombres), TsSide.Output));

        Assert.Equal("/** MINV.Infrastructure.Tests.WebContract.Muestras.ConNombres */\nexport interface ConNombres {\n  'codigo-externo': string;\n}\n",
            Declaration(catalog, typeof(ConNombres)));
        AssertSerializationMatches(catalog, typeof(ConNombres));
    }

    [Fact]
    public void Lo_que_no_sabe_traducir_lo_rechaza_con_un_mensaje_claro_en_vez_de_adivinar()
    {
        var unknownType = Assert.Throws<NotSupportedException>(() => Translate((typeof(ConInterfaz), TsSide.Output)));
        Assert.Contains("System.IDisposable", unknownType.Message);
        Assert.Contains("ConInterfaz.Recurso", unknownType.Message);

        var numericKeys = Assert.Throws<NotSupportedException>(() => Translate((typeof(ConClaveNumerica), TsSide.Output)));
        Assert.Contains("clave de texto", numericKeys.Message);

        var converter = Assert.Throws<NotSupportedException>(() => Translate((typeof(ConConvertidor), TsSide.Output)));
        Assert.Contains("JsonConverterAttribute", converter.Message);
    }

    [Fact]
    public void La_comprobacion_con_el_JSON_detecta_lo_que_no_coincide()
    {
        var (_, catalog) = Translate((typeof(Recibo), TsSide.Output), (typeof(Ficha), TsSide.Output));
        List<string> Errors(Type type, string json) => ContractJson.Check(JsonDocument.Parse(json).RootElement, new TsReference(type), catalog, type.Name);

        Assert.Empty(Errors(typeof(Linea), "{\"sku\":\"A\",\"cantidad\":2}"));
        Assert.Contains("Linea.sku: llegó null", Assert.Single(Errors(typeof(Linea), "{\"sku\":null,\"cantidad\":2}")));
        Assert.Contains("Linea.cantidad: el tipo la declara obligatoria", Assert.Single(Errors(typeof(Linea), "{\"sku\":\"A\"}")));
        Assert.Contains("Linea.extra: el JSON la trae y el tipo no la declara", Assert.Single(Errors(typeof(Linea), "{\"sku\":\"A\",\"cantidad\":2,\"extra\":1}")));
        Assert.Contains("Linea.cantidad: se esperaba Number y llegó String", Assert.Single(Errors(typeof(Linea), "{\"sku\":\"A\",\"cantidad\":\"2\"}")));
        var sample = JsonSerializer.SerializeToNode(new ContractSamples(true).Create(typeof(Ficha), Nullness.NotNull), RpcJson.Options)!.AsObject();
        sample["color"] = "Morado";
        Assert.Contains("«Morado» no está en Color", Assert.Single(Errors(typeof(Ficha), sample.ToJsonString())));
    }

    [Fact]
    public void Los_textos_van_entre_comillas_simples_con_los_escapes_de_TypeScript()
    {
        Assert.Equal("'Catálogo'", TsCatalog.Quote("Catálogo"));
        Assert.Equal(@"'a\'b\\c\nd'", TsCatalog.Quote("a'b\\c\nd"));
        Assert.Equal(@"'x\u0001y'", TsCatalog.Quote("x" + (char)1 + "y"));
    }
}

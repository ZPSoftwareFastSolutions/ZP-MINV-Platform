# Informe del paquete B5 · generador del contrato TypeScript (V7)

> Escrito por quien construyó el paquete, en el árbol de trabajo `ZP-MINV-V7-contrato` (rama `v7-contrato`). Sin commits.

## Resumen

Paquete B5 (regla P-07, diseño §7) terminado y en verde. Al empezar, `git status` estaba limpio y el último commit
(65644f3, B4a) no traía avance de este paquete: se construyó de cero.

1. **Comando `minv contrato-web [--salida <archivo>]`** en `src/4. Tools/MINV.Cli`. Se atiende ANTES de armar el host:
   no necesita base de datos ni cadena de conexión. Sin `--salida` escribe
   `src/3. Presentation/MINV.WebCatalog/src/3-infrastructure/http/contract.generated.ts` (busca `MINV.sln` desde la
   carpeta actual y, si no, desde la de la herramienta). Solo reescribe el archivo si cambió. UTF-8 sin BOM, LF.
2. **Generador** en `src/4. Tools/MINV.Cli/WebContract/` (5 archivos): reflexión sobre `RpcCatalog`, traducción de
   tipos con la serialización de `RpcJson.Options`, nombres, y la escritura del archivo. Lo que no sabe traducir lo
   rechaza con un mensaje claro en vez de adivinar.
3. **Contrato generado** (reemplaza al PROVISIONAL de W1): 192 operaciones y 407 tipos (346 interfaces, 31 tipos sin
   propiedades como `Record<string, never>` y 30 uniones de enumeraciones); 3947 líneas, 141 497 bytes. Trae
   `RpcOperations`, `RpcOperationMeta`, `RPC_META`, `WebSession`, `RpcRequest`, `RpcResponse`, `RpcError`, `PERMISSIONS`
   y `ROLES` con la forma que espera `contract.ts`.
4. **Se corrigieron siete defectos del provisional** (ver «Contratos»). Esas correcciones rompían la compilación de
   código de la web que no puedo tocar (`mockWeb.ts`, `webMappers.ts` y dos archivos de prueba). Por eso
   `contract.ts` ganó una sección «ajustes de la web»: alias derivados del generado, solo para las operaciones de la
   cuenta y la sesión que la web ya usaba. El resto de las operaciones queda EXACTO. Con eso `tsc`, `oxlint`, `vitest` y
   `npm run build` de la web están en verde.
5. **Pruebas**: 19 nuevas en `tests/MINV.Infrastructure.Tests/WebContract/`:
   - 11 de unidad del traductor, cada regla comparada con el JSON real de `RpcJson.Options`.
   - 8 sobre el contrato real. Incluyen la que falla con «ejecute minv contrato-web» si el archivo quedó desactualizado,
     una que serializa muestras de TODOS los tipos de respuesta y otra que lee con `RpcJson.Options` TODAS las
     peticiones armadas solo con su tipo TypeScript.
6. **Documentación**: línea nueva en la lista de comandos de `CLAUDE.md` y una precisión en
   `docs/architecture/plataforma-web-v7.md` §7 (qué trae el contrato).

No toqué la base `minv`, el PostgreSQL de localhost:5432, Docker, otras carpetas ni las pantallas del escritorio. No
leí archivos de credenciales. En la web solo cambié `contract.generated.ts` (regenerado) y `contract.ts`.

## Contratos (nombres exactos)

**Comando**
- Uso: `dotnet run --project "src/4. Tools/MINV.Cli" -- contrato-web [--salida <archivo>]`. Sale con código 0 y uno de
  dos mensajes: «✔ Contrato de la web generado: <ruta> (192 operaciones, 407 tipos)» o «✔ El contrato de la web ya
  estaba al día: <ruta> (…)».
- Con error sale con código 1 y «✖ <mensaje>» (el `catch` de siempre de `Program.cs`).
- Está en la ayuda (`minv ayuda`) y en el comentario de cabecera de `Program.cs`. El método es
  `Cli.WebContractCommand(options)`.

**Código del generador** (espacio de nombres `MINV.Cli.WebContract`, todo `internal`; `MINV.Cli.csproj` declara
`InternalsVisibleTo MINV.Infrastructure.Tests`)
- `WebContractGenerator`:
  - `Generate()`, `Generate(IReadOnlyList<WebOperation>)`, `CatalogOperations()` y `RepositoryRoot()`.
  - `const Warning = "archivo generado: no editar; regenerar con minv contrato-web"`.
  - `const Command = "dotnet run --project \"src/4. Tools/MINV.Cli\" -- contrato-web"`.
  - `RelativePath`: la ruta del archivo dentro del repositorio.
- `record WebOperation(Type Request, Type Response, Nullness ResponseNullness, bool Command, IReadOnlyList<string> Permissions, IReadOnlyList<string> Modules, bool Customer)`
  con `Name` (nombre corto) y `FullName` (el de `RpcCatalog.NameOf`).
- `record GeneratedWebContract(string Text, IReadOnlyList<WebOperation> Operations, TsCatalog Catalog, TypeScriptTypes Types)`.
- `TypeScriptTypes(JsonSerializerOptions options, IEnumerable<Type>? pinned = null)`, el traductor. Expone `AddRoot`,
  `Reference`, `Build()` (devuelve `TsCatalog`), `static Display(Type)` y
  `static Reserved = { "Record", "RpcOperations", "RpcOperationMeta" }`.
- `TsCatalog`: `Declarations`, `NameOf(Type)`, `Declaration(Type)`, `Render(TsType)`, `Write(StringBuilder, TsDeclaration)`,
  `Property(TsProperty)` y `static Quote(string)`.
- Modelo (en `TsModel.cs`):
  - `TsType`, con `TsPrimitive` (String, Number, Boolean, Unknown), `TsNullable`, `TsArray`, `TsDictionary`, `TsTuple`
    y `TsReference(Type Target)`.
  - `TsProperty(string Name, TsType Type, bool Optional, Type ClrType)`.
  - `enum TsSide { Input, Output }`.
  - `TsDeclaration`, con `TsInterface(Source, Name, Side, Properties)` y `TsEnum(Source, Name, Members)`.
- `record Nullness(bool Nullable, Nullness? Element, IReadOnlyList<Nullness> Arguments)`, con `From(NullabilityInfo)`
  y un `ToString()` comparable.
- `CompilerNullability.Of(PropertyInfo | FieldInfo | ParameterInfo definition, IReadOnlyList<Type> arguments)`: lee
  las marcas `NullableAttribute` y `NullableContextAttribute` con el mismo recorrido que .NET.

**Forma del archivo generado** (salida determinista: orden alfabético ordinal, LF, sin BOM, sin fecha)
- Primera línea: `// archivo generado: no editar; regenerar con minv contrato-web`. Luego una explicación.
- Sección «tipos de las peticiones y las respuestas»: un tipo por declaración, en orden alfabético y con
  `/** <nombre completo de .NET> */` encima.
  - Record o clase → `export interface X { … }`; si no tiene propiedades, `export type X = Record<string, never>;`.
  - Enumeración → `export type X = 'A' | 'B';`.
- Sección «operaciones del RPC»:
  - `export interface RpcOperations { Nombre: { request: X; response: Y }; … }`.
  - `export interface RpcOperationMeta { type; command; permissions; modules; customer }`. Permisos y módulos: se
    exigen todos (así lo hace la tubería).
  - `export const RPC_META = { Nombre: { type: 'MINV…', command, permissions: [...], modules: [...], customer }, … } as const satisfies Record<keyof RpcOperations, RpcOperationMeta>;`
- Sección «permisos y roles»: `PERMISSIONS` (de `PermissionCodes.All`, `name` = la descripción) y `ROLES` (de
  `RoleCodes.All`), como arreglos `{ code, name }` `as const`, ordenados por código.

**Reglas de traducción** (todas comprobadas contra el JSON real en las pruebas)
- Nombres de propiedad:
  - Los da la política de `RpcJson.Options` (camelCase); `[JsonPropertyName]` se respeta y un nombre que no es
    identificador va entre comillas.
  - `[JsonIgnore]` incondicional quita la propiedad.
  - Cualquier otro atributo de `System.Text.Json.Serialization` en el tipo o en un miembro hace fallar al generador
    (salvo `[JsonPropertyOrder]` y `[JsonConstructor]`).
- Tipos simples:
  - `string`, `char`, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` y `byte[]` (base64)
    → `string`.
  - Todos los enteros, `float`, `double` y `decimal` → `number`; `bool` → `boolean`.
  - `object` y `JsonElement` → `unknown` (`unknown | null` se escribe `unknown`).
- Enumeraciones → unión de los textos que produce el PROPIO serializador para cada valor. Una `[Flags]` hace fallar al
  generador.
- Colecciones:
  - Listas, colecciones, conjuntos y arreglos → `T[]` (`(T | null)[]` si el elemento es anulable).
  - Diccionarios con clave `string` → `Record<string, T>`; con otra clave, falla.
  - Tuplas (`IncludeFields`) → `{ item1: …; item2: … }`.
- Nulabilidad: `T?` → `T | null`. En un tipo por referencia la dice `NullabilityInfoContext`, y «desconocido» cuenta
  como anulable.
- Nulabilidad de la RESPUESTA de una operación: sale del manejador (`Task<T?> Handle(…)`), porque la del
  `IRequest<T?>` no se puede leer por reflexión. Se exige exactamente un manejador en MINV.Application. Hoy la única
  anulable es `GetOpenPhysicalCountQuery` → `PhysicalCountSheet | null`.
- Petición (lado `Input`: lo que el servidor LEE):
  - Viajan los parámetros del constructor que usa System.Text.Json (opcionales, `?:`, si tienen valor por defecto) y
    las propiedades con «set» fuera del constructor (opcionales salvo `required`).
  - Las calculadas de solo lectura NO viajan: los 96 `AuditDetails` quedan fuera.
- Respuesta (lado `Output`: lo que el servidor ESCRIBE): viajan todas las propiedades públicas, también las calculadas
  (`BranchAccess.active`, `ReceiptLine.amount`), y ninguna falta.
- Un tipo que está en los dos lados se declara como respuesta (todo obligatorio). Hoy: `ProductHomologationInput` y
  `SiatEndpointSet`.
- Nombres de tipo:
  - Se usa el nombre corto.
  - Si dos tipos chocan, o uno se llama como un nombre reservado, a cada uno se le antepone el último segmento de su
    espacio de nombres, y otro más mientras sigan chocando.
  - Los cuatro tipos del sobre (`RpcRequest`, `RpcResponse`, `RpcError`, `WebSession`) conservan su nombre y cede el
    otro.
  - Hoy no hay choques.
- Genéricos cerrados: `Nombre` + `Of` + sus argumentos unidos con `And` (`PageOfApiProduct`, `PageOfApiStock`).
  - Un argumento de un modelo o una enumeración usa su nombre; uno simple, el nombre de .NET (`String`, `Int32`…);
    `Nullable<X>` → `NullableX`; `byte[]` → `Bytes`.
  - Solo se aceptan argumentos simples o modelos no genéricos, y no anulables.
  - Dentro del genérico, `T` toma la nulabilidad del argumento y `T?` es anulable si el argumento es por referencia. Esto
    se lee de las marcas del compilador porque NullabilityInfoContext no distingue `T` de `T?`.
- Dos OPERACIONES con el mismo nombre corto hacen fallar al generador con este mensaje: «Dos operaciones del RPC tienen
  el mismo nombre corto «X»: A.X y B.X. La web nombra cada operación por su nombre corto (RpcOperations y RPC_META del
  contrato generado): cambie el nombre de una de ellas.»
- Guardia de las opciones: si `RpcJson.Options` cambia en algo que altera el JSON, el generador falla en vez de escribir
  un contrato equivocado. Vigila `IncludeFields`, `DefaultIgnoreCondition`, `IgnoreReadOnly*`, números como texto,
  `ReferenceHandler`, convertidores distintos del de enumeraciones y un resolvedor que no sea el predeterminado.

**Defectos del provisional corregidos por el generado** (forma exacta del servidor)
1. `MyAccountView`: `phone: string | null` (era `string`) y `customerCode: string`, que faltaba.
2. `BranchAccess`: `active: BranchInfo | null` (propiedad calculada que el servidor sí envía; faltaba).
3. `StorefrontReservationView`:
   - `reservedUntil: string | null` (era `string`).
   - `kind: string` y `mailQueued: boolean` son obligatorios (eran opcionales).
   - `cancelReason: string | null` obligatorio.
4. `CreateMyReservationCommand`:
   - `kind?: PcBuildKind`, con `PcBuildKind = 'Build' | 'Cart'` (era `kind: string`, obligatorio).
   - Las líneas son `StorefrontReservationLineInput { quantity?: number; sku: string; slot?: string | null }` (el
     provisional las llamaba `MyReservationLine`, con `quantity` obligatorio).
5. `RpcError`: `errors: string[] | null` y `code: string | null` son obligatorios (el servidor siempre los escribe).
6. `RpcResponse`: ya no es genérico (`result: unknown`) y `ok`, `result`, `error` y `replayed` son obligatorios.
   Nadie en la web usaba `RpcResponse<T>`.
7. Completitud y listas:
   - Faltaban 185 operaciones: entre ellas `LogoutCommand` (permitida a clientes), `ReserveCartCommand`,
     `ResendReservationMailCommand` y `GetOutgoingMailsQuery`, que pedían B1, B2 y B3a.
   - `PERMISSIONS` usa las descripciones reales de `PermissionCodes.All`; el provisional tenía textos acortados.

**Adaptador de la web `3-infrastructure/http/contract.ts`** (único archivo de la web tocado, además del generado)
- Sin cambios: importa `PERMISSIONS`, `ROLES` y `RPC_META` y reexporta todo con `export type * from './contract.generated'`.
  Siguen iguales `RpcOperationName`, `RpcRequestOf`, `RpcResponseOf`, `RpcOperationInfo`, `RPC_OPERATIONS`,
  `isRpcOperation`, `rpcOperation`, `NamedCode`, `PERMISSION_LIST`, `ROLE_LIST`, `permissionName`, `roleName` y
  `ACCOUNT_OPERATIONS`.
- Nuevo: `import type * as Contract from './contract.generated'` y la sección «ajustes de la web». Sus alias tienen
  prioridad sobre los del generado:
  - `StorefrontReservationLineInput`: el generado con `quantity: number` obligatorio.
  - `CreateMyReservationCommand`: `kind?: 'Build' | 'Cart' | 'build' | 'cart'` (tipo privado `EnumText<T>`); sus líneas
    son el alias anterior.
  - `BranchAccess`: `active` opcional.
  - `WebSession`: su `access` es el alias `BranchAccess`.
  - `MyAccountView`: `customerCode` opcional.
  - `StorefrontReservationView`: `reservedUntil: string`.
  - `RpcOperations`: el generado, con esos alias en `CancelMyReservationCommand`, `CreateMyReservationCommand`,
    `GetMyAccountQuery`, `GetMyReservationsQuery`, `SelectBranchCommand` y `UpdateMyAccountCommand` (tipo privado
    `AdjustedOperation`).
  - Las otras 186 operaciones (incluida `ChangePasswordCommand`) quedan exactas.
- Los módulos del panel deben seguir importando de `@/4-presentation/app/contract` (regla P-07).

**Pruebas nuevas** (`tests/MINV.Infrastructure.Tests/WebContract/`; el proyecto referencia ahora `MINV.Cli`)
- `TypeScriptTypesTests` (11):
  - `Traduce_cada_tipo_como_lo_escribe_RpcJson_incluidas_las_propiedades_calculadas`
  - `Las_enumeraciones_son_la_union_de_los_textos_que_escribe_el_serializador`
  - `En_una_peticion_los_parametros_con_valor_por_defecto_son_opcionales_y_las_calculadas_no_viajan`
  - `Un_tipo_que_viaja_en_la_peticion_y_en_la_respuesta_se_declara_completo`
  - `Un_generico_cerrado_tiene_nombre_estable_y_distingue_T_de_T_anulable`
  - `Si_dos_tipos_chocan_se_antepone_el_ultimo_segmento_de_su_espacio_de_nombres`
  - `Dos_operaciones_con_el_mismo_nombre_corto_hacen_fallar_al_generador_con_un_mensaje_claro`
  - `Respeta_JsonPropertyName_y_JsonIgnore`
  - `Lo_que_no_sabe_traducir_lo_rechaza_con_un_mensaje_claro_en_vez_de_adivinar`
  - `La_comprobacion_con_el_JSON_detecta_lo_que_no_coincide`
  - `Los_textos_van_entre_comillas_simples_con_los_escapes_de_TypeScript`
- `WebContractTests` (8):
  - `El_contrato_de_la_web_del_repositorio_esta_al_dia`: su mensaje es «El contrato de la web (…) no coincide con el
    código: ejecute minv contrato-web (dotnet run …). Primera diferencia en la línea N: «…» → «…».».
  - `Es_determinista_con_fin_de_linea_LF_sin_BOM_y_sin_fecha`
  - `Trae_cada_operacion_de_RpcCatalog_con_su_nombre_completo_permisos_modulos_y_si_la_usa_un_cliente`
  - `Tiene_la_forma_que_espera_el_adaptador_de_la_web`
  - `Las_listas_de_permisos_y_roles_son_las_del_dominio_por_codigo`
  - `Cada_respuesta_serializada_con_RpcJson_cumple_su_tipo_TypeScript`
  - `Cada_peticion_armada_solo_con_su_tipo_TypeScript_la_lee_RpcJson_sin_perder_nada`
  - `Las_marcas_del_compilador_dan_la_misma_nulabilidad_que_NullabilityInfoContext`
- Utilidades reutilizables (en `ContractJson.cs`):
  - `ContractJson.Check(json, tsType, catalog, ruta)`: diferencias entre un JSON y un tipo TypeScript.
  - `ContractJson.Build(tsType, clrType, catalog, full)`: el JSON que enviaría la web.
  - `ContractJson.IsSubset`.
  - `ContractSamples(full).Create(type, nullness)`: muestras «completa» y «mínima» de cualquier tipo del contrato.
- Tipos de muestra en `WebContractSamples.cs`. Usa `V21MigrationTests.RepoRoot()`.

## Desviaciones del diseño

- **Adaptador con ajustes**: el diseño esperaba que el generado encajara con cambiar solo nombres en `contract.ts`.
  Pero la forma exacta del servidor rompía la compilación de `mockWeb.ts`, `webMappers.ts` y dos pruebas, que no puedo
  tocar. Se resolvió con alias en `contract.ts` derivados del generado (no tipos escritos a mano) y limitados a las seis
  operaciones de la cuenta y la sesión que la web ya usaba.
  - Cinco ajustes son seguros o ciertos. El servidor sí lee `kind` en minúsculas: lo comprobé deserializando
    `{"kind":"cart"}` → `Cart`, y también acepta el número. Exigir `quantity` es más estricto que el servidor. Que
    `active` y `customerCode` sean opcionales es seguro para quien lee.
  - UNO dice más que el tipo del servidor: `reservedUntil: string`. El record `StorefrontReservationView` lo declara
    `DateTimeOffset?` (nulo si el armado nunca se reservó), pero en estas operaciones siempre llega con valor. «Mis
    reservas» filtra `ReservedAt != null`, crear y cancelar devuelven armados reservados, y `PcBuild.Reserve` fija
    `ReservedAt` y `ReservedUntil` juntos y nada los borra. El generado conserva `string | null`.
- **Orden alfabético en todo**: tipos, operaciones, propiedades, miembros de las enumeraciones y las listas
  `PERMISSIONS`/`ROLES` van por orden alfabético ordinal, como pide la regla de salida determinista. El provisional usaba
  el orden de declaración y el del dominio. `PERMISSIONS` usa `name` = `Description` del dominio, para conservar la
  forma `{ code, name }` que espera `contract.ts`.
- **`WebSession.kind` es `string`**: el servidor lo declara `string`. El provisional decía `'staff' | 'customer'`. La web
  no dependía de eso: `webMappers.toSession` valida el valor.
- **La nulabilidad de una respuesta sale del manejador de MediatR**: el diseño no lo decía, y es la única forma de leer
  `IRequest<T?>`. Si un día un caso de uso no tiene exactamente un manejador en MINV.Application, el generador falla con
  un mensaje que lo explica.
- **Dónde viven las pruebas**: el generador está en `MINV.Cli`, que es un ejecutable, y sus pruebas en
  `MINV.Infrastructure.Tests`, que ahora referencia a `MINV.Cli` (hay precedente: `MINV.Integration.Tests` referencia el
  ejecutable `MINV.SiatSimulator`). No se agregó un proyecto de pruebas nuevo a `MINV.sln`.
- **`plataforma-web-v7.md` §7**: se precisó qué trae el contrato (`RPC_META` con módulos y `customer`, `WebSession`, el
  sobre del RPC, `PERMISSIONS`, `ROLES`, la serialización de `RpcJson.Options` y `WebContractTests`).

## Pendientes

- **Web: quitar los ajustes de `contract.ts`** corrigiendo el código que los necesita. Sin ellos, `npx tsc -b --noEmit`
  da exactamente estos errores (la ruta es relativa a la web).

  a) `kind` en minúsculas (corregir `toCreateReservationPayload`, que debe enviar `'Cart'`, y las pruebas):
  - `src/3-infrastructure/http/webMappers.ts(151,5): error TS2322: Type '"build" | "cart"' is not assignable to type 'PcBuildKind | undefined'. Type '"build"' is not assignable to type 'PcBuildKind | undefined'. Did you mean '"Build"'?`
  - `src/3-infrastructure/data/mockCheckout.test.ts(77,124)`, `(81,123)` y `(86,111)`: `error TS2820: Type '"cart"' is not assignable to type 'PcBuildKind | undefined'. Did you mean '"Cart"'?`
  - `src/3-infrastructure/data/mockWeb.test.ts(176,120)`, `(183,74)`, `(184,107)` y `(185,107)`: el mismo TS2820.

  b) `quantity` obligatorio (el mock debe usar `line.quantity ?? 1`):
  - `src/3-infrastructure/data/mockWeb.ts(439,194): error TS18048: 'line.quantity' is possibly 'undefined'.`
  - `mockWeb.ts(442,41): error TS2345: Argument of type '{ sku: string; quantity: number | undefined; }[]' is not assignable to parameter of type 'readonly { sku: string; quantity: number; }[]'.`
  - `mockWeb.ts(463,7): error TS2322: Type '{ slot: string | null; sku: string; name: string; quantity: number | undefined; unitPrice: number; subtotal: number; }[]' is not assignable to type 'StorefrontReservationLine[]'.`

  c) `BranchAccess.active` (el mock debe calcularla):
  - `mockWeb.ts(203,35): error TS2741: Property 'active' is missing in type '{ allBranches: false; branches: BranchInfo[]; activeBranchId: string; }' but required in type 'BranchAccess'.`
  - `mockWeb.ts(204,5)`: el mismo TS2741, con `allBranches: true`.
  - Sin el alias de `WebSession`: `mockWeb.ts(214,7): error TS2322: Type '…/contract").BranchAccess' is not assignable to type '…/contract.generated").BranchAccess'. Types of property 'active' are incompatible. Type 'BranchInfo | null | undefined' is not assignable to type 'BranchInfo | null'.`

  d) `MyAccountView.customerCode` (el mock debe inventar uno, `WEB-…`):
  - `mockWeb.ts(330,5): error TS2741: Property 'customerCode' is missing in type '{ name: string; email: string; phone: string; documentType: number | null; documentNumber: string | null; complement: string | null; }' but required in type 'MyAccountView'.`

  e) `reservedUntil` anulable (el mock y la prueba deben tolerar null):
  - `mockWeb.ts(401,55): error TS2769: No overload matches this call. … Argument of type 'string | null' is not assignable to parameter of type 'string | number | Date'.`
  - `src/3-infrastructure/data/mockWeb.test.ts(180,21)`: el mismo TS2769.
- **Web: `README.md` de la web, líneas 223-224**, todavía dice que el contrato «hoy es un archivo PROVISIONAL». Ya no lo
  es. No lo toqué porque está fuera de los dos archivos permitidos.
- **Web: prueba intermitente que ya existía**: `src/4-presentation/state/SessionProvider.test.tsx`. No es del contrato:
  usa puertos simulados y una sesión del dominio.
  - En la línea base, ANTES de mis cambios, falló «al arrancar pregunta por la sesión…», línea 82: `AssertionError: expected false to be true`.
  - En la primera corrida completa después de los cambios falló «sin sesión queda listo…», línea 94: `AssertionError: expected 'api' to be 'mock'`.
  - Sola pasa 4 de 4 veces, y las dos corridas completas finales pasaron 537/537.
  - Parece una carrera de la sonda, que guarda la sesión en un `useEffect` bajo carga. No la toqué.
- **Web (informativo): el fragmento `catalogo` se descarga para todos**. El grupo `catalogo` de `vite.config.ts` es para
  `3-infrastructure/data/`, pero el fragmento principal lo importa de forma estática y `index.html` lo precarga. Eso ya
  pasaba con el contrato provisional: lo comprobé compilando esa versión.
  - `RPC_META` vive en ese fragmento. Con las 192 operaciones pasó de 309,77 kB (gzip 42,83 kB) a 338,78 kB (gzip 46,29 kB).
  - Si importa, conviene sacar el contrato de ese grupo o revisar el grupo.
- **Servidor: `long` → `number`**, como pide el encargo. En JavaScript un entero mayor que 2^53 pierde precisión; los
  NIT y números de factura actuales caben.
- **Si cambia un caso de uso, un record o un permiso**: regenerar con `minv contrato-web` y volver a correr `tsc` de la
  web. `WebContractTests.El_contrato_de_la_web_del_repositorio_esta_al_dia` lo exige. Un choque de nombres nuevo lo
  resuelve solo (con prefijo); uno de operaciones hace fallar al generador.
- **Entorno**: este equipo solo tiene el runtime de .NET 10 (10.0.11 y 10.0.12, SDK 10.0.303). Los proyectos son
  net8.0 con `RollForward=Major`. El archivo se generó y probó sobre .NET 10. Si alguien corre las pruebas sobre .NET 8,
  la prueba de «al día» compara con lo que genera ese runtime; espero lo mismo, pero no lo pude comprobar.
- **Fuera de este paquete** (siguen en rojo en `MINV.Infrastructure.Tests`, ya anotadas por B2 y B4a):
  `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol` y
  `LocalDataSeederTests.Genera_una_empresa_completa_y_coherente_con_usuarios_de_cada_rol`. Falta un usuario de prueba
  con el rol `CLIENTE` (regla P-13).
- `CHANGELOG.md` no se actualizó, igual que en los paquetes anteriores.
- No hice commit (prohibido). Todo queda sin confirmar en `ZP-MINV-V7-contrato`:
  - 7 archivos modificados: `CLAUDE.md`, `plataforma-web-v7.md`, `contract.generated.ts`, `contract.ts`,
    `MINV.Cli.csproj`, `Program.cs` y `MINV.Infrastructure.Tests.csproj`.
  - 2 carpetas nuevas: `src/4. Tools/MINV.Cli/WebContract/` y `tests/MINV.Infrastructure.Tests/WebContract/`.
  - Este informe.

## Pruebas

Entorno: .NET SDK 10.0.303 (runtime 10.0.12), node v26.7.0, npm 11.6.0. Comandos de .NET con Git Bash desde la raíz
del árbol de trabajo; los de la web con PowerShell desde `src/3. Presentation/MINV.WebCatalog`.

**PostgreSQL: no corresponde.** El generador y sus pruebas son solo reflexión y JSON en memoria: no hay pruebas nuevas
contra PostgreSQL, no se creó ningún clúster temporal, no se definió `MINV_TEST_PG` y no se tocó `minv` ni
localhost:5432.

**Preparación y línea base**
- `npm ci --no-audit --no-fund` (una vez): «added 191 packages in 14s», código 0.
- `dotnet build MINV.sln -nologo -v q` antes de empezar: «Compilación correcta. 0 Advertencia(s) 0 Errores».
- Web, línea base con el contrato provisional:
  - `npx tsc -b --noEmit`: código 0.
  - `npx oxlint`: código 0.
  - `npx vitest run`: «Test Files 1 failed | 40 passed (41) · Tests 1 failed | 536 passed (537)». Es la prueba
    intermitente de `SessionProvider.test.tsx:82` (ver «Pendientes»).

**Servidor**
- `dotnet build MINV.sln -nologo -v q --no-incremental`: «Compilación correcta. 0 Advertencia(s) 0 Errores» (16 s).
- `dotnet test tests/MINV.Infrastructure.Tests --no-build --filter "FullyQualifiedName~MINV.Infrastructure.Tests.WebContract"`:
  «Correctas! - Con error: 0, Superado: 19, Omitido: 0, Total: 19, Duración: 1 s». Es la corrida final, después de la
  compilación completa.
- `dotnet test tests/MINV.Infrastructure.Tests --no-build` (el proyecto completo, porque ahora referencia `MINV.Cli`):
  «Con error! - Con error: 2, Superado: 273, Omitido: 23, Total: 298, Duración: 5 m 20 s».
  - Las 23 omitidas son de PostgreSQL.
  - Las 2 con error son las ya conocidas de B4a (sus 279 + mis 19 = 298):
    `DemoWorkspaceTests.La_demostracion_es_Tech_Zone_Gaming_y_permite_ingresar_con_cada_rol` («Assert.Equal() Failure:
    Collections differ … Expected: [···, "BODEGA", "CAJERO", "CLIENTE", "CONSULTA", "GERENCIA", ···]») y
    `LocalDataSeederTests.Genera_una_empresa_completa_y_coherente_con_usuarios_de_cada_rol` («Assert.All() Failure: 1 out
    of 7 items … [6]: Item: Tuple ("CLIENTE", "Cliente web")»).
- Prueba de «al día» con el archivo alterado a propósito (`customerCode: string | null`): falló con «El contrato de la
  web (src\3. Presentation\MINV.WebCatalog\src\3-infrastructure\http\contract.generated.ts) no coincide con el código:
  ejecute minv contrato-web (dotnet run --project "src/4. Tools/MINV.Cli" -- contrato-web). Primera diferencia en la
  línea 283: «  customerCode: string | null;» → «  customerCode: string;».».
  - Luego `minv contrato-web` respondió «✔ Contrato de la web generado: … (192 operaciones, 407 tipos)».
  - Una segunda ejecución respondió «✔ El contrato de la web ya estaba al día: …».
  - La misma orden desde la carpeta de la web también encontró el repositorio.
- Una sola corrección en las pruebas durante el trabajo: la primera corrida dio 17 de 18, porque el texto esperado de
  `Ficha` omitía `duracion`. Corregida la prueba, no el generador.

**Web** (con el contrato generado y `contract.ts` final)
- `npx tsc -b --noEmit`: código 0, sin salida.
  - Antes de los ajustes de `contract.ts` daba los errores listados en «Pendientes».
- `npx oxlint`: código 0, sin salida.
- `npx vitest run`, tres corridas con el contrato final: «Test Files 41 passed (41) · Tests 537 passed (537)» las tres,
  código 0. La tercera fue después de un cambio solo de comentario en `contract.ts`, con `tsc` y `oxlint` repetidos: código 0.
  - Una corrida anterior dio 536/537 por la prueba intermitente de `SessionProvider.test.tsx:94`.
  - Ese archivo solo, 4 corridas: «Tests 14 passed (14)» las cuatro.
- `npm run build`: código 0, «✓ 2105 modules transformed».
  - `index-*.js`: 238,14 kB, gzip 63,27 kB.
  - `catalogo-*.js`: 338,78 kB, gzip 46,29 kB. Con el provisional era 309,77 kB, gzip 42,83 kB.
- La prueba de arquitectura de la web (`architecture.test.ts`) pasa sin cambios: un solo importador del generado y
  ninguna palabra prohibida en su código.

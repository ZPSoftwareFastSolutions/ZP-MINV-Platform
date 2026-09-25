# Inicio rápido · M-INV V3 (escritorio + PostgreSQL) · paso a paso

La V3 es una solución .NET (`MINV.sln`) con base de datos PostgreSQL y cliente de escritorio WPF. Esta guía es el
«algoritmo» para compilarla, crear la base, cargar los datos de la V2.1 y abrir el cliente.

```text
ALGORITMO
 1. Requisitos: Windows 10/11, .NET SDK 8 o superior, PostgreSQL 15+ (16 recomendado).
 2. Compilar y probar:                 tools\build_v3.ps1
 3. Crear roles y base (una vez):      psql -U postgres  (CREATE ROLE … / CREATE DATABASE minv …)
 4. Crear las 96 tablas:               psql -U minv_owner -d minv -f scripts\db_init.sql   (o: minv migrate)
 5. Cargar datos:                      minv import-v21 …   (migra la V2.1)   o   minv tenant create …   (empresa nueva)
 6. Contraseñas de los usuarios:       minv user password …
 7. Verificar la base:                 minv verify --codigo DEMO
 8. Abrir el cliente:                  MINV.DesktopClient.exe  →  empresa, correo, contraseña
```

---

## 1. Requisitos

| Qué | Versión | Cómo comprobarlo |
|---|---|---|
| Windows | 10 u 11 (64 bits) | — |
| .NET SDK | 8 o superior (con el SDK 10 funciona: los ejecutables usan *roll-forward*) | `dotnet --list-sdks` |
| PostgreSQL | 15 o superior (16 recomendado) | `psql --version` |

PostgreSQL se instala con el instalador oficial para Windows (postgresql.org › Download › Windows) o, si usa Docker:

```powershell
docker run --name minv-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:16
```

## 2. Compilar y probar

En PowerShell, en la carpeta del repositorio (rama `Inventario-V3`):

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1
```

Debe terminar en «RESULTADO: todos los pasos sin fallas». Para incluir las pruebas contra PostgreSQL real (crean y
borran una base temporal):

```powershell
$env:MINV_TEST_PG = 'Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres'
powershell -ExecutionPolicy Bypass -File tools\build_v3.ps1
```

## 3. Crear los roles y la base (una sola vez)

```powershell
psql -U postgres -c "CREATE ROLE minv_owner LOGIN PASSWORD 'clave-del-dueno';"
psql -U postgres -c "CREATE ROLE minv_app LOGIN PASSWORD 'clave-de-la-aplicacion';"
psql -U postgres -c "CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0;"
```

- `minv_owner` es el dueño de las tablas: aplica las migraciones y ejecuta la importación.
- `minv_app` es con el que se conecta el cliente: está sujeto a Row Level Security y no puede modificar ni borrar
  movimientos, pagos ni auditoría.

## 4. Crear las 96 tablas

Opción A (DBA, sin .NET):

```powershell
psql -U minv_owner -d minv -v ON_ERROR_STOP=1 -f scripts\db_init.sql
```

Opción B (con la herramienta `minv`):

```powershell
$env:MINV_DB = 'Host=localhost;Port=5432;Database=minv;Username=minv_owner;Password=clave-del-dueno'
dotnet run --project "src/4. Tools/MINV.Cli" -c Release -- migrate
```

Ambas son idempotentes (se pueden repetir) y dejan los 7 esquemas, las 96 tablas, los triggers, la seguridad por
empresa, las vistas y los permisos de `minv_app`.

## 5. Cargar datos

**A. Migrar el libro de la V2.1** (la demo del repositorio o el libro real descargado de SharePoint):

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -c Release -- import-v21 `
    --archivo src/M-INV_V2_Colaborativo.xlsx --codigo DEMO `
    --admin admin@distribuidorademo.example --nombre "Administrador M-INV" `
    --zona America/Bogota --moneda COP --pais CO --pais-nombre Colombia
```

Pide la contraseña del administrador (o use `--clave`). Al terminar muestra lo migrado y la **paridad con la V2.1**:
stock, semáforo, cobertura, ranking, alertas y pedido idénticos a la instantánea del libro.

**B. Empresa nueva, vacía**:

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -c Release -- tenant create --codigo MIEMPRESA `
    --razon-social "Mi Empresa S.R.L." --admin admin@miempresa.com --nombre "Administrador" --moneda BOB
```

## 6. Contraseñas de los usuarios migrados

La V2.1 identificaba a las personas por su cuenta de Microsoft 365 (sin contraseña propia). En la V3 cada usuario
necesita una:

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -c Release -- user password --codigo DEMO --correo ana.gomez@distribuidorademo.example
```

Queda marcada como «debe cambiarla» (la pantalla de cambio de contraseña del cliente está en la hoja de ruta; mientras tanto la reasigna el administrador con el mismo comando). Tras 5 intentos fallidos la cuenta se bloquea 15 minutos.

## 7. Verificar

```powershell
dotnet run --project "src/4. Tools/MINV.Cli" -c Release -- verify --codigo DEMO
```

Comprueba tablas, triggers append-only, Row Level Security y la conservación (Σ existencias = Σ movimientos).

## 8. Abrir el cliente de escritorio

```powershell
$env:MINV_DB = 'Host=localhost;Port=5432;Database=minv;Username=minv_app;Password=clave-de-la-aplicacion'
dotnet run --project "src/3. Presentation/MINV.DesktopClient" -c Release
```

(o edite `ConnectionStrings:Minv` en `appsettings.json` junto al ejecutable). Ingrese con el código de la empresa
(`DEMO`), su correo y su contraseña.

| Pantalla | Qué hace |
|---|---|
| ▦ Stock | Stock de cada producto al instante (sin «Recalcular»): semáforo, salidas de 30 días, cobertura y valor. Busque por SKU o nombre; la tabla es virtualizada (100.000+ filas sin demora) |
| ⇄ Registrar movimiento | SKU o código de barras (el escáner lo completa), posición (p. ej. `ALM01-A-01-01`), tipo, cantidad, documento y observaciones. Una salida mayor que el disponible se bloquea (poka-yoke) |
| ⚠ Alertas y pedido | Alertas priorizadas y pedido sugerido por proveedor (como 16_ALERTAS y 18_PEDIDO) |
| ☰ Actividad | Auditoría inmutable: quién hizo qué, cuándo y con qué resultado |

**Impresora y escáner** (`appsettings.json`, sección `Hardware`): `PrinterKind` = `serial` (`PrinterTarget` = `COM3`),
`network` (`192.168.1.50:9100`) o `windows` (nombre de la impresora USB instalada). Los escáneres en modo teclado
funcionan sin configurar; los de puerto serie usan `SerialBarcodeScanner`.

## 9. Distribuir el cliente

```powershell
dotnet publish "src/3. Presentation/MINV.DesktopClient" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Genera un ejecutable que no necesita .NET instalado en la estación (carpeta `bin/Release/net8.0-windows/win-x64/publish`).

## 10. Si algo no funciona

| Síntoma | Solución |
|---|---|
| «No se pudo conectar con la base de datos» | Revise `MINV_DB` / `appsettings.json`, que PostgreSQL esté encendido y el puerto 5432 abierto |
| «M-INV V3 requiere PostgreSQL 15 o superior» | Actualice PostgreSQL (la base usa `NULLS NOT DISTINCT` y `security_invoker`) |
| «Empresa, correo o contraseña incorrectos» | Revise el código de la empresa; asigne la contraseña con `minv user password` |
| «La empresa no tiene licenciado el módulo POS_HARDWARE» | Active el módulo en `iam.tenant_modules` (se activan todos al crear la empresa) |
| La importación de la V2.1 se cancela | El informe dice qué movimiento viola una regla (p. ej. stock negativo): corríjalo con un AJUSTE en la V2.1, recalcule y vuelva a importar |
| `dotnet ef` no arranca con solo el SDK 10 | Defina `$env:DOTNET_ROLL_FORWARD = 'Major'` (el script `build_v3.ps1` ya lo hace) |

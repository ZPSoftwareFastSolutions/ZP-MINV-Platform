# Inicio rápido · M-INV V3.1 (escritorio + PostgreSQL) · paso a paso

La V3 es una solución .NET (`MINV.sln`) con base de datos PostgreSQL y cliente de escritorio WPF (`M-INV.exe`). La
**V3.1** trae el cliente completo y rediseñado (pantalla de carga, menú por rol, tablero con gráficos, registro guiado,
toma física, alertas, pedido, ficha con kardex, tema claro/oscuro) y un **modo demostración** que funciona sin base de
datos. Guía visual de la interfaz: [`docs/product/escritorio-v3.1.md`](../product/escritorio-v3.1.md).

```text
ALGORITMO RÁPIDO · probar el sistema en 3 pasos, sin instalar PostgreSQL
 A. Requisitos: Windows 10/11 y .NET SDK 8 o superior (con el SDK 10 funciona).
 B. Publicar el ejecutable:            powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1
 C. Abrir dist\M-INV-<versión>-win-x64\M-INV.exe  →  pantalla de carga  →  «Explorar la demostración»  →  elegir un rol

ALGORITMO COMPLETO · con PostgreSQL (trabajo real)
 1. Requisitos: Windows 10/11, .NET SDK 8 o superior, PostgreSQL 15+ (16 recomendado).
 2. Compilar y probar:                 tools\build_v3.ps1
 3. Crear roles y base (una vez):      psql -U postgres  (CREATE ROLE … / CREATE DATABASE minv …)
 4. Crear las 96 tablas:               psql -U minv_owner -d minv -f scripts\db_init.sql   (o: minv migrate)
 5. Cargar datos:                      minv import-v21 …   (migra la V2.1)   o   minv tenant create …   (empresa nueva)
 6. Contraseñas de los usuarios:       minv user password …
 7. Verificar la base:                 minv verify --codigo DEMO
 8. Abrir el cliente:                  M-INV.exe  →  empresa, correo, contraseña  (la 1.ª vez, cambiar la contraseña)
 9. Distribuir a otras estaciones:     tools\publicar_escritorio.ps1  →  copiar la carpeta dist\M-INV-…
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

Queda marcada como «debe cambiarla»: al primer ingreso el cliente pide elegir una propia (al menos 8 caracteres, con letras y números). Después se cambia cuando se quiera desde el menú de la cuenta o Configuración › «Cambiar contraseña». Tras 5 intentos fallidos la cuenta se bloquea 15 minutos.

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

(o edite `ConnectionStrings:Minv` en `appsettings.json` junto a `M-INV.exe`). Al abrir:

1. **Pantalla de carga**: comprueba en segundos si PostgreSQL responde (y si es 15 o superior).
2. **Inicio de sesión**: código de la empresa (`DEMO`), correo y contraseña. Si el administrador asignó la contraseña
   con `minv user password`, se pide cambiarla al entrar. Tras 5 intentos fallidos la cuenta se bloquea 15 minutos.
3. **Ventana principal**: el menú muestra solo lo que su rol puede usar.

| Pantalla | Qué hace |
|---|---|
| Inicio | Indicadores, próximo paso, entradas/salidas de 14 días, semáforo, alertas urgentes, más vendidos, últimos movimientos y actividad |
| Stock | Al instante (sin «Recalcular»): búsqueda, chips por estado, categoría, orden por columna, exportar a Excel; doble clic abre la ficha |
| Registrar movimiento | Tipo → producto (buscador o escáner) → cantidad, con vista previa de lo que quedará; una salida que dejaría la posición en negativo se pinta de rojo sangre y no se puede registrar (poka-yoke) |
| Toma física | Iniciar, contar (escáner incluido), ver sobrantes/faltantes y generar todos los ajustes con confirmación |
| Alertas · Pedido sugerido | Priorizadas como en la V2.1, con «Registrar entrada» directo; pedido por proveedor para copiar o exportar |
| Actividad | Auditoría inmutable con búsqueda y filtros por resultado |
| Configuración · Ayuda | Tema claro/oscuro, impresora ESC/POS con página de prueba, prueba del escáner, sesión y permisos; guías y atajos |

Atajos: `Ctrl+K` buscar producto · `Ctrl+1…7` pantallas · `Ctrl+N` registrar · `F5` actualizar · `Ctrl+B` menú ·
`Ctrl+Shift+L` tema · `Esc` cerrar · `F1` ayuda.

### Probar sin base de datos (demostración)

En el inicio de sesión pulse **Explorar la demostración**: el cliente migra el libro de la V2.1 que viaja con el
ejecutable (`Demo\M-INV_V2_Colaborativo.xlsx`) a una base en memoria con el mismo importador y las mismas reglas, y
permite entrar como Administrador, Bodega, Ventas o Gerencia. Todo lo que haga ahí se pierde al cerrar; la contraseña de
la demostración es aleatoria en cada ejecución y no se guarda en ningún lado.

**Impresora y escáner** (`appsettings.json`, sección `Hardware`): `PrinterKind` = `serial` (`PrinterTarget` = `COM3`),
`network` (`192.168.1.50:9100`) o `windows` (nombre de la impresora USB instalada). Los escáneres en modo teclado
funcionan sin configurar; los de puerto serie usan `SerialBarcodeScanner`.

## 9. Distribuir el cliente

```powershell
powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1                 # requiere .NET 8+ Desktop Runtime en la estación
powershell -ExecutionPolicy Bypass -File tools\publicar_escritorio.ps1 -Autocontenido  # incluye el runtime (descarga ~150 MB la 1.ª vez)
```

Genera `dist\M-INV-<versión>-win-x64\` con `M-INV.exe` (un solo archivo, con su ícono), `appsettings.json` y la carpeta
`Demo`. Copie la carpeta completa a cada estación, ajuste la cadena de conexión en `appsettings.json` (o la variable
`MINV_DB`) y cree un acceso directo a `M-INV.exe`. Las preferencias de cada estación (tema, impresora, última empresa y
correo) se guardan en `%LOCALAPPDATA%\M-INV\cliente.json`.

## 10. Si algo no funciona

| Síntoma | Solución |
|---|---|
| «Sin conexión con la base de datos» en la pantalla de carga o en el inicio de sesión | Revise `MINV_DB` / `appsettings.json`, que PostgreSQL esté encendido y el puerto 5432 abierto, y pulse «Reintentar»; mientras tanto puede usar la demostración |
| `M-INV.exe` no abre en otra estación | Instale el *Windows Desktop Runtime* de .NET 8 o superior, o publique con `-Autocontenido` |
| La impresora no imprime | Configuración › Impresora: tipo (serie, red o Windows), destino y «Imprimir página de prueba»; el aviso dice la causa |
| «M-INV V3 requiere PostgreSQL 15 o superior» | Actualice PostgreSQL (la base usa `NULLS NOT DISTINCT` y `security_invoker`) |
| «Empresa, correo o contraseña incorrectos» | Revise el código de la empresa; asigne la contraseña con `minv user password` |
| «La empresa no tiene licenciado el módulo POS_HARDWARE» | Active el módulo en `iam.tenant_modules` (se activan todos al crear la empresa) |
| La importación de la V2.1 se cancela | El informe dice qué movimiento viola una regla (p. ej. stock negativo): corríjalo con un AJUSTE en la V2.1, recalcule y vuelva a importar |
| `dotnet ef` no arranca con solo el SDK 10 | Defina `$env:DOTNET_ROLL_FORWARD = 'Major'` (el script `build_v3.ps1` ya lo hace) |

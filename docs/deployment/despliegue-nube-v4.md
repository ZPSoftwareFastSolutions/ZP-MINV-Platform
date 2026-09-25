# Despliegue en la nube · M-INV V4 (PostgreSQL gestionado + servidores) · paso a paso

Cómo llevar M-INV 4.0.0-alpha.1 a una base PostgreSQL **gestionada** (DigitalOcean, AWS RDS o Supabase) con el
**servidor en la nube** (`MINV.CloudServer`, para los escritorios) y el **API Gateway** (`MINV.ApiGateway`, para
integraciones B2B). Arquitectura: [`docs/architecture/arquitectura-v4.md`](../architecture/arquitectura-v4.md) ·
integradores: [`docs/integration/api-gateway-v1.md`](../integration/api-gateway-v1.md) · probar todo primero en un solo
equipo: [`docs/deployment/inicio-rapido-v4.md`](inicio-rapido-v4.md).

```text
ALGORITMO DE DESPLIEGUE EN LA NUBE
 1. Crear el PostgreSQL gestionado (15 o superior; 16 recomendado), con TLS y acceso solo desde sus servidores.
 2. Como administrador del proveedor: rol dueño «minv_owner» (con CREATEROLE) y base «minv» a su nombre.
 3. Preparar la base:   tools\bd_nube.ps1 -Accion preparar -Conexion "<cadena del rol dueño>" [-DatosPrueba]
       → crea minv_server y minv_app (claves al azar), aplica scripts\db_init.sql (110 tablas, RLS, funciones),
         guarda %LOCALAPPDATA%\M-INV\credenciales-nube.txt.        Comprobar:  tools\bd_nube.ps1 -Accion estado
 4. Generar la clave maestra de integraciones (MINV_INTEGRATION_KEYS) y guardarla en una bóveda.
 5. Levantar los dos servidores con el rol minv_server (dotnet, ejecutable publicado o docker compose) detrás de https.
 6. Comprobar:  GET https://minv.suempresa.com/api/v1/health   y   GET https://api.suempresa.com/health
 7. Escritorio: «Nube» → https://minv.suempresa.com → empresa, correo y contraseña.
 8. Respaldos (PITR), réplica de lectura opcional (MINV_DB_READ) y lista de verificación (§11).
```

## 0. Qué se despliega

```text
   escritorios M-INV.exe ──https──►  minv.suempresa.com  ─┐   proxy TLS (Caddy, balanceador del proveedor…)
   tienda / ERP (API Key) ──https──►  api.suempresa.com   ─┤
                                                          ▼
              ┌───────────────────────────────┐   ┌───────────────────────────────────────────────┐
              │ MINV.CloudServer  (:5080/8080)│   │ MINV.ApiGateway (:5090/8080)                  │
              │ login · RPC · idempotencia    │   │ /v1 · webhooks (salida) · refresco de reportes│
              └───────────────┬───────────────┘   └──────────────────────┬────────────────────────┘
                              │   rol minv_server · SSL Mode=VerifyFull  │
                              ▼                                          ▼
              ┌─────────────────────────────────────────────────────────────────────┐   ┌──────────────────┐
              │ PostgreSQL gestionado: base «minv» · 110 tablas · RLS · PITR         │──►│ réplica (opcional)│
              └─────────────────────────────────────────────────────────────────────┘   │ MINV_DB_READ      │
                                                                                          └──────────────────┘
```

- **Despliegue SIEMPRE el gateway** (al menos una réplica), aunque no tenga integraciones: además del API, entrega los
  webhooks y refresca el modelo de lectura de la gerencia cada 5 minutos.
- Ambos servidores son **sin estado** (la sesión y la idempotencia viven en la base): puede correr varias réplicas de
  cada uno detrás del balanceador. El despachador de webhooks y el refresco de reportes son seguros con varias réplicas.
- Los escritorios en modo nube **no tienen credenciales de la base**: solo necesitan llegar por https al servidor.

## 1. Requisitos

| Qué | Versión / valor |
|---|---|
| PostgreSQL gestionado | 15 o superior (16 recomendado): la base usa `NULLS NOT DISTINCT`, `security_invoker` y políticas RESTRICTIVAS |
| Servidores | Linux x64 (recomendado) o Windows, con el runtime **ASP.NET Core 8** (o superior: los proyectos usan *roll-forward*), o Docker |
| Para preparar la base | un equipo con Windows y PowerShell (`tools\bd_nube.ps1`), `psql` y el SDK .NET 8+; o solo `psql` con `scripts/db_init.sql` |
| DNS y TLS | dos nombres (p. ej. `minv.suempresa.com` y `api.suempresa.com`) con certificado válido |
| Pool de conexiones | **conexión directa** o PgBouncer/Supavisor en **modo sesión**. NUNCA modo transacción (ver §2.4) |

## 2. Roles, TLS y pool de conexiones

### 2.1 Tres roles

| Rol | Uso | Propiedades |
|---|---|---|
| `minv_owner` | dueño de la base y de las tablas; aplica migraciones; crea los otros roles | `LOGIN CREATEROLE`; salta RLS por ser dueño: **nunca** lo usa un servidor ni un escritorio |
| `minv_server` | `MINV.CloudServer` y `MINV.ApiGateway` | `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`; no es dueño de nada; sin UPDATE/DELETE/TRUNCATE en los 15 libros append-only; EXECUTE en las 4 funciones SECURITY DEFINER |
| `minv_app` | escritorio con conexión directa (solo en red local; en la nube se usa el modo «Nube») | igual que `minv_server`, sin las funciones SECURITY DEFINER |

Los servidores **se niegan a arrancar** si su rol es superusuario, tiene BYPASSRLS o es dueño de alguna tabla («El rol
de conexión puede saltarse la seguridad por filas…»). `MINV_ALLOW_PRIVILEGED_ROLE=1` desactiva esa comprobación: solo
en desarrollo, nunca en la nube.

> **Cree `minv_server` y `minv_app` ANTES de aplicar las migraciones.** Los permisos se conceden dentro de la migración
> solo a los roles que ya existen. `tools\bd_nube.ps1` lo hace en ese orden. Si creó un rol después, vea el §4.3.

### 2.2 TLS con verificación completa

Descargue el certificado raíz (CA) del proveedor y use en TODAS las cadenas de conexión:

```text
SSL Mode=VerifyFull;Root Certificate=<ruta al certificado del proveedor>
```

`VerifyFull` comprueba la cadena del certificado **y** que el nombre del servidor coincida: use el nombre de host que
da el proveedor (no una IP).

### 2.3 Cadenas de conexión

```text
MINV_DB = Host=<host>;Port=<puerto>;Database=minv;Username=minv_server;Password=<contraseña>;SSL Mode=VerifyFull;Root Certificate=<ruta>
```

| Proveedor | Host | Puerto | Usuario | Certificado |
|---|---|---|---|---|
| DigitalOcean (directo) | `<cluster>-do-user-<n>-0.<x>.db.ondigitalocean.com` | `25060` | `minv_server` | `ca-certificate.crt` |
| DigitalOcean (pool en modo **Session**) | el mismo host | `25061` | `minv_server` · base = nombre del pool | `ca-certificate.crt` |
| AWS RDS (directo) | `<instancia>.<id>.<región>.rds.amazonaws.com` | `5432` | `minv_server` | `global-bundle.pem` |
| Supabase (directo, IPv6) | `db.<referencia>.supabase.co` | `5432` | `minv_server` | `prod-ca-2021.crt` |
| Supabase (Supavisor modo **sesión**, IPv4) | `aws-0-<región>.pooler.supabase.com` | `5432` | `minv_server.<referencia>` | `prod-ca-2021.crt` |

### 2.4 Por qué NO sirve el modo transacción de los pools

Al abrir cada conexión, M-INV fija las variables de **sesión** `minv.tenant_id` y `minv.branch_ids`, que usan las
políticas de Row Level Security. En modo transacción, el pool (PgBouncer, Supavisor en el puerto 6543, RDS Proxy con
multiplexación) puede mandar la siguiente sentencia a otra conexión del servidor, que tiene las variables de OTRA
petición o ninguna: se verían filas de otra empresa o sucursal, o ninguna. Use la conexión directa (cada servidor ya
tiene su propio pool de Npgsql) o un pool en **modo sesión**.

## 3. Crear la base en el proveedor

### 3.1 DigitalOcean Managed PostgreSQL

1. **Databases › Create Database Cluster**: motor PostgreSQL **16**, la región de sus servidores, un plan con **nodo en
   espera** (standby) si contrató el módulo CLOUD_HA, nombre del clúster.
2. **Settings › Trusted Sources**: agregue solo sus Droplets / App Platform / IP fijas de los servidores y del equipo
   desde el que preparará la base.
3. **Overview › Connection Details**: anote host y puerto (`25060`) y pulse **Download CA certificate**
   (`ca-certificate.crt`). El usuario administrador es `doadmin` y la base inicial `defaultdb`.
4. Conéctese como `doadmin` y cree el dueño y la base:

   ```sql
   CREATE ROLE minv_owner LOGIN CREATEROLE PASSWORD '<contraseña del dueño>';
   GRANT minv_owner TO doadmin;   -- permite crear la base a nombre del dueño
   CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0;
   ```

   ```powershell
   psql "host=<host> port=25060 dbname=defaultdb user=doadmin sslmode=verify-full sslrootcert=ca-certificate.crt"
   ```

5. **Pool de conexiones (opcional)**: si crea uno en *Connection Pools*, elija **Pool Mode = Session** (el modo por
   defecto, *Transaction*, NO sirve) y conéctese por el puerto `25061` con el nombre del pool como base de datos. Con
   pocos servidores, la conexión directa (`25060`) es lo más simple.
6. Siga en el §4 con la cadena del dueño:
   `Host=<host>;Port=25060;Database=minv;Username=minv_owner;Password=<…>;SSL Mode=VerifyFull;Root Certificate=C:\certs\ca-certificate.crt`.

Respaldos: DigitalOcean hace respaldos diarios y conserva la restauración a un punto en el tiempo (PITR) de los últimos
7 días (**Backups › Restore from backup** crea un clúster nuevo). Réplica de lectura: **Add a read-only node** →
úsela en `MINV_DB_READ`.

### 3.2 AWS RDS for PostgreSQL

1. **RDS › Create database › Standard create**: motor **PostgreSQL 16.x**; plantilla *Production* (**Multi-AZ** si
   contrató CLOUD_HA); usuario maestro (p. ej. `postgres`) con contraseña en AWS Secrets Manager.
2. **Conectividad**: la misma VPC que sus servidores, **Public access = No** (salvo que prepare la base desde fuera;
   en ese caso actívelo temporalmente y restrinja el grupo de seguridad a su IP); grupo de seguridad con entrada TCP
   `5432` solo desde el grupo de seguridad de los servidores.
3. **Grupo de parámetros**: verifique `rds.force_ssl = 1` (valor por defecto desde PostgreSQL 15).
4. **Backups**: retención automática de 7 a 35 días (habilita PITR); cifrado en reposo con KMS.
5. Descargue el paquete de certificados de RDS: `https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem`.
6. Conéctese como usuario maestro y cree el dueño y la base:

   ```sql
   CREATE ROLE minv_owner LOGIN CREATEROLE PASSWORD '<contraseña del dueño>';
   GRANT minv_owner TO postgres;   -- el usuario maestro (miembro de rds_superuser, no superusuario real)
   CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0;
   ```

7. **No use RDS Proxy** con M-INV (multiplexa por transacción: ver §2.4). Conéctese directo al endpoint de la instancia.
8. Siga en el §4 con `Host=<instancia>.<id>.<región>.rds.amazonaws.com;Port=5432;Database=minv;Username=minv_owner;…;SSL Mode=VerifyFull;Root Certificate=C:\certs\global-bundle.pem`.

Réplica de lectura: **Actions › Create read replica** (misma región o entre regiones) → `MINV_DB_READ`. Restauración:
**Restore to point in time** crea una instancia nueva; cambie `MINV_DB` a ella.

### 3.3 Supabase

1. **New project**: región cercana a sus sucursales y una contraseña fuerte para `postgres` (guárdela en su bóveda).
2. **Project Settings › Database**:
   - **SSL Configuration**: active *Enforce SSL on incoming connections* y pulse **Download certificate**
     (`prod-ca-2021.crt`).
   - **Network restrictions**: limite el acceso a las IP de sus servidores y del equipo de preparación.
3. **Conexión** (botón *Connect* del proyecto):
   - **Directa** `db.<referencia>.supabase.co:5432`: solo IPv6 (salvo el complemento de IPv4). Úsela si sus servidores
     tienen IPv6.
   - **Supavisor en modo sesión** `aws-0-<región>.pooler.supabase.com:5432`: IPv4; el usuario lleva la referencia del
     proyecto: `postgres.<referencia>`, `minv_owner.<referencia>`, `minv_server.<referencia>`. **Sirve.**
   - **Supavisor en modo transacción** (puerto `6543`): **NO sirve** para M-INV (§2.4).
4. Conéctese como `postgres` y cree el dueño y la base:

   ```sql
   CREATE ROLE minv_owner LOGIN CREATEROLE PASSWORD '<contraseña del dueño>';
   GRANT minv_owner TO postgres;
   CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0;
   ```

5. M-INV usa sus propios esquemas (`iam`, `catalog`, … `integration`, `reporting`): **no** los agregue a *Exposed
   schemas* de la API de datos de Supabase; M-INV no necesita PostgREST ni las claves `anon`/`service_role`.
6. Siga en el §4 con `Host=aws-0-<región>.pooler.supabase.com;Port=5432;Database=minv;Username=minv_owner.<referencia>;…;SSL Mode=VerifyFull;Root Certificate=C:\certs\prod-ca-2021.crt`.

Respaldos: diarios según el plan; la restauración a un punto en el tiempo (PITR) es un complemento del plan Pro o
superior: actívelo si contrató CLOUD_HA. Réplicas de lectura: disponibles en planes pagos → `MINV_DB_READ`.

## 4. Preparar la base (roles, 110 tablas, seguridad)

### 4.1 Con la herramienta (recomendado)

```powershell
powershell -ExecutionPolicy Bypass -File tools\bd_nube.ps1 -Accion preparar `
    -Conexion "Host=<host>;Port=<puerto>;Database=minv;Username=minv_owner;Password=<contraseña del dueño>;SSL Mode=VerifyFull;Root Certificate=<ruta>"
```

- Crea los roles `minv_server` y `minv_app` con contraseñas **aleatorias**, aplica `scripts\db_init.sql` (idempotente:
  110 tablas en 8 esquemas, triggers append-only, Row Level Security por empresa y por sucursal, funciones SECURITY
  DEFINER, modelo de lectura `reporting` y permisos) y escribe las cadenas de conexión en
  `%LOCALAPPDATA%\M-INV\credenciales-nube.txt` (solo en ese equipo: cópielas a su bóveda y **borre el archivo** cuando
  termine).
- `-DatosPrueba` carga además la empresa de prueba multi-sucursal (MINV, sucursales CM, EA y SC, 12 usuarios): útil
  para una demostración o un entorno de pruebas; **no** en la base de producción de un cliente.
- `tools\bd_nube.ps1 -Accion estado -Conexion "<…>"` muestra si la base responde y en qué estado está.

### 4.2 A mano (sin PowerShell)

Como `minv_owner`, conectado a la base `minv`:

```sql
CREATE ROLE minv_server LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '<contraseña del servidor>';
CREATE ROLE minv_app    LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '<contraseña del escritorio>';
```

Luego aplique el esquema (cualquiera de las dos opciones; ambas son idempotentes):

```powershell
psql "<cadena del dueño en formato libpq>" -v ON_ERROR_STOP=1 -f scripts\db_init.sql
dotnet run --project "src/4. Tools/MINV.Cli" -c Release -- migrate --conexion "<cadena del dueño>"
```

Para una empresa nueva y vacía: `minv tenant create --codigo … --razon-social "…" --admin … --nombre "…"` (con la
cadena del dueño). Para migrar una base V3 existente, restaure su respaldo en el proveedor y aplique las migraciones:
la migración `V4MultiBranchCloud` rellena la sucursal de los datos existentes (guía:
[`.claude/database-migration-guide.md`](../../.claude/database-migration-guide.md)).

### 4.3 Si creó `minv_server` después de migrar

Como `minv_owner`:

```sql
GRANT USAGE ON SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration, reporting TO minv_server;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration TO minv_server;
REVOKE UPDATE, DELETE, TRUNCATE ON inventory.stock_movements, iam.audit_logs, iam.access_logs, sales.cash_movements, sales.payments,
    accounting.exchange_rates, accounting.average_cost_history, inventory.stock_transfer_movements, inventory.stock_transfer_discrepancies,
    inventory.stock_transfer_events, inventory.stock_transfer_line_batches, integration.outbox_events, integration.webhook_deliveries,
    sales.external_orders, iam.processed_requests FROM minv_server;
REVOKE INSERT, UPDATE, DELETE ON iam.modules, iam.__ef_migrations_history FROM minv_server;
GRANT SELECT ON reporting.v_branch_stock, reporting.v_branch_daily_sales TO minv_server;
GRANT EXECUTE ON FUNCTION iam.current_tenant_id(), iam.branch_visible(uuid) TO minv_server;
GRANT EXECUTE ON FUNCTION integration.resolve_api_key(text), iam.resolve_session(text),
    integration.claim_deliveries(integer, integer), reporting.refresh_all() TO minv_server;
```

### 4.4 Verificar

```sql
SELECT count(*) FROM information_schema.tables
 WHERE table_type = 'BASE TABLE' AND table_name <> '__ef_migrations_history'
   AND table_schema IN ('iam','catalog','warehouse','inventory','purchasing','sales','accounting','integration');   -- 110
SELECT count(*) FROM pg_policies WHERE policyname = 'tenant_isolation';                                           -- 108
SELECT count(*) FROM pg_policies WHERE policyname = 'branch_isolation';                                           -- 40
SELECT count(*) FROM pg_trigger  WHERE tgname = 'trg_append_only';                                                -- 15
SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname LIKE 'minv_%';     -- minv_server y minv_app: f, f
```

## 5. Variables de entorno

| Variable | Servidor | Obligatoria | Valor |
|---|---|---|---|
| `MINV_DB` | ambos | sí | cadena con el rol **`minv_server`** y `SSL Mode=VerifyFull;Root Certificate=<ruta>` |
| `MINV_DB_READ` | ambos | no | cadena a la réplica de lectura (rol `minv_server`); si falta, los reportes leen de `MINV_DB` |
| `MINV_INTEGRATION_KEYS` | ambos | **sí** (el gateway firma con ella; ambos cifran al registrar webhooks) | `id:base64(32 bytes)` o, al rotar, `id-nueva:base64;id-anterior:base64` (la primera cifra, todas descifran) |
| `ASPNETCORE_URLS` | ambos | sí | `http://0.0.0.0:8080` detrás de un proxy TLS, o `https://+:443` si Kestrel sirve el certificado |
| `ASPNETCORE_Kestrel__Certificates__Default__Path` / `__Password` | ambos | si Kestrel sirve https | ruta del `.pfx` y su contraseña |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | ambos | detrás de un proxy | `true`: los límites por IP ven la IP real del cliente (`X-Forwarded-For`). Exponga los servidores **solo** a través del proxy |
| `MINV_ALLOW_PRIVILEGED_ROLE` | ambos | **nunca en la nube** | `1` permite arrancar con un rol dueño o con BYPASSRLS (solo desarrollo) |
| `Minv__Storage` | ambos | no | `postgres` (por defecto) o `memoria` (pruebas automáticas: arranca vacío) |
| `Minv__LoginsPerMinute` | servidor en la nube | no | inicios de sesión por minuto por IP (10) |
| `Minv__Webhooks__Enabled` | gateway | no | `true` (por defecto) · `false` en réplicas que no deben entregar webhooks |
| `Minv__Webhooks__IntervalSeconds` | gateway | no | espera cuando la cola está vacía (5) |
| `Minv__Webhooks__AllowPrivateTargets` | gateway | no | `false`; `true` solo para pruebas locales (webhooks en `localhost`) |
| `Minv__Reporting__Enabled` | gateway | no | `true` (por defecto) |
| `Minv__Reporting__RefreshMinutes` | gateway | no | cada cuántos minutos se refresca el modelo de lectura (5) |

Las claves `Minv:…` también se pueden pasar como argumentos (`--Minv:Webhooks:Enabled false`).

**Generar la clave maestra** (32 bytes aleatorios; el id identifica la clave, ≤ 40 caracteres, sin `:` ni `;`):

```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
'k' + (Get-Date -Format 'yyyyMM') + ':' + [Convert]::ToBase64String($b)
```

```bash
echo "k$(date +%Y%m):$(openssl rand -base64 32)"
```

Guárdela en la bóveda del proveedor (DigitalOcean App Platform *encrypted env vars*, AWS Secrets Manager / SSM
Parameter Store, secretos del orquestador). **Si la pierde, los secretos de los webhooks no se pueden descifrar**:
habría que desactivar y volver a registrar cada webhook (y entregar los secretos nuevos a los integradores).

## 6. Ejecutar los servidores

### 6.1 Con `dotnet run` (pruebas)

```powershell
$env:MINV_DB = 'Host=<host>;Port=<puerto>;Database=minv;Username=minv_server;Password=<…>;SSL Mode=VerifyFull;Root Certificate=<ruta>'
$env:MINV_INTEGRATION_KEYS = '<id>:<base64 de 32 bytes>'
dotnet run --project "src/3. Presentation/MINV.CloudServer" -c Release -- --urls http://localhost:5080
dotnet run --project "src/3. Presentation/MINV.ApiGateway"  -c Release -- --urls http://localhost:5090     # en otra consola
```

Para simular la nube en un solo equipo contra la base local, `tools\servidores_locales.ps1 -Accion iniciar` hace todo
esto (servidor en `http://localhost:5080`, gateway en `http://localhost:5090`, rol `minv_server`); `-Accion estado` y
`-Accion detener`.

### 6.2 Publicado (ejecutable)

```powershell
dotnet publish "src/3. Presentation/MINV.CloudServer" -c Release -o publicar\cloudserver
dotnet publish "src/3. Presentation/MINV.ApiGateway"  -c Release -o publicar\apigateway
```

Para Linux agregue `-r linux-x64 --self-contained false`. Copie cada carpeta al servidor y ejecute
`dotnet MINV.CloudServer.dll` / `dotnet MINV.ApiGateway.dll` (en Windows también `MINV.CloudServer.exe`). En Linux,
como servicio de systemd (un archivo por servidor; las variables en un archivo con permisos `600`):

```ini
# /etc/systemd/system/minv-cloudserver.service
[Unit]
Description=M-INV · servidor en la nube
After=network-online.target

[Service]
WorkingDirectory=/opt/minv/cloudserver
ExecStart=/usr/bin/dotnet /opt/minv/cloudserver/MINV.CloudServer.dll
EnvironmentFile=/etc/minv/cloudserver.env
Restart=always
User=minv

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now minv-cloudserver minv-apigateway
journalctl -u minv-cloudserver -f
```

### 6.3 Con Docker Compose

La carpeta `deploy/` trae `docker-compose.yml`, `Dockerfile.cloudserver`, `Dockerfile.apigateway` y `.env.example`
(con un servicio PostgreSQL local opcional para pruebas; en producción use el gestionado).

```bash
cd deploy
cp .env.example .env            # complete MINV_DB, MINV_DB_READ (opcional) y MINV_INTEGRATION_KEYS; nunca lo versione
docker compose up -d --build
docker compose ps
docker compose logs -f
```

Monte el certificado del proveedor dentro del contenedor y use su ruta interna en `Root Certificate=…`.

### 6.4 TLS delante de los servidores

La opción más simple en una VM es un proxy con certificados automáticos, por ejemplo Caddy:

```text
minv.suempresa.com {
    reverse_proxy localhost:5080
}
api.suempresa.com {
    reverse_proxy localhost:5090
}
```

(o el balanceador del proveedor: DigitalOcean Load Balancer / App Platform, AWS ALB con certificado de ACM). Con proxy,
defina `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` y no publique los puertos 5080/5090 a internet.

### 6.5 Qué comprueban al arrancar

1. La base está al día (si faltan migraciones: «La base de datos no está al día: faltan …», aplíquelas con el dueño).
2. El rol NO es superusuario, no tiene BYPASSRLS y no es dueño de tablas.
3. El rol puede ejecutar `iam.resolve_session` (si no: es otro rol o se creó después de migrar, §4.3).

Luego: `GET /api/v1/health` (servidor en la nube) y `GET /health` (gateway) responden
`{"status":"ok",…,"version":"4.0.0-alpha.1"}`.

## 7. Conectar los escritorios

1. Publique el escritorio (`tools\publicar_escritorio.ps1`) y distribúyalo: **no** necesita `ConnectionStrings` ni
   `MINV_DB` en modo nube.
2. En el inicio de sesión elija **Nube**, escriba `https://minv.suempresa.com` en **Servidor** y pulse
   **Probar**: debe decir «Servidor M-INV 4.0.0-alpha.1 disponible».
3. Empresa, correo y contraseña. El escritorio recuerda el modo y la dirección en `%LOCALAPPDATA%\M-INV\cliente.json`;
   el token de sesión vive solo en memoria y vence tras 12 horas sin actividad.
4. La barra superior muestra la sucursal activa; la gerencia global puede elegir «Todas las sucursales».

Reglas: la dirección debe ser **https** (http solo se acepta para `localhost`); el escritorio y el servidor deben tener
la misma versión mayor (4), si no: «Este servidor es M-INV 4.x: actualice el escritorio».

## 8. Integraciones B2B

1. En el escritorio (Administrador): **Integraciones › API Keys › Nueva** con los alcances mínimos y, si corresponde,
   una sucursal. Entregue el token al integrador por un canal seguro (se muestra una sola vez).
2. El integrador usa `https://api.suempresa.com` y la guía [`docs/integration/api-gateway-v1.md`](../integration/api-gateway-v1.md);
   la documentación interactiva queda en `https://api.suempresa.com/docs`.
3. Los webhooks deben apuntar a URL https públicas (el gateway rechaza IP privadas por seguridad).

## 9. Respaldos, PITR y continuidad

| Proveedor | Respaldos | Punto en el tiempo (PITR) | Réplica de lectura |
|---|---|---|---|
| DigitalOcean | diarios, automáticos | incluido (últimos 7 días), restaura en un clúster nuevo | nodos de solo lectura |
| AWS RDS | automáticos (retención 7–35 días) + instantáneas manuales | incluido dentro de la retención | réplicas en la región o entre regiones |
| Supabase | diarios según el plan | complemento (plan Pro o superior) | planes pagos |

- **Pruebe la restauración** al menos una vez por trimestre: restaure en una instancia nueva, apunte un servidor de
  pruebas y ejecute las consultas del §4.4 y `SELECT count(*) FROM inventory.v_conservation_breaches;` y
  `SELECT count(*) FROM inventory.v_transfer_breaches;` (ambas deben dar 0).
- Respalde **aparte** (en la bóveda) lo que no está en la base: `MINV_INTEGRATION_KEYS`, las contraseñas de los roles y
  los certificados.
- Tras una restauración, la réplica de lectura se vuelve a crear desde la instancia restaurada.
- Crecimiento: `integration.outbox_events`, `integration.webhook_deliveries` e `iam.processed_requests` son append-only
  y no se depuran solas; vigile su tamaño.

## 10. Operación diaria

| Tarea | Cómo |
|---|---|
| Salud | `GET /api/v1/health` y `GET /health` en el monitor del proveedor (cada minuto) |
| Registros | salida estándar de cada servidor (`journalctl`, `docker compose logs`, consola del proveedor): «Webhooks: N eventos…», «Modelo de lectura refrescado», fallas técnicas con su tipo de comando |
| Entregas de webhooks | escritorio › Integraciones › Entregas, o `GET /v1/webhooks/deliveries` |
| Revocar una llave filtrada | escritorio › Integraciones › API Keys › Revocar (efecto inmediato) |
| Rotar el secreto de un webhook | Integraciones › Webhooks › Rotar secreto, o `POST /v1/webhooks/{id}/rotate-secret` (doble firma 24 h) |
| Rotar la clave maestra | 1) genere una nueva y póngala PRIMERA: `MINV_INTEGRATION_KEYS=<nueva>:<…>;<anterior>:<…>`; 2) reinicie ambos servidores; 3) rote el secreto de cada webhook (queda cifrado con la nueva); 4) pasadas 24 h de la última rotación, quite la anterior y reinicie |
| Actualizar M-INV | respaldo → migraciones con `minv_owner` → nuevos servidores → escritorios de la misma versión mayor |

## 11. Lista de verificación

- [ ] PostgreSQL 15+ gestionado, TLS obligatorio, acceso de red solo desde los servidores (y temporalmente desde el
      equipo de preparación).
- [ ] Rol `minv_owner` dueño de la base `minv`; `minv_server` y `minv_app` creados **antes** de migrar.
- [ ] 110 tablas, 108 políticas `tenant_isolation`, 40 `branch_isolation`, 15 triggers append-only (§4.4).
- [ ] `minv_server`: `rolsuper = f`, `rolbypassrls = f`, sin tablas propias.
- [ ] Conexión directa o pool en **modo sesión** (nunca transacción, nunca RDS Proxy).
- [ ] Todas las cadenas con `SSL Mode=VerifyFull;Root Certificate=<ruta>` y el nombre de host del proveedor.
- [ ] `MINV_INTEGRATION_KEYS` generada, igual en ambos servidores y guardada en la bóveda.
- [ ] `MINV_ALLOW_PRIVILEGED_ROLE` **no** definida.
- [ ] Servidor en la nube y gateway (al menos una réplica) arriba; `/api/v1/health` y `/health` responden.
- [ ] https delante de ambos; `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` si hay proxy; puertos internos no expuestos.
- [ ] Escritorios en modo «Nube» con `https://…`; versión 4.x.
- [ ] Respaldos automáticos y PITR activos; restauración probada; réplica de lectura en `MINV_DB_READ` (si contrató
      GLOBAL_AUDIT).
- [ ] `credenciales-nube.txt` copiado a la bóveda y borrado del equipo de preparación.
- [ ] Módulos licenciados correctos en `iam.tenant_modules` (CLOUD_HA, MULTI_BRANCH, API_INTEGRATIONS, GLOBAL_AUDIT).

## 12. Si algo no funciona

| Síntoma | Causa y solución |
|---|---|
| El servidor no arranca: «El rol de conexión puede saltarse la seguridad por filas…» | `MINV_DB` usa el dueño o el administrador del proveedor: use `minv_server` |
| «La base de datos no está al día: faltan …» | aplique las migraciones con `minv_owner` (§4) |
| «El rol de conexión no puede ejecutar iam.resolve_session» | `minv_server` se creó después de migrar: §4.3 |
| «Falta la cadena de conexión: defina MINV_DB» | variable ausente en ese servidor o contenedor |
| `The remote certificate is invalid…` / error de SSL | certificado del proveedor equivocado o conexión por IP: use el host del proveedor y su CA |
| Datos que aparecen y desaparecen, o «la fila no es de su empresa o de sus sucursales» sin motivo | pool en modo transacción: cambie a modo sesión o conexión directa (§2.4) |
| Supabase: `Tenant or user not found` | con Supavisor el usuario es `minv_server.<referencia>` |
| No se registran webhooks: «Este equipo no tiene la clave maestra de integraciones» | falta `MINV_INTEGRATION_KEYS` en el servidor que atendió la petición (defínala en ambos) |
| Webhooks que no llegan | Integraciones › Entregas: «no es una dirección pública (protección SSRF)» = la URL resuelve a una IP privada; códigos 4xx/5xx = el receptor rechaza; revise la firma del lado del receptor |
| Tablero por sucursal desactualizado | el refresco lo hace el gateway cada 5 min: compruebe que corre y que `Minv__Reporting__Enabled` no está en `false` |
| Muchos `429` en el inicio de sesión | detrás de un proxy sin `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` todos los escritorios parecen una sola IP |
| Escritorio: «El servidor en la nube debe usar https» | escriba la dirección con `https://` (http solo para `localhost`) |
| Escritorio: «Este servidor es M-INV 4.x: actualice el escritorio» | versión mayor distinta entre escritorio y servidor |
| «Su usuario no tiene sucursales asignadas» | asígnele sucursales en Sucursales › usuarios (o dele el rol GERENCIA para ver todas) |

-- =====================================================================================================================
-- M-INV · Comprobación de normalización de la base de datos (V7, migración V7WebPlatform: 157 tablas en 10 esquemas)
-- =====================================================================================================================
-- Qué es: consultas de SOLO LECTURA sobre pg_catalog (y, en las comprobaciones de datos, sobre las tablas y las vistas de
-- control) que revisan que la base siga las reglas de normalización y de diseño de M-INV (reglas A-04, A-06, B-02, B-06,
-- B-12 y P-05; docs/database/ERD-MINV-V3.md §2 y §3). No crea, cambia ni borra nada.
--
-- Cómo leerlo: cada consulta empieza con una línea «-- @comprobacion <id> · <clase> · <título>» y un comentario con la
-- regla que comprueba. Todas devuelven las columnas «tabla | columna | detalle» (las informativas pueden devolver otras).
--   · clase «problema»: devuelve filas SOLO cuando algo no cumple la regla. Algunas filas son decisiones de diseño ya
--     justificadas: están en la tabla «Excepciones documentadas» de docs/database/normalizacion-v7.md, que es la misma
--     lista que usa la prueba automática (MINV.Infrastructure.Tests.NormalizationTests). Cualquier otra fila es un hallazgo.
--   · clase «informativa»: lista cosas para revisar a mano (no son errores); lo dice su título.
--   · E01-E19: estructura (catálogo de PostgreSQL). D01-D10: coherencia de los datos con las redundancias controladas.
--
-- Cómo ejecutarlo (con el dueño de la base, p. ej. minv_owner, o con un superusuario: un rol sujeto a RLS, como
-- minv_server, no ve las filas de ninguna empresa y las comprobaciones de datos saldrían vacías sin serlo):
--   psql -X -v ON_ERROR_STOP=1 -d <base> -f scripts/verificar_normalizacion.sql
-- La conexión puede venir de las variables PGHOST, PGPORT, PGUSER y PGPASSWORD (así la contraseña no queda en la orden).
-- Requiere PostgreSQL 15 o superior (usa pg_index.indnullsnotdistinct).
-- =====================================================================================================================
\set ON_ERROR_STOP on
\pset pager off
\pset null '—'

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E01 · problema · Tablas sin clave primaria
-- 1FN: toda fila se identifica por su clave. Una tabla sin clave primaria admite filas repetidas y no se puede
-- referenciar con una FK.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E01 · problema · Tablas sin clave primaria'
SELECT n.nspname || '.' || c.relname AS tabla, NULL::text AS columna, 'no tiene clave primaria' AS detalle
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND NOT EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conrelid = c.oid AND k.contype = 'p')
ORDER BY 1;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E02 · problema · Tablas de negocio sin tenant_id
-- Regla A-04: toda tabla de negocio lleva tenant_id (aislamiento por empresa con filtros de EF Core, FK compuestas y
-- RLS). Las únicas tablas de la plataforma sin empresa son iam.tenants e iam.modules (ERD §2). No se cuenta el historial
-- de migraciones de EF Core (iam.__ef_migrations_history), que no es una tabla de negocio.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E02 · problema · Tablas de negocio sin tenant_id'
SELECT n.nspname || '.' || c.relname AS tabla, NULL::text AS columna,
       'no tiene tenant_id: la RLS por empresa no la aísla' AS detalle
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND c.relname <> '__ef_migrations_history'
  AND NOT EXISTS (SELECT 1 FROM pg_attribute a
                  WHERE a.attrelid = c.oid AND a.attname = 'tenant_id' AND a.attnum > 0 AND NOT a.attisdropped)
ORDER BY 1;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E03 · problema · Tablas con tenant_id sin la política RLS de la empresa
-- Regla B-12: toda tabla con tenant_id tiene la RLS activada y la política PERMISIVA tenant_isolation para todas las
-- órdenes, que en lectura y en escritura exige tenant_id = iam.current_tenant_id().
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E03 · problema · Tablas con tenant_id sin la política RLS de la empresa'
SELECT tabla, NULL::text AS columna, detalle
FROM (
    SELECT n.nspname || '.' || c.relname AS tabla,
           CASE
               WHEN NOT c.relrowsecurity THEN 'la RLS está desactivada'
               WHEN p.oid IS NULL THEN 'no tiene la política tenant_isolation'
               WHEN NOT p.polpermissive OR p.polcmd <> '*'
                    OR pg_get_expr(p.polqual, p.polrelid) IS DISTINCT FROM '(tenant_id = iam.current_tenant_id())'
                    OR pg_get_expr(p.polwithcheck, p.polrelid) IS DISTINCT FROM '(tenant_id = iam.current_tenant_id())'
                   THEN 'la política tenant_isolation no exige tenant_id = iam.current_tenant_id() al leer y al escribir'
           END AS detalle
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    LEFT JOIN pg_policy p ON p.polrelid = c.oid AND p.polname = 'tenant_isolation'
    WHERE c.relkind IN ('r', 'p')
      AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
      AND EXISTS (SELECT 1 FROM pg_attribute a
                  WHERE a.attrelid = c.oid AND a.attname = 'tenant_id' AND a.attnum > 0 AND NOT a.attisdropped)
) x
WHERE detalle IS NOT NULL
ORDER BY 1;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E04 · problema · Tablas de sucursal sin branch_isolation
-- Reglas B-02 y B-12: una tabla cuya sucursal es obligatoria (branch_id, o from_branch_id y to_branch_id, NOT NULL) es
-- de una sucursal y lleva la política RESTRICTIVA branch_isolation con iam.branch_visible(…). Una sucursal ANULABLE es
-- un dato de contexto, no una partición (ERD §7.1; se listan en la revisión informativa I02).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E04 · problema · Tablas de sucursal sin branch_isolation'
SELECT tabla, columna, detalle
FROM (
    SELECT n.nspname || '.' || c.relname AS tabla,
           string_agg(a.attname, ', ' ORDER BY a.attname) AS columna,
           CASE
               WHEN p.oid IS NULL THEN 'la sucursal es obligatoria y la tabla no tiene la política branch_isolation'
               WHEN p.polpermissive THEN 'la política branch_isolation no es RESTRICTIVA'
               WHEN pg_get_expr(p.polqual, p.polrelid) NOT LIKE '%iam.branch_visible(%' THEN 'branch_isolation no usa iam.branch_visible'
           END AS detalle
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname IN ('branch_id', 'from_branch_id', 'to_branch_id')
                       AND a.attnotnull AND NOT a.attisdropped
    LEFT JOIN pg_policy p ON p.polrelid = c.oid AND p.polname = 'branch_isolation'
    WHERE c.relkind IN ('r', 'p')
      AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
    GROUP BY n.nspname, c.relname, p.oid, p.polpermissive, p.polqual, p.polrelid
) x
WHERE detalle IS NOT NULL
ORDER BY 1;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E05 · problema · Tablas append-only sin trigger (o con privilegios de más)
-- Reglas A-05 y B-06: un libro (hecho inmutable) no se actualiza ni se borra. Convención del ERD §2: las tablas mutables
-- llevan updated_at y los libros solo created_at. Todo libro tiene trg_append_only y trg_append_only_truncate activos
-- con iam.minv_append_only(); ninguna tabla con esos triggers tiene updated_at; y minv_app y minv_server (si existen)
-- no conservan UPDATE, DELETE ni TRUNCATE sobre un libro.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E05 · problema · Tablas append-only sin trigger (o con privilegios de más)'
WITH tabla AS (
    SELECT c.oid, n.nspname || '.' || c.relname AS nombre,
           EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'created_at' AND NOT a.attisdropped) AS con_alta,
           EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'updated_at' AND NOT a.attisdropped) AS con_cambio
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p')
      AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
),
funcion AS (
    SELECT p.oid FROM pg_proc p JOIN pg_namespace pn ON pn.oid = p.pronamespace
    WHERE pn.nspname = 'iam' AND p.proname = 'minv_append_only'
),
libro AS (
    SELECT t.oid, t.nombre, t.con_alta, t.con_cambio,
           (SELECT g.tgenabled FROM pg_trigger g WHERE g.tgrelid = t.oid AND g.tgname = 'trg_append_only') AS fila,
           (SELECT g.tgenabled FROM pg_trigger g WHERE g.tgrelid = t.oid AND g.tgname = 'trg_append_only_truncate') AS vaciado,
           EXISTS (SELECT 1 FROM pg_trigger g WHERE g.tgrelid = t.oid AND g.tgname IN ('trg_append_only', 'trg_append_only_truncate')
                                                AND g.tgfoid NOT IN (SELECT oid FROM funcion)) AS otra_funcion
    FROM tabla t
)
SELECT nombre AS tabla, NULL::text AS columna, detalle
FROM (
    SELECT nombre,
           CASE
               WHEN fila IS NULL THEN 'libro (created_at sin updated_at) sin trg_append_only'
               WHEN vaciado IS NULL THEN 'libro sin trg_append_only_truncate'
               WHEN fila = 'D' OR vaciado = 'D' THEN 'un trigger append-only está desactivado'
               WHEN otra_funcion THEN 'un trigger append-only no ejecuta iam.minv_append_only()'
           END AS detalle
    FROM libro
    WHERE con_alta AND NOT con_cambio
    UNION ALL
    SELECT nombre, 'tiene trigger append-only y también updated_at (una fila inmutable no se actualiza)'
    FROM libro
    WHERE con_cambio AND (fila IS NOT NULL OR vaciado IS NOT NULL)
    UNION ALL
    SELECT l.nombre, r.rolname || ' conserva ' || string_agg(p.privilegio, ', ' ORDER BY p.privilegio) || ' sobre un libro'
    FROM libro l
    JOIN pg_roles r ON r.rolname IN ('minv_app', 'minv_server')
    CROSS JOIN (VALUES ('DELETE'), ('TRUNCATE'), ('UPDATE')) AS p(privilegio)
    WHERE l.fila IS NOT NULL AND has_table_privilege(r.oid, l.oid, p.privilegio)
    GROUP BY l.nombre, r.rolname
) x
WHERE detalle IS NOT NULL
ORDER BY 1, 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E06 · problema · FK sin índice que la cubra
-- Integridad y rendimiento: PostgreSQL no indexa solo las columnas que referencian. Sin un índice cuyas primeras columnas
-- sean las de la FK, cada borrado o cambio del padre y cada búsqueda de los hijos recorre la tabla entera. Sirve un
-- índice parcial solo si su filtro es «<columna de la FK> IS NOT NULL» (la comprobación de la FK nunca busca nulos).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E06 · problema · FK sin índice que la cubra'
WITH fk AS (
    SELECT k.oid, k.conname, k.conrelid, k.conkey,
           n.nspname || '.' || c.relname AS tabla,
           nf.nspname || '.' || cf.relname AS referencia,
           (SELECT string_agg(a.attname, ', ' ORDER BY u.orden)
            FROM unnest(k.conkey) WITH ORDINALITY AS u(att, orden)
            JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = u.att) AS columnas
    FROM pg_constraint k
    JOIN pg_class c ON c.oid = k.conrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN pg_class cf ON cf.oid = k.confrelid
    JOIN pg_namespace nf ON nf.oid = cf.relnamespace
    WHERE k.contype = 'f'
      AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
)
SELECT fk.tabla, fk.columnas AS columna,
       fk.conname || ' → ' || fk.referencia || ': ningún índice empieza por esas columnas' AS detalle
FROM fk
WHERE NOT EXISTS (
    SELECT 1
    FROM pg_index i
    WHERE i.indrelid = fk.conrelid
      AND i.indisvalid
      AND (SELECT array_agg(x ORDER BY x) FROM unnest((i.indkey::int2[])[0:cardinality(fk.conkey) - 1]) AS x)
          = (SELECT array_agg(x ORDER BY x) FROM unnest(fk.conkey) AS x)
      AND (i.indpred IS NULL
           OR pg_get_expr(i.indpred, i.indrelid) IN (
                  SELECT '(' || a.attname || ' IS NOT NULL)'
                  FROM unnest(fk.conkey) AS u(att)
                  JOIN pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = u.att)))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E07 · problema · FK a tablas de negocio que no son compuestas con tenant_id
-- Regla A-04 (ERD §2): toda FK hacia una tabla con tenant_id es compuesta (tenant_id, x_id) → (tenant_id, id): una fila
-- nunca puede apuntar a datos de otra empresa, y lo garantiza PostgreSQL.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E07 · problema · FK a tablas de negocio que no son compuestas con tenant_id'
SELECT n.nspname || '.' || c.relname AS tabla,
       (SELECT string_agg(a.attname, ', ' ORDER BY u.orden)
        FROM unnest(k.conkey) WITH ORDINALITY AS u(att, orden)
        JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = u.att) AS columna,
       k.conname || ' → ' || nf.nspname || '.' || cf.relname || ': la FK no incluye tenant_id → tenant_id' AS detalle
FROM pg_constraint k
JOIN pg_class c ON c.oid = k.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_class cf ON cf.oid = k.confrelid
JOIN pg_namespace nf ON nf.oid = cf.relnamespace
WHERE k.contype = 'f'
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = k.confrelid AND a.attname = 'tenant_id' AND NOT a.attisdropped)
  AND NOT EXISTS (
      SELECT 1
      FROM unnest(k.conkey, k.confkey) AS p(hijo, padre)
      JOIN pg_attribute ah ON ah.attrelid = k.conrelid AND ah.attnum = p.hijo
      JOIN pg_attribute ap ON ap.attrelid = k.confrelid AND ap.attnum = p.padre
      WHERE ah.attname = 'tenant_id' AND ap.attname = 'tenant_id')
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E08 · problema · FK entre tablas de sucursal que no incluyen la sucursal
-- Regla B-02: la FK de un hijo de sucursal hacia su padre de sucursal incluye branch_id (un hijo no puede ser de otra
-- sucursal que su padre); entre dos tablas «entre sucursales» incluye from_branch_id y to_branch_id. (El vínculo de una
-- transferencia con su movimiento, de una sucursal hacia una línea entre sucursales, no se puede expresar con una FK con
-- sucursal: su sucursal es la del movimiento, ERD §4 inventory.stock_transfer_movements.)
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E08 · problema · FK entre tablas de sucursal que no incluyen la sucursal'
WITH clase AS (
    SELECT p.polrelid AS relid,
           CASE WHEN pg_get_expr(p.polqual, p.polrelid) LIKE '%from_branch_id%' THEN 'entre' ELSE 'sucursal' END AS tipo
    FROM pg_policy p
    WHERE p.polname = 'branch_isolation'
),
par AS (
    SELECT k.oid, ah.attname AS hijo, ap.attname AS padre
    FROM pg_constraint k
    CROSS JOIN LATERAL unnest(k.conkey, k.confkey) AS u(h, p)
    JOIN pg_attribute ah ON ah.attrelid = k.conrelid AND ah.attnum = u.h
    JOIN pg_attribute ap ON ap.attrelid = k.confrelid AND ap.attnum = u.p
    WHERE k.contype = 'f'
)
SELECT n.nspname || '.' || c.relname AS tabla,
       (SELECT string_agg(a.attname, ', ' ORDER BY u.orden)
        FROM unnest(k.conkey) WITH ORDINALITY AS u(att, orden)
        JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = u.att) AS columna,
       k.conname || ' → ' || nf.nspname || '.' || cf.relname || ': falta ' ||
       CASE WHEN h.tipo = 'sucursal' THEN 'branch_id → branch_id' ELSE 'from_branch_id y to_branch_id' END AS detalle
FROM pg_constraint k
JOIN clase h ON h.relid = k.conrelid
JOIN clase r ON r.relid = k.confrelid AND r.tipo = h.tipo
JOIN pg_class c ON c.oid = k.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_class cf ON cf.oid = k.confrelid
JOIN pg_namespace nf ON nf.oid = cf.relnamespace
WHERE k.contype = 'f'
  AND ((h.tipo = 'sucursal'
        AND NOT EXISTS (SELECT 1 FROM par WHERE par.oid = k.oid AND par.hijo = 'branch_id' AND par.padre = 'branch_id'))
    OR (h.tipo = 'entre'
        AND (NOT EXISTS (SELECT 1 FROM par WHERE par.oid = k.oid AND par.hijo = 'from_branch_id' AND par.padre = 'from_branch_id')
             OR NOT EXISTS (SELECT 1 FROM par WHERE par.oid = k.oid AND par.hijo = 'to_branch_id' AND par.padre = 'to_branch_id'))))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E09 · problema · Columnas de texto sin límite
-- ERD §2 (tipos): los textos tienen longitud máxima (varchar(n)), así el dominio de cada atributo está definido y un
-- dato desbocado no crece sin control. Se listan text y varchar/char sin longitud (los JSON van en E11).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E09 · problema · Columnas de texto sin límite'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna,
       format_type(a.atttypid, a.atttypmod) || ' sin longitud máxima' AS detalle
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND a.attnum > 0 AND NOT a.attisdropped
  AND (a.atttypid = 'text'::regtype
       OR (a.atttypid IN ('varchar'::regtype, 'bpchar'::regtype) AND a.atttypmod < 0))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E10 · problema · Columnas cuyo nombre sugiere un total o un contador guardado
-- 3FN y regla A-06: no se guardan columnas derivables de otras (totales, saldos, contadores) salvo las redundancias
-- controladas documentadas en el ERD. Revisa por el nombre las columnas numéricas (total, subtotal, sum, count, counter,
-- balance, saldo, attempts, rounds, failures, retries, on_hand, reserved, available, accumulated, outstanding).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E10 · problema · Columnas cuyo nombre sugiere un total o un contador guardado'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna,
       format_type(a.atttypid, a.atttypmod) || ': el nombre sugiere un valor derivable (total o contador)' AS detalle
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND a.attnum > 0 AND NOT a.attisdropped
  AND a.atttypid IN ('int2'::regtype, 'int4'::regtype, 'int8'::regtype, 'numeric'::regtype, 'float4'::regtype,
                     'float8'::regtype, 'money'::regtype)
  AND a.attname ~ '(^|_)(total|totals|subtotal|sum|count|counter|balance|saldo|attempts|rounds|failures|retries|on_hand|reserved|available|accumulated|outstanding)(_|$)'
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E11 · problema · Columnas JSON
-- 1FN: cada columna guarda un valor atómico. Un JSON guarda un documento entero; solo se acepta como copia literal de un
-- mensaje (evento del outbox, respuesta guardada, mensajes del SIN, detalle de la auditoría) que nunca se consulta por sus
-- partes. Se listan json/jsonb y los textos que por su nombre guardan un documento (payload, details, messages, response,
-- body, metadata, data, json).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E11 · problema · Columnas JSON'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna,
       CASE WHEN a.atttypid IN ('json'::regtype, 'jsonb'::regtype) THEN format_type(a.atttypid, a.atttypmod)
            ELSE format_type(a.atttypid, a.atttypmod) || ' que guarda un documento (por su nombre)' END AS detalle
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND a.attnum > 0 AND NOT a.attisdropped
  AND (a.atttypid IN ('json'::regtype, 'jsonb'::regtype)
       OR (a.atttypid IN ('text'::regtype, 'varchar'::regtype)
           AND a.attname IN ('payload', 'details', 'messages', 'response', 'body', 'metadata', 'data', 'json')))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E12 · problema · Claves naturales sin restricción de unicidad
-- FNBC: si un dato identifica a la fila (code, number, sku, email) debe haber una unicidad (restricción o índice único,
-- normalmente con tenant_id) que lo garantice; si no, dos filas pueden decir ser «la misma» cosa.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E12 · problema · Claves naturales sin restricción de unicidad'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna,
       'ninguna restricción ni índice único incluye la columna' AS detalle
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND a.attnum > 0 AND NOT a.attisdropped
  AND a.attname IN ('code', 'number', 'sku', 'email')
  AND NOT EXISTS (SELECT 1 FROM pg_index i
                  WHERE i.indrelid = c.oid AND i.indisunique AND a.attnum = ANY (i.indkey::int2[]))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E13 · problema · Columnas de estado sin CHECK
-- Dominio de un atributo (ERD §2: estados como texto): una columna de estado (status, state, mode, *_status, *_state)
-- tiene un CHECK que limita sus valores a la lista del enum (status IN (…); PostgreSQL guarda un IN de un solo valor
-- como una igualdad). Un CHECK que solo la relaciona con otra columna, como «(status = 'Closed') = (closed_at IS NOT
-- NULL)», no limita sus valores.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E13 · problema · Columnas de estado sin CHECK'
WITH col AS (
    SELECT c.oid AS relid, n.nspname || '.' || c.relname AS tabla, a.attname
    FROM pg_attribute a
    JOIN pg_class c ON c.oid = a.attrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p')
      AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
      AND a.attnum > 0 AND NOT a.attisdropped
      AND a.atttypid IN ('varchar'::regtype, 'text'::regtype, 'bpchar'::regtype)
      AND (a.attname IN ('status', 'state', 'mode') OR a.attname ~ '_(status|state)$')
)
SELECT tabla, attname AS columna, 'estado sin un CHECK que limite sus valores (IN (…))' AS detalle
FROM col
WHERE NOT EXISTS (SELECT 1 FROM pg_constraint k
                  WHERE k.conrelid = col.relid AND k.contype = 'c'
                    AND (pg_get_constraintdef(k.oid) ~ ('\m' || col.attname || '(\)::text)? = ANY \(')
                         OR pg_get_constraintdef(k.oid) ~ ('^CHECK \(+' || col.attname || '(\)::text)? = ''[^'']*''::text\)+$')))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E14 · problema · Columnas enumeradas sin CHECK
-- Igual que E13 para los demás valores de una lista cerrada (kind, type, direction, outcome, channel, action,
-- environment, scope, method, domain y *_type, *_kind, *_mode, …) guardados como código corto (texto de hasta 40 caracteres o
-- número entero): un CHECK con la lista (IN (…)) o con el rango de códigos (BETWEEN) define su dominio. Los textos más
-- largos con esos nombres son listas abiertas (acción y entidad de la auditoría, tipo de petición, tipo de evento, canal
-- de un pedido externo) y no se revisan. Las listas cerradas sin CHECK por diseño son excepciones documentadas.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E14 · problema · Columnas enumeradas sin CHECK'
WITH col AS (
    SELECT c.oid AS relid, n.nspname || '.' || c.relname AS tabla, a.attname
    FROM pg_attribute a
    JOIN pg_class c ON c.oid = a.attrelid
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p')
      AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
      AND a.attnum > 0 AND NOT a.attisdropped
      AND (a.atttypid IN ('int2'::regtype, 'int4'::regtype)
           OR (a.atttypid IN ('varchar'::regtype, 'bpchar'::regtype) AND a.atttypmod BETWEEN 5 AND 44))
      AND a.attname ~ '(^|_)(kind|type|direction|outcome|channel|action|environment|scope|method|mode|domain)$'
      AND a.attname <> 'mode'
)
SELECT tabla, attname AS columna, 'valor de una lista cerrada sin un CHECK que limite sus valores (IN (…) o BETWEEN)' AS detalle
FROM col
WHERE NOT EXISTS (SELECT 1 FROM pg_constraint k
                  WHERE k.conrelid = col.relid AND k.contype = 'c'
                    AND (pg_get_constraintdef(k.oid) ~ ('\m' || col.attname || '(\)::text)? = ANY \(')
                         OR pg_get_constraintdef(k.oid) ~ ('^CHECK \(+' || col.attname || '(\)::text)? = ''[^'']*''::text\)+$')
                         OR (pg_get_constraintdef(k.oid) ~ ('\m' || col.attname || '\M >= ')
                             AND pg_get_constraintdef(k.oid) ~ ('\m' || col.attname || '\M <= '))))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E15 · problema · Restricciones CHECK duplicadas
-- Dos CHECK con la misma definición en la misma tabla repiten una regla (y la duplican en cada cambio del esquema).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E15 · problema · Restricciones CHECK duplicadas'
SELECT n.nspname || '.' || c.relname AS tabla,
       string_agg(k.conname, ', ' ORDER BY k.conname) AS columna,
       'misma definición: ' || pg_get_constraintdef(k.oid) AS detalle
FROM pg_constraint k
JOIN pg_class c ON c.oid = k.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE k.contype = 'c'
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
GROUP BY n.nspname, c.relname, pg_get_constraintdef(k.oid)
HAVING count(*) > 1
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E16 · problema · Columnas anulables que forman parte de una unicidad
-- En PostgreSQL dos nulos no chocan en un índice único: una unicidad con una columna anulable solo protege las filas sin
-- nulos. Es correcto si el índice es NULLS NOT DISTINCT o si es parcial con «<columna> IS NOT NULL» (entonces la regla es,
-- a propósito, «única cuando existe»).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E16 · problema · Columnas anulables que forman parte de una unicidad'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna,
       'admite nulos y forma parte de ' || ic.relname || ' (sin NULLS NOT DISTINCT ni filtro IS NOT NULL)' AS detalle
FROM pg_index i
JOIN pg_class ic ON ic.oid = i.indexrelid
JOIN pg_class c ON c.oid = i.indrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
CROSS JOIN LATERAL unnest(i.indkey::int2[]) AS k(att)
JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.att
WHERE i.indisunique
  AND c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND NOT a.attnotnull
  AND NOT i.indnullsnotdistinct
  AND coalesce(pg_get_expr(i.indpred, i.indrelid), '') NOT LIKE '%' || a.attname || ' IS NOT NULL%'
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E17 · problema · Grupos repetidos en columnas
-- 1FN: sin grupos repetidos. Una columna de tipo arreglo o una serie de columnas numeradas (telefono1, telefono2…)
-- guardan una lista en la fila; la lista va en una tabla hija. (El número de un algoritmo, como sha256, no cuenta.)
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E17 · problema · Grupos repetidos en columnas'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna,
       CASE WHEN t.typcategory = 'A' THEN 'columna de tipo arreglo (' || format_type(a.atttypid, a.atttypmod) || ')'
            ELSE 'nombre numerado: posible grupo repetido' END AS detalle
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_type t ON t.oid = a.atttypid
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND a.attnum > 0 AND NOT a.attisdropped
  AND (t.typcategory = 'A' OR (a.attname ~ '[0-9]+$' AND a.attname !~ '(sha256|sha1|md5)$'))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E18 · problema · Columnas que copian un dato de la tabla que ya referencian
-- 3FN: si una fila apunta a otra por <x>_id, un atributo de esa otra fila (<x>_code, <x>_name…) se obtiene por la FK y
-- guardarlo otra vez es una dependencia transitiva. Solo se acepta como instantánea legal o comercial documentada.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E18 · problema · Columnas que copian un dato de la tabla que ya referencian'
WITH fk AS (
    SELECT DISTINCT k.conrelid, k.confrelid, regexp_replace(a.attname, '_id$', '') AS prefijo
    FROM pg_constraint k
    CROSS JOIN LATERAL unnest(k.conkey) AS x(att)
    JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attnum = x.att
    WHERE k.contype = 'f'
      AND a.attname LIKE '%\_id'
      AND a.attname NOT IN ('tenant_id', 'branch_id', 'from_branch_id', 'to_branch_id')
)
SELECT DISTINCT n.nspname || '.' || c.relname AS tabla, ch.attname AS columna,
       'copia ' || nf.nspname || '.' || cf.relname || '.' || pa.attname || ', que ya se alcanza por ' || fk.prefijo || '_id' AS detalle
FROM fk
JOIN pg_class c ON c.oid = fk.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_class cf ON cf.oid = fk.confrelid
JOIN pg_namespace nf ON nf.oid = cf.relnamespace
JOIN pg_attribute pa ON pa.attrelid = fk.confrelid AND pa.attnum > 0 AND NOT pa.attisdropped
                    AND pa.attname NOT IN ('id', 'tenant_id', 'branch_id', 'created_at', 'created_by', 'updated_at', 'updated_by')
JOIN pg_attribute ch ON ch.attrelid = fk.conrelid AND ch.attnum > 0 AND NOT ch.attisdropped
                    AND ch.attname = fk.prefijo || '_' || pa.attname
WHERE n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion E19 · problema · Columnas booleanas que un CHECK iguala a otra columna
-- 3FN: una columna booleana que un CHECK obliga a ser «<otra columna> IS [NOT] NULL» se puede calcular de esa otra
-- columna (dato derivable guardado). El CHECK impide que se contradigan, pero la columna sobra.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'E19 · problema · Columnas booleanas que un CHECK iguala a otra columna'
SELECT n.nspname || '.' || c.relname AS tabla, m[1] AS columna,
       k.conname || ' la iguala a (' || m[2] || ' IS ' || coalesce(m[3], '') || 'NULL)' AS detalle
FROM pg_constraint k
JOIN pg_class c ON c.oid = k.conrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
CROSS JOIN LATERAL regexp_match(pg_get_constraintdef(k.oid), '^CHECK \(\((\w+) = \((\w+) IS (NOT )?NULL\)\)\)$') AS m
JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attname = m[1] AND a.atttypid = 'bool'::regtype
WHERE k.contype = 'c'
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion I01 · informativa · Resumen del esquema (revisión informativa: no son errores)
-- Cifras para comparar con el ERD y la guía de migraciones (V7: 157 tablas, 155 tenant_isolation, 64 branch_isolation,
-- 32 libros append-only, 6 funciones SECURITY DEFINER).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'I01 · informativa · Resumen del esquema (revisión informativa: no son errores)'
WITH t AS (
    SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p')
      AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
      AND c.relname <> '__ef_migrations_history'
)
SELECT indicador AS tabla, NULL::text AS columna, valor::text AS detalle
FROM (VALUES
    (1, 'Tablas del modelo', (SELECT count(*) FROM t)),
    (2, 'Esquemas con tablas', (SELECT count(DISTINCT c.relnamespace) FROM pg_class c WHERE c.oid IN (SELECT oid FROM t))),
    (3, 'Claves foráneas', (SELECT count(*) FROM pg_constraint k WHERE k.contype = 'f' AND k.conrelid IN (SELECT oid FROM t))),
    (4, 'Restricciones CHECK', (SELECT count(*) FROM pg_constraint k WHERE k.contype = 'c' AND k.conrelid IN (SELECT oid FROM t))),
    (5, 'Índices únicos (con las claves primarias)', (SELECT count(*) FROM pg_index i WHERE i.indisunique AND i.indrelid IN (SELECT oid FROM t))),
    (6, 'Políticas tenant_isolation', (SELECT count(*) FROM pg_policy p WHERE p.polname = 'tenant_isolation' AND p.polrelid IN (SELECT oid FROM t))),
    (7, 'Políticas branch_isolation (RESTRICTIVAS)', (SELECT count(*) FROM pg_policy p WHERE p.polname = 'branch_isolation' AND NOT p.polpermissive AND p.polrelid IN (SELECT oid FROM t))),
    (8, 'Libros append-only (trg_append_only)', (SELECT count(*) FROM pg_trigger g WHERE g.tgname = 'trg_append_only' AND g.tgrelid IN (SELECT oid FROM t))),
    (9, 'Funciones SECURITY DEFINER', (SELECT count(*) FROM pg_proc p JOIN pg_namespace pn ON pn.oid = p.pronamespace
                                      WHERE p.prosecdef AND pn.nspname IN ('iam', 'integration', 'billing', 'reporting'))),
    (10, 'Vistas de control', (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                               WHERE c.relkind = 'v' AND c.relname LIKE 'v\_%\_breaches' AND n.nspname = 'inventory'))
) AS v(orden, indicador, valor)
ORDER BY orden;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion I02 · informativa · Sucursal anulable: dato de contexto, no partición (revisión informativa)
-- ERD §7.1: en estas tablas la sucursal es un atributo (la sucursal activa de quien actuó, la de una llave o la de un
-- centro de costo), no la partición de la fila: no llevan branch_isolation y no son errores.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'I02 · informativa · Sucursal anulable: dato de contexto, no partición (revisión informativa)'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna,
       'sucursal opcional (dato de contexto): sin branch_isolation' AS detalle
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND a.attnum > 0 AND NOT a.attisdropped AND NOT a.attnotnull
  AND a.attname IN ('branch_id', 'from_branch_id', 'to_branch_id', 'active_branch_id')
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion I03 · informativa · Instantáneas deliberadas en documentos y reservas (revisión informativa)
-- Datos copiados A PROPÓSITO en el momento del hecho (precio cotizado, contacto y datos para la factura del visitante,
-- comprador de un documento fiscal): no dependen de lo que pase después con el cliente o el catálogo (ERD §8.4, §9.5,
-- §10.1 y §11.4). No son errores.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'I03 · informativa · Instantáneas deliberadas en documentos y reservas (revisión informativa)'
SELECT n.nspname || '.' || c.relname AS tabla, a.attname AS columna, 'instantánea del momento del hecho' AS detalle
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p')
  AND n.nspname IN ('iam', 'catalog', 'warehouse', 'inventory', 'purchasing', 'sales', 'accounting', 'integration', 'billing', 'service')
  AND a.attnum > 0 AND NOT a.attisdropped
  AND (a.attname ~ '^(buyer|contact)_' OR a.attname = 'quoted_unit_price'
       OR (c.relname = 'fiscal_documents' AND a.attname IN ('customer_code', 'user_code')))
ORDER BY 1, 2;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D01 · problema · Existencias distintas de la suma de sus movimientos
-- Redundancia controlada (ERD §3): stock_levels.quantity_on_hand es el estado materializado de la existencia y debe ser
-- Σ movimientos (vista inventory.v_conservation_breaches, invariante de conservación de la V1).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D01 · problema · Existencias distintas de la suma de sus movimientos'
SELECT 'inventory.stock_levels' AS tabla, 'quantity_on_hand' AS columna,
       'existencia ' || v.stock_level_id || ': ' || v.quantity_on_hand || ' y los movimientos suman ' || coalesce(v.ledger_quantity, 0) AS detalle
FROM inventory.v_conservation_breaches v
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D02 · problema · Reservado distinto de la suma de las reservas activas
-- Redundancia controlada (ERD §3 y §10.2): stock_levels.quantity_reserved debe ser la suma de las reservas activas de
-- esa existencia (caja, pedido, armado o carrito).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D02 · problema · Reservado distinto de la suma de las reservas activas'
SELECT 'inventory.stock_levels' AS tabla, 'quantity_reserved' AS columna,
       'existencia ' || s.id || ': reservado ' || s.quantity_reserved || ' y las reservas activas suman ' || coalesce(r.total, 0) AS detalle
FROM inventory.stock_levels s
LEFT JOIN (SELECT tenant_id, stock_level_id, sum(quantity) AS total
           FROM inventory.stock_reservations
           WHERE status = 'Active'
           GROUP BY tenant_id, stock_level_id) r ON r.tenant_id = s.tenant_id AND r.stock_level_id = s.id
WHERE s.quantity_reserved <> coalesce(r.total, 0)
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D03 · problema · Transferencias que no conservan la mercadería
-- Regla B-04: lo recibido NO se guarda (se deriva: despachado − faltantes); la vista inventory.v_transfer_breaches
-- compara salidas, manifiesto, recibido y faltantes de cada línea.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D03 · problema · Transferencias que no conservan la mercadería'
SELECT 'inventory.stock_transfer_lines' AS tabla, 'quantity' AS columna,
       'transferencia ' || v.number || ' (' || v.status || '), línea ' || v.line_id || ': cantidad ' || v.quantity ||
       ', salidas ' || coalesce(v.sent, 0) || ', manifiesto ' || coalesce(v.shipped, 0) || ', recibido ' || coalesce(v.received, 0) ||
       ', faltante ' || coalesce(v.shortage, 0) AS detalle
FROM inventory.v_transfer_breaches v
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D04 · problema · Series en stock distintas del stock
-- Regla T-02: el estado de cada serie es estado materializado (con su bitácora serial_events); en cada sucursal las
-- series InStock de una variante serializada coinciden con su stock (vista inventory.v_serial_breaches).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D04 · problema · Series en stock distintas del stock'
SELECT 'inventory.serial_numbers' AS tabla, 'status' AS columna,
       'SKU ' || v.sku || ' en la sucursal ' || v.branch_id || ': ' || v.serials_in_stock || ' series en stock y stock ' || v.stock AS detalle
FROM inventory.v_serial_breaches v
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D05 · problema · Armados y carritos cuyo estado no es el de su bitácora
-- Regla S-04 (ERD §10.2): pc_builds.status es estado materializado; cada cambio deja su fila en pc_build_events con el
-- estado resultante. El estado actual es el de alguna fila del último instante de su bitácora (hay filas con el mismo
-- instante cuando un caso de uso hace dos cambios juntos).
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D05 · problema · Armados y carritos cuyo estado no es el de su bitácora'
SELECT 'sales.pc_builds' AS tabla, 'status' AS columna,
       b.number || ' (' || b.status || '): ' ||
       CASE WHEN u.instante IS NULL THEN 'no tiene filas en pc_build_events' ELSE 'no es el estado de su última fila en pc_build_events' END AS detalle
FROM sales.pc_builds b
LEFT JOIN (SELECT pc_build_id, max(occurred_at) AS instante FROM sales.pc_build_events GROUP BY pc_build_id) u ON u.pc_build_id = b.id
WHERE NOT EXISTS (SELECT 1 FROM sales.pc_build_events e
                  WHERE e.pc_build_id = b.id AND e.occurred_at = u.instante AND e.status = b.status)
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D06 · problema · Transferencias cuyo estado no es el de su bitácora
-- Regla B-04 (ERD §3): stock_transfers.status es estado materializado y stock_transfer_events guarda cada transición.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D06 · problema · Transferencias cuyo estado no es el de su bitácora'
SELECT 'inventory.stock_transfers' AS tabla, 'status' AS columna,
       t.number || ' (' || t.status || '): ' ||
       CASE WHEN u.instante IS NULL THEN 'no tiene filas en stock_transfer_events' ELSE 'no es el estado de su última fila en stock_transfer_events' END AS detalle
FROM inventory.stock_transfers t
LEFT JOIN (SELECT transfer_id, max(occurred_at) AS instante FROM inventory.stock_transfer_events GROUP BY transfer_id) u ON u.transfer_id = t.id
WHERE NOT EXISTS (SELECT 1 FROM inventory.stock_transfer_events e
                  WHERE e.transfer_id = t.id AND e.occurred_at = u.instante AND e.status = t.status)
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D07 · problema · Casos de garantía cuyo estado no es el de su bitácora
-- Regla T-05 (ERD §9.5): warranty_claims.status es estado materializado y warranty_claim_events guarda cada cambio.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D07 · problema · Casos de garantía cuyo estado no es el de su bitácora'
SELECT 'service.warranty_claims' AS tabla, 'status' AS columna,
       w.number || ' (' || w.status || '): ' ||
       CASE WHEN u.instante IS NULL THEN 'no tiene filas en warranty_claim_events' ELSE 'no es el estado de su última fila en warranty_claim_events' END AS detalle
FROM service.warranty_claims w
LEFT JOIN (SELECT claim_id, max(occurred_at) AS instante FROM service.warranty_claim_events GROUP BY claim_id) u ON u.claim_id = w.id
WHERE NOT EXISTS (SELECT 1 FROM service.warranty_claim_events e
                  WHERE e.claim_id = w.id AND e.occurred_at = u.instante AND e.status = w.status)
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D08 · problema · Cola del correo distinta de su bitácora de intentos
-- V7 (ERD §11.3 y §11.4): outgoing_mail_dispatch.attempts es el contador materializado de la cola y debe ser el número de
-- filas de outgoing_mail_attempts de ese correo (se guardan en el mismo SaveChanges); un correo «Sent» tiene un intento
-- exitoso.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D08 · problema · Cola del correo distinta de su bitácora de intentos'
SELECT 'integration.outgoing_mail_dispatch' AS tabla, 'attempts' AS columna,
       'correo ' || d.outgoing_mail_id || ' (' || d.status || '): ' ||
       CASE WHEN d.attempts <> coalesce(a.filas, 0)
                THEN d.attempts || ' intentos en la cola y ' || coalesce(a.filas, 0) || ' filas en outgoing_mail_attempts'
            ELSE 'figura enviado sin un intento exitoso' END AS detalle
FROM integration.outgoing_mail_dispatch d
LEFT JOIN (SELECT tenant_id, outgoing_mail_id, count(*) AS filas, bool_or(succeeded) AS exito
           FROM integration.outgoing_mail_attempts
           GROUP BY tenant_id, outgoing_mail_id) a ON a.tenant_id = d.tenant_id AND a.outgoing_mail_id = d.outgoing_mail_id
WHERE d.attempts <> coalesce(a.filas, 0)
   OR (d.status = 'Sent' AND NOT coalesce(a.exito, false))
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D09 · problema · Outbox con entregas de rondas que no registró
-- Regla B-06 (ERD §4 integration.outbox_dispatch): rounds cuenta las rondas de entrega hechas y cada entrega guarda su
-- número de ronda en el mismo SaveChanges; ninguna entrega puede ser de una ronda posterior a las registradas.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D09 · problema · Outbox con entregas de rondas que no registró'
SELECT 'integration.outbox_dispatch' AS tabla, 'rounds' AS columna,
       'evento ' || d.outbox_event_id || ': ' || d.rounds || ' rondas registradas y entregas hasta la ronda ' || w.ultima AS detalle
FROM integration.outbox_dispatch d
JOIN (SELECT tenant_id, outbox_event_id, max(attempt) AS ultima
      FROM integration.webhook_deliveries
      GROUP BY tenant_id, outbox_event_id) w ON w.tenant_id = d.tenant_id AND w.outbox_event_id = d.outbox_event_id
WHERE w.ultima > d.rounds
ORDER BY 3;

-- ---------------------------------------------------------------------------------------------------------------------
-- @comprobacion D10 · problema · Piezas sin ranura fuera de un carrito
-- Regla P-05 (ERD §11.1): la ranura nula solo se admite en las líneas de un carrito (lo exigen el dominio y el trigger
-- trg_pc_build_line_slot); en un armado cada pieza tiene su ranura.
-- ---------------------------------------------------------------------------------------------------------------------
\echo 'D10 · problema · Piezas sin ranura fuera de un carrito'
SELECT 'sales.pc_build_lines' AS tabla, 'slot' AS columna,
       'línea ' || l.id || ' de ' || b.number || ' (' || b.kind || ') sin ranura' AS detalle
FROM sales.pc_build_lines l
JOIN sales.pc_builds b ON b.tenant_id = l.tenant_id AND b.id = l.pc_build_id
WHERE l.slot IS NULL AND b.kind <> 'Cart'
ORDER BY 3;

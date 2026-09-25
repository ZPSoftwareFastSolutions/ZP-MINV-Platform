-- =====================================================================================================================
-- M-INV V3 · Inicialización de la base de datos PostgreSQL (96 tablas, 7 esquemas, 5FN)
-- Z&P Software Fast Solutions
--
-- ARCHIVO GENERADO por tools/build_v3.ps1 (cabecera + «dotnet ef migrations script --idempotent»). No lo edite a mano:
-- cambie el modelo (src/2. Infrastructure/MINV.Infrastructure) y regenere. Es idempotente: se puede volver a ejecutar.
--
-- Requisitos: PostgreSQL 15 o superior (se recomienda 16).
--
-- 1) Como superusuario (psql -U postgres), una sola vez:
--      CREATE ROLE minv_owner LOGIN PASSWORD '<clave del dueño>';
--      CREATE ROLE minv_app   LOGIN PASSWORD '<clave de la aplicación>';
--      CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0;
-- 2) Como dueño, conectado a la base «minv»:
--      psql -U minv_owner -d minv -v ON_ERROR_STOP=1 -f scripts/db_init.sql
-- 3) La aplicación se conecta como minv_app (sujeto a Row Level Security; sin UPDATE/DELETE en los libros mayores):
--      MINV_DB = "Host=localhost;Port=5432;Database=minv;Username=minv_app;Password=<clave de la aplicación>"
--
-- Contenido: esquemas iam, catalog, warehouse, inventory, purchasing, sales y accounting; tablas con PK, FK compuestas
-- (tenant_id, id), restricciones CHECK e índices únicos; catálogo de módulos comerciales; triggers append-only, un valor
-- por atributo y asientos cuadrados; Row Level Security por tenant; vistas v_stock_by_variant, v_conservation_breaches y
-- v_activity; permisos de minv_app.
-- =====================================================================================================================

DO $$
BEGIN
    IF current_setting('server_version_num')::int < 150000 THEN
        RAISE EXCEPTION 'M-INV V3 requiere PostgreSQL 15 o superior (servidor: %)', current_setting('server_version');
    END IF;
END;
$$;


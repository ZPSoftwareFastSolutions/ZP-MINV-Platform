-- =====================================================================================================================
-- M-INV V4.1 · Inicialización de la base de datos PostgreSQL (140 tablas, 9 esquemas + modelo de lectura, 5FN)
-- Z&P Software Fast Solutions
--
-- ARCHIVO GENERADO por tools/build_v3.ps1 (cabecera + «dotnet ef migrations script --idempotent»). No lo edite a mano:
-- cambie el modelo (src/2. Infrastructure/MINV.Infrastructure) y regenere. Es idempotente: se puede volver a ejecutar.
--
-- Requisitos: PostgreSQL 15 o superior (se recomienda 16). En la nube: DigitalOcean, AWS RDS o Supabase (con PgBouncer
-- use el modo SESIÓN o la conexión directa: la seguridad por filas usa variables de sesión).
--
-- 1) Como superusuario o administrador (una sola vez):
--      CREATE ROLE minv_owner  LOGIN PASSWORD '<clave del dueño>';
--      CREATE ROLE minv_server LOGIN NOBYPASSRLS PASSWORD '<clave del servidor>';   -- V4: servidor en la nube y API Gateway
--      CREATE ROLE minv_app    LOGIN NOBYPASSRLS PASSWORD '<clave de la aplicación>'; -- escritorio con conexión directa
--      CREATE DATABASE minv OWNER minv_owner ENCODING 'UTF8' TEMPLATE template0;
--    (o bien: minv roles --clave-servidor … --clave-app … · tools\bd_nube.ps1 hace todo)
-- 2) Como dueño, conectado a la base «minv» (los roles deben existir: la migración les otorga los privilegios):
--      psql -U minv_owner -d minv -v ON_ERROR_STOP=1 -f scripts/db_init.sql
-- 3) Conexiones (siempre sujetas a Row Level Security por empresa y por sucursal):
--      servidores: MINV_DB = "Host=…;Database=minv;Username=minv_server;Password=…;SSL Mode=VerifyFull"
--      escritorio directo (base local): MINV_DB = "Host=localhost;Port=5432;Database=minv;Username=minv_app;Password=…"
--
-- Contenido: esquemas iam, catalog, warehouse, inventory, purchasing, sales, accounting, integration y billing (V4.1:
-- facturación SIAT computarizada en línea); tablas con PK, FK compuestas (tenant_id, id) y (tenant_id, branch_id, id);
-- restricciones CHECK e índices únicos; catálogo de módulos comerciales; triggers append-only, un valor por atributo y
-- asientos cuadrados; Row Level Security por empresa y política restrictiva por sucursal; funciones SECURITY DEFINER
-- para el servidor (V4.1: billing.siat_active_tenants); esquema reporting (vistas materializadas + vistas filtradas);
-- vistas v_stock_by_variant, v_conservation_breaches, v_transfer_breaches, v_activity y (V4.1)
-- billing.v_fiscal_document_totals (totales fiscales derivados); privilegios de minv_app y minv_server.
-- =====================================================================================================================

DO $$
BEGIN
    IF current_setting('server_version_num')::int < 150000 THEN
        RAISE EXCEPTION 'M-INV requiere PostgreSQL 15 o superior (servidor: %)', current_setting('server_version');
    END IF;
END;
$$;


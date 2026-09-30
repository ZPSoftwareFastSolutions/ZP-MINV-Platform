# Entrega de la V7 — Algoritmo y Usuarios de Prueba

Este documento formaliza la entrega de la **versión 7.0.0-alpha.1 (Plataforma Web)** y describe el paso a paso para probar el ciclo completo de la aplicación, así como los usuarios de prueba disponibles.

## 1. Usuarios de Prueba

> **Importante:** Las contraseñas de todos los usuarios se generan aleatoriamente en su equipo al ejecutar `tools\bd_local.ps1 -Accion recrear` por razones de seguridad. Puede consultar todas las contraseñas activas en cualquier momento abriendo el archivo local:
> `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt`

### 1.1 Personal (Panel Web y Escritorio)
Todos pertenecen a la empresa de prueba **Tech Zone Gaming S.R.L. (`TECHZONE`)**.

| Rol en el sistema | Nombre de Prueba | Correo Electrónico | Módulos Principales (Panel Web) |
|:---|:---|:---|:---|
| **Administrador General** | Andrea Quiroga | `andrea@techzone.bo` | Acceso TOTAL a todos los módulos y configuración global. |
| **Gerente de Sucursal** | Carlos Mendoza | `carlos@techzone.bo` | Catálogo, Compras, Transferencias, Reportes y Aprobaciones. |
| **Encargado de Bodega** | Luis Navarro | `luis@techzone.bo` | Stock, Toma Física, Movimientos, Recepción/Despacho. |
| **Operador de Ventas** | Diego Flores | `diego@techzone.bo` | Caja, Ventas, Reservas, Clientes, SIAT. |
| **Contador** | Roberto Silva | `roberto@techzone.bo` | Libros Fiscales, Documentos Fiscales, Contabilidad y Asientos. |

### 1.2 Clientes (Tienda Web)

| Tipo de Cliente | Nombre | Correo Electrónico | Uso en Pruebas |
|:---|:---|:---|:---|
| **Cliente Frecuente** | Ana López | `ana.lopez@example.com` | Tiene historial de reservas y compras anteriores. |
| **Cliente Nuevo** | (Registro manual) | (Cualquiera real) | Úselo para probar el flujo de registro desde `/registrarse`. |

---

## 2. Algoritmo Paso a Paso (Flujo Completo Extremo a Extremo)

Siga este orden lógico para comprobar que todos los módulos de la V7 interactúan correctamente entre sí.

### Preparación del Entorno
1. **Servicios Docker:** Ejecute `docker compose -f deploy/docker-compose.yml up -d --build`. Compruebe que el proxy web, API Gateway, CloudServer, y la base de datos están corriendo.
2. **Recrear BD Local:** Ejecute `tools\bd_local.ps1 -Accion recrear` para poblar la base de datos con los datos de prueba iniciales. Anote las contraseñas generadas de `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt`.

### Fase A: La Tienda Web (El Cliente)
3. **Registro:** Entre a `http://localhost/` (o su túnel de Cloudflare) y haga clic en **«Ingresar»**. Vaya a «Registrarse» y cree un nuevo usuario cliente.
4. **Reserva Unitaria:** Navegue por el catálogo, abra la ficha de una Laptop y presione **«Reservar ahora»**. Llene los datos opcionales de facturación y el plazo de recojo. Obtendrá el código `RES-WEB-...`.
5. **Carrito de Compras:** Agregue varios productos sueltos al carrito y proceda a reservar el lote completo.

### Fase B: El Panel Web (El Personal Operativo)
6. **Revisión de Reserva:** Abra una ventana en Incógnito, ingrese a `/panel` e inicie sesión como **Operador de Ventas** (Diego Flores).
7. **Caja y Facturación:** Vaya al módulo **«Caja»**, cargue la reserva web (busque el código). La caja pre-llenará los datos del cliente. Complete el cobro para emitir la factura computarizada (SIAT).
8. **Documentos Fiscales:** Vaya a **«Documentos Fiscales»** y busque la venta recién hecha. Abra el detalle e imprima el comprobante (`PrintDialog`).

### Fase C: Compras e Inventario (La Bodega y Gerencia)
9. **Crear Orden:** Inicie sesión como **Gerente de Sucursal** (Carlos Mendoza). Vaya a **«Compras»** y cree una nueva Orden de Compra para reabastecer los productos vendidos. Apruébela.
10. **Recepción:** Cambie la sesión a **Encargado de Bodega** (Luis Navarro). Abra la Orden de Compra y confirme la **Recepción de Mercadería**. Verifique en el módulo **«Stock»** que el inventario se ha incrementado.
11. **Toma Física:** En **«Toma Física»**, inicie un conteo ciego, registre una diferencia intencional y vea cómo el sistema genera el ajuste de stock.

### Fase D: Análisis Contable y SIAT (El Contador y Administrador)
12. **Asientos Automáticos:** Inicie sesión como **Contador** (Roberto Silva). Vaya a **«Contabilidad»** y revise el **Libro Diario**. Verifique que la Venta y la Compra generaron asientos contables automáticamente.
13. **Verificación SIAT:** Inicie sesión como **Administrador General** (Andrea Quiroga). En el módulo **«SIAT»**, verifique que el estado del CUFD sea «Activo» y que no haya paquetes pendientes de envío en contingencia.
14. **Reportes:** Vaya a **«Reportes»**, genere el informe de Ventas del mes y expórtelo a archivo **CSV** para comprobar el formato de columnas.

> **Finalización exitosa:** Al completar este algoritmo, habrá verificado la funcionalidad end-to-end (E2E) abarcando M1, M3, M4, M6, M7, M8, M9, M10, B1-B5 y W1-W3.

# Entrega de la V7 — Algoritmo y Usuarios de Prueba

Este documento formaliza la entrega de la **versión 7.0.0-alpha.1 (Plataforma Web)** y describe el paso a paso para probar el ciclo completo de la aplicación, así como los usuarios de prueba disponibles. La guía detallada está en `docs/deployment/inicio-rapido-v7.md`.

## 1. Usuarios de Prueba

> **Importante:** Las contraseñas de todos los usuarios se generan al azar en su equipo al ejecutar `tools\bd_local.ps1 -Accion recrear`, por seguridad nunca se escriben en el repositorio. Consúltelas en el archivo local:
> `%LOCALAPPDATA%\M-INV\usuarios-prueba.txt`

### 1.1 Personal (panel web `/panel` y escritorio `M-INV.exe`)
Todos pertenecen a la empresa de prueba **Tech Zone Gaming S.R.L. (`TECHZONE`)**. El administrador tiene un correo fijo; el resto del personal recibe en cada carga un nombre al azar con el correo `nombre.apellido@techzone.example`. Los nombres exactos, sus sucursales y las contraseñas están en `usuarios-prueba.txt`.

| Rol en el sistema | Cuántos | Correo | Sucursales | Qué ve en el panel web |
|:---|:---:|:---|:---|:---|
| **Administrador** | 1 | `admin@techzone.example` | Todas | Todos los módulos, incluidos Usuarios, Integraciones, Configuración, SIAT, Homologación, Libros y Contabilidad. |
| **Gerencia** | 1 | `nombre.apellido@techzone.example` | Todas (gerencia global) | Todas las sucursales: Reportes, Ventas, Reservas, Compras, Stock, Transferencias, Series, Garantías, Contabilidad, Actividad y anulación de facturas. |
| **Bodega** | 3 | `nombre.apellido@techzone.example` | CM, CB y SC (uno por sucursal) | Stock, Alertas, Movimientos, Toma física, Compras, Transferencias, Series, Garantías y Reportes. |
| **Ventas** | 2 | `nombre.apellido@techzone.example` | CM y SC | Caja, Ventas, Reservas, Clientes, Armador de PC, Documentos fiscales, Garantías (abrir casos), Stock y Reportes. |
| **Cajero** | 4 | `nombre.apellido@techzone.example` | 2 en CM, 1 en CB y 1 en SC | Caja (turno, cobro y venta de reservas), Ventas, Reservas, Clientes, Documentos fiscales y Stock. |
| **Consulta** | 1 | `nombre.apellido@techzone.example` | CM, CB y SC | Solo lectura: Stock, Catálogo, Series, Documentos fiscales y Reportes. |

### 1.2 Clientes (tienda web, botón «Ingresar»)
Estas cuentas NO ingresan al escritorio ni al panel del personal. Sus contraseñas están en la sección «Clientes de la tienda web» de `usuarios-prueba.txt`.

| Cliente | Correo | Uso en pruebas |
|:---|:---|:---|
| **Rocío Villca Choque** | `rocio.villca@correo.example` | Tiene reservas propias para ver en «Mi cuenta». |
| **Marcelo Quisbert Loza** | `marcelo.quisbert@correo.example` | Segunda cuenta con reservas propias. |
| Cliente nuevo | el que usted registre | Para probar «Crear cuenta» desde «Ingresar». |

---

## 2. Algoritmo Paso a Paso (Flujo Completo Extremo a Extremo)

Siga este orden para comprobar que todos los módulos de la V7 interactúan correctamente entre sí.

### Preparación del Entorno
1. **Recrear la base local:** `powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear`. Antes de borrar, respalda la base anterior en `%LOCALAPPDATA%\M-INV\respaldos`. Las contraseñas quedan en `usuarios-prueba.txt`.
2. **Docker:** `powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir -Correo prueba -ReiniciarSimulador`. Al terminar muestra el enlace público; la web local es http://localhost:5173 y el buzón de correo de prueba http://127.0.0.1:8025.

### Fase A: La Tienda Web (El Cliente)
3. **Registro:** Entre a http://localhost:5173 (o al enlace público), pulse **«Ingresar»** y luego **«Crear cuenta»**. Una contraseña o un correo equivocados nunca dejan pasar.
4. **Compra de un solo producto:** Abra la ficha de un producto (por ejemplo un monitor), agréguelo al carrito y pulse **«Reservar»**. La pantalla de reserva muestra SOLO ese producto (no obliga a pasar por «Arma tu PC»). Complete sus datos y los de la factura y el plazo para recoger (1 a 3 días). Obtendrá el código `RES-WEB-…`.
5. **Correo:** En http://127.0.0.1:8025 verá el correo con el código y el detalle de la reserva (con `-Correo real` sale desde zapasoftwarefastsolutions@gmail.com).
6. **Buscar la reserva:** En «Mi reserva» búsquela con el **código de reserva O con el número de celular** (basta uno de los dos). Para cancelarla se piden ambos.
7. **Carrito de varios productos:** Agregue varios productos sueltos al carrito y reserve el lote completo.

### Fase B: El Panel Web (El Personal)
8. **Revisión de la reserva:** En una ventana de incógnito, entre a `/panel` con un usuario **Cajero** de CM. Las funciones son botones; las estadísticas aparecen solo al pulsar «Ver estadísticas ^».
9. **Caja y facturación:** En **«Caja»** abra el turno, cargue la reserva por su código (la caja precarga los datos de factura) y cobre: se emite la factura computarizada (SIAT, simulador).
10. **Documentos fiscales:** En **«Documentos fiscales»** busque la venta, abra el detalle e imprima el comprobante.
11. **El cliente la ve «Vendida»:** Vuelva a la tienda con la cuenta del cliente: en «Mi cuenta» la reserva figura como vendida.

### Fase C: Compras e Inventario
12. **Crear orden:** Con el **Administrador** (o Gerencia), en **«Compras»** cree una orden de compra para reponer lo vendido y apruébela.
13. **Recepción:** Con un usuario **Bodega** de CM, abra la orden y registre la recepción (series o IMEI cuando el producto las lleva). Verifique en **«Stock»** que subió el inventario.
14. **Toma física:** En **«Toma física»** inicie un conteo, registre una diferencia y genere el ajuste.

### Fase D: Contabilidad, SIAT y Reportes (Administrador)
15. **Asientos:** En **«Contabilidad»** revise el **Libro diario**: la venta y la compra generaron sus asientos.
16. **SIAT:** En **«SIAT»** verifique que el CUFD esté vigente y que no haya paquetes pendientes.
17. **Reportes:** En **«Reportes»** abra las ventas del mes y expórtelas a **CSV**.

> **Finalización exitosa:** Al completar este algoritmo habrá recorrido la tienda (cuenta, carrito, reserva, correo y búsqueda), el panel por rol, la caja con factura y los módulos de compras, inventario, contabilidad, SIAT y reportes.

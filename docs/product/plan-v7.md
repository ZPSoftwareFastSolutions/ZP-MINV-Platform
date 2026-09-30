# Plan de la Versión 7 · M-INV (rama `Inventario-V7`)

Tema: **todo el sistema en la web**. La tienda gana inicio de sesión y registro, carrito de compras y correo automático
de la reserva; el personal usa un **panel web por rol** con las funciones del escritorio; el escritorio gana botones,
filtros y funciones. Se construye sobre `Inventario-V6`.

Estado de cada tarea: `[x]` terminada · `[ ]` pendiente.

## Tareas

- [x] 1. Crear la rama `Inventario-V7` desde `Inventario-V6`.
- [x] 2. Mapa del sistema actual (casos de uso, escritorio, web, base de datos, seguridad, despliegue).
- [x] 3. Diseño de la V7 y sus reglas (`docs/architecture/plataforma-web-v7.md`, `.claude/v7-web-platform-rules.md`).
- [x] 4. Servidor: carrito → reserva de cualquier producto (un solo artículo o varios, sin pasar por «Armá tu PC»).
- [x] 5. Servidor: cuentas web (iniciar sesión y registrarse), roles y sesión segura.
- [x] 6. Servidor: correo automático con el código y el detalle de la reserva (remitente `zapasoftwarefastsolutions@gmail.com`).
- [x] 7. Base de datos: migración V7 y comprobación de normalización con informe.
- [x] 8. Web: botón «Ingresar» y pantallas de inicio de sesión y registro.
- [x] 9. Web: carrito de compras y página de reserva con los datos del cliente.
- [x] 10. Web: panel por rol separado por módulos, con botones, listas desplegables y filtros.
- [x] 11. Web: las funciones del escritorio (stock, catálogo, caja, ventas, clientes, compras, proveedores,
      transferencias, reservas, series y garantías, facturación, reportes, contabilidad, usuarios).
- [x] 12. Web: tablero simplificado (funciones como botones; estadísticas solo al pulsar «Ver»).
- [x] 13. Web: panel del cliente (mis reservas, mis datos).
- [x] 14. Escritorio: arreglos, más botones, filtros y funciones.
- [x] 15. Calidad: pruebas del servidor, de la web y recorrido completo en un navegador real.
- [x] 16. Ciberseguridad: revisión de accesos, sesiones y datos, con sus correcciones.
- [x] 17. Docker: todo en el mismo Docker Desktop, con enlace público.
- [ ] 18. Documentación, commit y push de `Inventario-V7`.
- [ ] 19. Entrega: algoritmo paso a paso y tabla de usuarios de prueba.

## Decisiones

- **Registrarse** crea una cuenta de **Cliente** (sus reservas y sus datos). Las cuentas del personal las crea el
  Administrador: nadie se registra solo con un rol del personal.
- **La web es otro cliente del servidor en la nube** (`MINV.CloudServer`): usa los mismos casos de uso, permisos y
  auditoría que el escritorio. El servidor decide qué puede hacer cada rol; la web solo oculta lo que no corresponde.
- **Correo**: el envío queda construido y probado con un buzón de prueba. Para que salga desde la cuenta de Gmail de
  Z&P hace falta la «contraseña de aplicación» de esa cuenta, que solo puede generar su dueño.

# Reglas de la plataforma web · M-INV V7

> **Documento normativo** para la rama `Inventario-V7`. **DEBE** = obligatorio · **NO DEBE** = prohibido · **PUEDE** =
> permitido. Complementa A-xx (V3), B-xx (V4), F-xx (V4.1), T-xx (V4.2), W-xx (V5) y S-xx (V6); donde hablen de lo mismo
> (sesión web, cuentas de cliente, carrito, correo, panel), **prevalece esta**. Diseño: `docs/architecture/plataforma-web-v7.md`.

### P-01 · El panel web es otro cliente del servidor en la nube
- El panel DEBE enviar los mismos `IRequest<>` de `MINV.Application` por `POST /api/v1/web/rpc`. NO DEBE existir lógica
  de negocio en la web ni rutas nuevas que dupliquen un caso de uso.
- Permisos, módulo, sucursal, validación y auditoría los decide el servidor en CADA petición. Ocultar un botón en la web es
  comodidad, no seguridad.

### P-02 · Sesión web por cookie
- El token de sesión DEBE viajar solo en una cookie `HttpOnly`, `SameSite=Strict`, `Path=/api/v1/web` y `Secure` bajo
  https. NO DEBE devolverse en el cuerpo ni guardarse en `localStorage`, `sessionStorage` ni en el estado de la página.
- Toda ruta `/api/v1/web/*` que cambia algo DEBE exigir la cabecera `X-MINV-Client-Version` y rechazar peticiones de otro
  origen (`Sec-Fetch-Site`, `Origin`). El servidor en la nube NO DEBE publicar CORS.
- La empresa DEBE salir de la configuración (`Minv:Web:TenantCode`), nunca del cuerpo de la petición.

### P-03 · Inicio de sesión y registro
- Una falla de inicio de sesión DEBE responder con un mensaje único (no distingue correo inexistente de contraseña
  incorrecta). Bloqueo por intentos y límites por IP real obligatorios.
- El registro DEBE crear SIEMPRE una cuenta con el rol `CLIENTE`; NO DEBE aceptar un rol, una sucursal ni permisos del
  cliente. El personal lo crea el Administrador.
- Las contraseñas NO DEBEN aparecer en registros, auditoría, respuestas ni en el estado persistente de la web.

### P-04 · Cuentas de cliente
- Los casos de uso `account.*` DEBEN operar únicamente sobre el cliente ligado al usuario de la sesión
  (`sales.customer_accounts`); NO DEBEN recibir un identificador de cliente ni de usuario.
- Una sesión cuyo único rol es `CLIENTE` solo PUEDE ejecutar casos de uso con permisos `account.*` y los de su propia
  sesión. La lista se comprueba en el servidor, en las dos rutas de RPC.

### P-05 · Carrito = `PcBuild` de tipo `Cart`
- Una reserva de carrito DEBE seguir S-03 y S-04 (armado + reservas de stock en UNA transacción, mismos estados y
  bitácora). La compatibilidad (T-06) se evalúa SOLO en armados.
- `slot` nulo solo en líneas de un carrito. Un carrito NO DEBE publicarse en la web como armado sugerido.
- Los datos para la factura de una reserva son una instantánea del visitante: siguen S-06 (enmascarados en la auditoría,
  nunca en la API pública, visibles solo con `sales.pcbuild.manage`).

### P-06 · Correo saliente
- NO DEBE enviarse un correo dentro de un caso de uso de negocio ni antes del COMMIT (B-08): se encola en
  `integration.outgoing_mails` + `outgoing_mail_dispatch` en el MISMO `SaveChanges` y lo envía `MailDispatcher`.
- Cada intento DEBE quedar en `integration.outgoing_mail_attempts` (append-only). NO DEBE guardarse el asunto ni el cuerpo.
- El asunto DEBE armarse solo con datos del servidor; todo valor variable del cuerpo DEBE ir codificado; NO DEBEN
  incluirse notas ni texto libre del cliente. Topes por destinatario y por empresa obligatorios.
- La contraseña SMTP NO DEBE versionarse ni imprimirse: vive cifrada en la base o en `deploy/.env`.

### P-07 · Contrato TypeScript generado
- `contract.generated.ts` DEBE generarse con `minv contrato-web`; NO DEBE editarse a mano. Una prueba DEBE fallar si está
  desactualizado. Los módulos del panel NO DEBEN declarar a mano tipos de peticiones o respuestas del servidor.

### P-08 · Capas de la web
- Siguen las capas de la V5. `fetch` solo en `3-infrastructure/http/*`; `localStorage` solo en
  `3-infrastructure/storage/*` y solo para el carrito. NO DEBE guardarse en el navegador nada de la sesión.
- Los puertos nuevos (`ISessionGateway`, `IRpcGateway`, `ICartStore`) viven en `1-domain/ports`.

### P-09 · Módulos del panel
- Cada módulo vive en `4-presentation/panel/modules/<módulo>/` y se registra solo con su `module.tsx`. Un módulo NO DEBE
  importar de otro módulo ni modificar el esqueleto (`shell/`) o el conjunto de componentes (`kit/`).
- Un módulo DEBE declarar los permisos que lo muestran; el menú se arma con los permisos de la sesión.

### P-10 · Tablero y pantallas
- El tablero DEBE mostrar las funciones del rol como botones. Las estadísticas DEBEN estar plegadas detrás de «Ver» y
  cargarse al abrirlas. NO DEBEN mostrarse gráficos ni indicadores al entrar.
- Toda lista DEBE tener: filtros con listas desplegables, búsqueda, estados de carga, vacío y error con «Reintentar».
- Los textos visibles DEBEN estar en español y sin términos técnicos.

### P-11 · Superficie pública
- El nginx del catálogo DEBE publicar solo la web, `/storefront/` y `/api/v1/web/`. NO DEBEN salir por el túnel las rutas
  del escritorio, `/docs`, la API B2B ni el buzón de prueba.
- Cabeceras obligatorias: `Content-Security-Policy` (solo `'self'`), `X-Content-Type-Options`, `Referrer-Policy`,
  `Permissions-Policy` y `frame-ancestors 'none'`.

### P-12 · Escritorio
- Las funciones nuevas del escritorio DEBEN usar los mismos casos de uso que el panel web. Siguen A-09 y T-08 (paleta,
  textos en español, sin lógica de negocio en las vistas).

### P-13 · Datos de prueba
- La empresa de prueba DEBE traer al menos dos cuentas de cliente con reservas propias, una reserva de carrito activa y
  correos en cola. Sus contraseñas van en `usuarios-prueba.txt` (A-13), nunca en el repositorio.

### P-14 · Definición de terminado
Además de B-17, T-10 y S-10: pasan las pruebas de la sesión web (cookie, CSRF, límites, lista de permitidos del
cliente), del carrito, del correo (encolado, envío, reintentos, reserva cancelada) y del generador del contrato; la web
pasa `typecheck`, `lint`, `test` y `build`; y un recorrido en un navegador real contra Docker: registrarse → agregar un
producto al carrito → reservar → llega el correo al buzón de prueba → el personal inicia sesión, ve la reserva en el panel
y en el escritorio → venderla en caja → el cliente la ve «Vendida» en su cuenta.

## Checklist para agentes
- [ ] ¿El token de sesión aparece en una respuesta, en el almacenamiento del navegador o en un registro? → rechazar (P-02).
- [ ] ¿Un caso de uso `account.*` recibe el id de un cliente? → rechazar (P-04).
- [ ] ¿Un correo enviado dentro de un manejador de negocio? → encolar (P-06).
- [ ] ¿Un tipo del servidor escrito a mano en un módulo del panel? → usar el contrato generado (P-07).
- [ ] ¿Un módulo importa de otro módulo o toca `shell/` o `kit/`? → rechazar (P-09).
- [ ] ¿Un tablero con gráficos visibles al entrar? → plegarlos (P-10).

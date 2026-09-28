# Tienda pública en Docker · M-INV V6 (enlace para los clientes)

La tienda web de la V6 (catálogo, fichas, disponibilidad y reservas de armados sobre la MISMA base del escritorio) se publica
desde este equipo con **Docker Desktop** y un **túnel de Cloudflare** (trycloudflare): los clientes abren un enlace
`https://…trycloudflare.com` desde cualquier lugar, sin abrir puertos del router ni contratar un servidor.

```text
 Cliente (internet) ──https──▶ Cloudflare ──túnel──▶ contenedor «tunel» (cloudflared)
                                                          │ http://webcatalog:80 (red interna de Docker)
                                                          ▼
                              contenedor «webcatalog» (nginx): la web + /storefront/ ──▶ contenedor «apigateway»
                                                                                              │ host.docker.internal:5432
 Escritorio M-INV.exe ──http://localhost:5080──▶ contenedor «cloudserver» ──────────────────▶ PostgreSQL de este equipo
                                                  └─ «siat-simulador» (misma red, :5095)
```

- **Un solo enlace** sirve la web y su API: la web se construye con `VITE_API_URL=/` y su nginx reenvía `/storefront/` al
  gateway. Solo se publica la tienda: `/docs`, la API B2B `/v1` y el servidor del escritorio **no** salen a internet.
- El gateway confía en `X-Forwarded-For` SOLO desde la red interna de Docker (`Minv:ForwardedHeaders`): el límite de 300
  lecturas y 10 reservas por minuto es **por cliente** (IP real que entrega Cloudflare), no uno para todos.
- Los puertos de los contenedores quedan en `127.0.0.1` (este equipo): catálogo `:5173`, gateway `:5090`, nube `:5080`,
  simulador del SIN `:5095`.

## El algoritmo

```text
 1. Requisitos: Docker Desktop encendido («Engine running») y la base local de la V6
      (powershell -ExecutionPolicy Bypass -File tools\bd_local.ps1 -Accion recrear, si todavía no existe).
 2. Subir todo (la primera vez construye las imágenes: 5 a 10 minutos):
      powershell -ExecutionPolicy Bypass -File tools\docker_local.ps1 -Accion subir
    Al final muestra «ENLACE PÚBLICO PARA TUS CLIENTES: https://….trycloudflare.com» y lo guarda en
    %LOCALAPPDATA%\M-INV\enlace-publico.txt.
 3. Compartir ese enlace con los clientes: ven los productos con la disponibilidad real y pueden reservar armados.
 4. El escritorio: M-INV.exe → «Nube» → http://localhost:5080 → empresa TECHZONE (o «Base local», igual que antes).
 5. Mantenerlo vivo:
    · Los contenedores se reinician solos (restart: unless-stopped) mientras Docker Desktop esté abierto.
    · Al iniciar sesión, la tarea «M-INV Tienda publica» enciende PostgreSQL, abre Docker Desktop si está cerrado, levanta
      los contenedores y guarda el enlace nuevo (registrarla una vez:  tools\docker_local.ps1 -Accion arranque; su
      registro queda en %LOCALAPPDATA%\M-INV\docker-local.log).
    · El equipo debe quedar encendido y sin suspenderse (Configuración › Sistema › Energía: «Nunca» con corriente).
 6. Ver el enlace vigente:   tools\docker_local.ps1 -Accion enlace      Estado:  -Accion estado      Apagar:  -Accion bajar
```

## El enlace cambia al reiniciar

El túnel rápido de trycloudflare no necesita cuenta, pero **su dirección cambia** cada vez que el contenedor `tunel` se
reinicia (reinicio del equipo, de Docker Desktop o `-Accion bajar`/`subir`). El enlace vigente siempre está en
`%LOCALAPPDATA%\M-INV\enlace-publico.txt` y con `-Accion enlace`.

Para un enlace **fijo** (por ejemplo `tienda.suempresa.com`) hace falta una cuenta gratuita de Cloudflare con un dominio:
se crea un túnel con nombre en *Zero Trust › Networks › Tunnels*, se copia su token y el servicio `tunel` pasa a
`command: tunnel --no-autoupdate run --token <token>` (el token va en `deploy/.env`, nunca en el repositorio), apuntando el
«Public hostname» a `http://webcatalog:80`.

## Límites

- Es la base de datos **de este equipo**: si el equipo se apaga, la tienda deja de responder.
- trycloudflare es para pruebas y demostraciones (Cloudflare no garantiza disponibilidad); para operar con clientes de forma
  permanente conviene el túnel con nombre o el despliegue en la nube de `docs/deployment/despliegue-nube-v4.md` §14.
- El simulador del SIN es de **simulación**: las facturas no tienen valor legal (ambiente de pruebas).

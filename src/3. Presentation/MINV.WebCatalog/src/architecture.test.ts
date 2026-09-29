// Prueba de arquitectura: las dependencias solo apuntan hacia adentro (1-domain ← 2-application ← 4-presentation) y la
// infraestructura entra únicamente por el punto de composición `4-presentation/app/container.ts`. V6 (regla S-07): la
// red vive SOLO en `3-infrastructure/http`. V7 (reglas P-02, P-07, P-08, P-09 y P-11): `fetch` solo en `api.ts`
// (tienda) y `webApi.ts` (sesión y RPC); `localStorage` solo en `3-infrastructure/storage/*` y solo para el carrito;
// NADA de la sesión se guarda en el navegador; el contrato generado se usa a través de un único adaptador; el panel se
// descarga aparte y sus módulos no se importan entre sí; nada de HTML ni código inyectado. V7 · W3b: un módulo del panel
// importa solo el conjunto del panel (kit, hooks, lib y registro), el contrato, las rutas y su propia carpeta, y no
// declara a mano tipos del servidor.
// Lee el código fuente de src/ (sin las pruebas ni sus utilidades) y revisa cada `import`.

import { describe, expect, it } from 'vitest';

const SOURCES = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}', '!./test-setup.ts', '!./test-utils.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

const REACT_LIKE = /^(react|react-dom|react-router|react-router-dom|lucide-react|clsx)(\/|$)/;

/** Carpeta del único almacenamiento permitido (el carrito, paquete W2). */
const STORAGE_FOLDER = './3-infrastructure/storage/';

function importsOf(source: string): string[] {
  return [...source.matchAll(/(?:^|\n)\s*(?:import|export)\s[^;]*?\sfrom\s+['"]([^'"]+)['"]/g)].map((match) => match[1]);
}

function filesIn(layer: string): [string, string[]][] {
  return Object.entries(SOURCES)
    .filter(([path]) => path.startsWith(`./${layer}/`))
    .map(([path, source]) => [path, importsOf(source)]);
}

function violations(layer: string, forbidden: (specifier: string, path: string) => boolean): string[] {
  return filesIn(layer).flatMap(([path, specifiers]) =>
    specifiers.filter((specifier) => forbidden(specifier, path)).map((specifier) => `${path} → ${specifier}`),
  );
}

/** Código sin comentarios: las reglas que buscan una palabra no deben tropezar con la documentación. */
function code(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`\\])\/\/[^\n]*/g, '$1');
}

/** Archivos cuyo CÓDIGO (sin comentarios) cumple el patrón. */
function offenders(pattern: RegExp): string[] {
  return Object.entries(SOURCES)
    .filter(([, source]) => pattern.test(code(source)))
    .map(([path]) => path);
}

describe('arquitectura limpia del catálogo web', () => {
  it('lee el código fuente de las cuatro capas', () => {
    expect(filesIn('1-domain').length).toBeGreaterThan(5);
    expect(filesIn('2-application').length).toBeGreaterThan(2);
    expect(filesIn('3-infrastructure').length).toBeGreaterThan(1);
    expect(filesIn('4-presentation').length).toBeGreaterThan(20);
  });

  it('1-domain no conoce React, la aplicación, la infraestructura ni la presentación', () => {
    expect(
      violations(
        '1-domain',
        (specifier) =>
          REACT_LIKE.test(specifier) ||
          specifier.startsWith('@/2-application') ||
          specifier.startsWith('@/3-infrastructure') ||
          specifier.startsWith('@/4-presentation'),
      ),
    ).toEqual([]);
  });

  it('2-application solo depende del dominio y de shared', () => {
    expect(
      violations(
        '2-application',
        (specifier) =>
          REACT_LIKE.test(specifier) ||
          specifier.startsWith('@/3-infrastructure') ||
          specifier.startsWith('@/4-presentation'),
      ),
    ).toEqual([]);
  });

  it('3-infrastructure implementa los puertos del dominio; de la aplicación solo conoce el contrato de la tienda (storefront)', () => {
    expect(
      violations(
        '3-infrastructure',
        (specifier) =>
          REACT_LIKE.test(specifier) ||
          (specifier.startsWith('@/2-application') && !specifier.startsWith('@/2-application/storefront')) ||
          specifier.startsWith('@/4-presentation'),
      ),
    ).toEqual([]);
    // Y el contrato lo usa únicamente el adaptador HTTP (el mock de la V5 habla el dominio directamente).
    expect(
      violations('3-infrastructure', (specifier, path) => specifier.startsWith('@/2-application') && !path.startsWith('./3-infrastructure/http/')),
    ).toEqual([]);
  });

  it('4-presentation importa 3-infrastructure solo en app/container.ts', () => {
    expect(
      violations(
        '4-presentation',
        (specifier, path) => specifier.startsWith('@/3-infrastructure') && path !== './4-presentation/app/container.ts',
      ),
    ).toEqual([]);
    expect(filesIn('4-presentation').some(([path, specifiers]) => path === './4-presentation/app/container.ts' && specifiers.some((s) => s.startsWith('@/3-infrastructure')))).toBe(true);
  });

  it('shared es puro: sin React ni capas', () => {
    expect(violations('shared', (specifier) => REACT_LIKE.test(specifier) || specifier.startsWith('@/'))).toEqual([]);
  });

  it('`localStorage` vive solo en 3-infrastructure/storage/* (el carrito); ningún otro almacenamiento del navegador (reglas S-07 y P-08)', () => {
    expect(offenders(/\blocalStorage\b/).filter((path) => !path.startsWith(STORAGE_FOLDER))).toEqual([]);
    expect(offenders(/\b(sessionStorage|indexedDB|XMLHttpRequest|WebSocket|openDatabase)\b/)).toEqual([]);
    // Tampoco cookies propias: la única cookie es la de la sesión, HttpOnly, y la escribe el servidor.
    expect(offenders(/\bdocument\.cookie\b|\bcookieStore\b/)).toEqual([]);
  });

  it('NADA de la sesión se guarda en el navegador: el almacenamiento no conoce la sesión ni el contrato (regla P-02)', () => {
    // El almacenamiento (solo el carrito) no importa nada de la sesión, de la cuenta ni del contrato…
    expect(
      violations('3-infrastructure/storage', (specifier) => /(^|\/)(auth|account)(\/|$)/.test(specifier) || /contract|webApi|webMappers|Session|Rpc/.test(specifier)),
    ).toEqual([]);
    // …ni menciona sesión, token, contraseña o permisos.
    expect(offenders(/session|sesi[oó]n|token|password|contrase[nñ]a|permission|permiso/i).filter((path) => path.startsWith(STORAGE_FOLDER))).toEqual([]);
    // Y quien maneja la sesión (dominio, aplicación, adaptadores y proveedor) no importa el almacenamiento.
    const sessionFiles = Object.entries(SOURCES).filter(([path]) => /(\/auth\/|\/account\/|webApi|webMappers|Session|Rpc|mockWeb|contract)/.test(path));
    expect(sessionFiles.length).toBeGreaterThan(10);
    expect(
      sessionFiles.flatMap(([path, source]) =>
        importsOf(source)
          .filter((specifier) => /(^|\/)storage(\/|$)/.test(specifier))
          .map((specifier) => `${path} → ${specifier}`),
      ),
    ).toEqual([]);
  });

  it('la red (`fetch`) vive solo en 3-infrastructure/http: api.ts (tienda) y webApi.ts (sesión y RPC) (reglas S-07 y P-08)', () => {
    expect(offenders(/\bfetch\(/).sort()).toEqual(['./3-infrastructure/http/api.ts', './3-infrastructure/http/webApi.ts']);
    expect(offenders(/\b(sendBeacon|EventSource)\b/)).toEqual([]);
    // Los puertos existen en el dominio y los implementa la infraestructura.
    expect(Object.keys(SOURCES)).toEqual(
      expect.arrayContaining([
        './1-domain/ports/ICatalogSource.ts',
        './1-domain/ports/IReservationGateway.ts',
        './1-domain/ports/ISessionGateway.ts',
        './1-domain/ports/IRpcGateway.ts',
        './1-domain/ports/IAccountGateway.ts',
        './3-infrastructure/http/HttpCatalogSource.ts',
        './3-infrastructure/http/HttpReservationGateway.ts',
        './3-infrastructure/http/HttpSessionGateway.ts',
        './3-infrastructure/http/HttpRpcGateway.ts',
        './3-infrastructure/http/RpcAccountGateway.ts',
      ]),
    );
  });

  it('la sesión web viaja al mismo origen, con la cookie y la cabecera propia; nunca con un token a mano (regla P-02)', () => {
    const webApi = SOURCES['./3-infrastructure/http/webApi.ts'];
    expect(webApi).toContain("credentials: 'same-origin'");
    expect(webApi).toContain("mode: 'same-origin'");
    expect(webApi).toContain("CLIENT_VERSION_HEADER = 'X-MINV-Client-Version'");
    expect(webApi).toContain("WEB_API_PREFIX = '/api/v1/web'");
    expect(offenders(/\bAuthorization\b|\bBearer\b/)).toEqual([]);
  });

  it('el contrato generado se usa a través de UN solo adaptador (regla P-07)', () => {
    const importers = Object.entries(SOURCES)
      .filter(([, source]) => importsOf(source).some((specifier) => specifier.includes('contract.generated')))
      .map(([path]) => path);
    expect(importers).toEqual(['./3-infrastructure/http/contract.ts']);
    expect(SOURCES['./3-infrastructure/http/contract.generated.ts']).toBeDefined();
    // La presentación lo recibe por el contenedor (y su fachada `app/contract.ts`), no desde la infraestructura.
    expect(new Set(importsOf(SOURCES['./4-presentation/app/contract.ts']))).toEqual(new Set(['./container']));
  });

  it('el panel se descarga aparte: nadie fuera del panel lo importa de forma estática (regla P-09)', () => {
    expect(
      Object.entries(SOURCES)
        .filter(([path]) => !path.startsWith('./4-presentation/panel/'))
        .flatMap(([path, source]) =>
          importsOf(source)
            .filter((specifier) => specifier.startsWith('@/4-presentation/panel'))
            .map((specifier) => `${path} → ${specifier}`),
        ),
    ).toEqual([]);
    // Entra solo por la carga diferida de la tabla de rutas.
    expect(SOURCES['./4-presentation/app/routeTable.tsx']).toContain("import('@/4-presentation/panel/PanelRoot')");
  });

  it('un módulo del panel no importa de otro módulo (regla P-09)', () => {
    const MODULES = './4-presentation/panel/modules/';
    const crossImports = Object.entries(SOURCES)
      .filter(([path]) => path.startsWith(MODULES))
      .flatMap(([path, source]) => {
        const inside = path.slice(MODULES.length).split('/');
        const own = inside[0];
        // Cuántos «../» hacen falta para salir del módulo propio desde este archivo.
        const levelsToLeave = inside.length - 1;
        return importsOf(source)
          .filter((specifier) => {
            const absolute = /^@\/4-presentation\/panel\/modules\/([^/]+)/.exec(specifier);
            if (absolute) return absolute[1] !== own;
            if (!specifier.startsWith('.')) return false;
            const ups = specifier.split('/').filter((part) => part === '..').length;
            const target = specifier.split('/').filter((part) => part !== '..' && part !== '.')[0] ?? '';
            // Sale del módulo y entra en OTRA carpeta de `modules/` (subir un nivel más llega a shell/ o kit/).
            return ups === levelsToLeave && target !== own;
          })
          .map((specifier) => `${path} → ${specifier}`);
      });
    expect(crossImports).toEqual([]);
  });

  it('un módulo del panel importa SOLO el conjunto del panel, el contrato y su propia carpeta (regla P-09, W3b)', () => {
    const MODULES = './4-presentation/panel/modules/';
    const ALLOWED = new Set([
      '@/4-presentation/panel/kit',
      '@/4-presentation/panel/hooks',
      '@/4-presentation/panel/lib',
      '@/4-presentation/panel/registry',
      '@/4-presentation/app/contract',
      '@/4-presentation/app/routes',
    ]);
    const PACKAGES = /^(react|react-dom|react-router|react-router-dom|lucide-react|clsx)(\/|$)/;
    const files = Object.entries(SOURCES).filter(([path]) => path.startsWith(MODULES));
    expect(files.length).toBeGreaterThan(3);
    const outside = files.flatMap(([path, source]) => {
      const own = `${MODULES}${path.slice(MODULES.length).split('/')[0]}/`;
      return importsOf(source)
        .filter((specifier) => {
          if (specifier.startsWith('.')) {
            // Una importación relativa no puede salir de la carpeta del módulo (ni a shell/, kit/ u otro módulo).
            const resolved = new URL(specifier, `https://modulo.invalid/${path.slice(2)}`).pathname;
            return !`.${resolved}`.startsWith(own);
          }
          return !ALLOWED.has(specifier) && !PACKAGES.test(specifier);
        })
        .map((specifier) => `${path} → ${specifier}`);
    });
    expect(outside).toEqual([]);
  });

  it('un módulo del panel no declara a mano tipos del servidor: salen del contrato generado (regla P-07, W3b)', () => {
    const contract = SOURCES['./3-infrastructure/http/contract.generated.ts'];
    const serverTypes = new Set([...contract.matchAll(/export\s+(?:interface|type)\s+(\w+)/g)].map((match) => match[1]));
    expect(serverTypes.has('RpcOperations')).toBe(true);
    const declared = Object.entries(SOURCES)
      .filter(([path]) => path.startsWith('./4-presentation/panel/modules/'))
      .flatMap(([path, source]) =>
        [...code(source).matchAll(/(?:^|\n)\s*(?:export\s+)?(?:interface|type)\s+(\w+)/g)]
          .map((match) => match[1])
          .filter((name) => serverTypes.has(name) || /(Query|Command)$/.test(name))
          .map((name) => `${path} → ${name}`),
      );
    expect(declared).toEqual([]);
  });

  it('nada de HTML ni código inyectado: la página corre con una Content-Security-Policy de solo «self» (regla P-11)', () => {
    expect(offenders(/dangerouslySetInnerHTML|\binnerHTML\b|\bouterHTML\b|insertAdjacentHTML|document\.write/)).toEqual([]);
    expect(offenders(/\beval\(|new Function\(|set(Timeout|Interval)\(\s*['"`]/)).toEqual([]);
    // Ni scripts, estilos, fuentes o imágenes de otros dominios en el código de la V7 (sesión, cuenta, carrito y panel).
    const v7 = Object.entries(SOURCES).filter(([path]) => /(\/auth\/|\/account\/|\/panel\/|\/cart\/|webApi|webMappers|Session|UserMenu|sessionLinks|mockWeb)/.test(path));
    expect(v7.length).toBeGreaterThan(20);
    expect(v7.filter(([, source]) => /https?:\/\/(?!interno\.invalid)/.test(code(source))).map(([path]) => path)).toEqual([]);
  });
});

// Prueba de arquitectura: las dependencias solo apuntan hacia adentro (1-domain ← 2-application ← 4-presentation) y la
// infraestructura entra únicamente por el punto de composición `4-presentation/app/container.ts`.
// Lee el código fuente de src/ (sin las pruebas) y revisa cada `import`.

import { describe, expect, it } from 'vitest';

const SOURCES = import.meta.glob<string>(['./**/*.{ts,tsx}', '!./**/*.test.{ts,tsx}', '!./test-setup.ts'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

const REACT_LIKE = /^(react|react-dom|react-router|react-router-dom|lucide-react|clsx)(\/|$)/;

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

  it('3-infrastructure implementa el puerto del dominio sin tocar la aplicación ni la presentación', () => {
    expect(
      violations(
        '3-infrastructure',
        (specifier) =>
          REACT_LIKE.test(specifier) ||
          specifier.startsWith('@/2-application') ||
          specifier.startsWith('@/4-presentation'),
      ),
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

  it('nadie usa almacenamiento del navegador ni red (solo presentación, estado en memoria)', () => {
    const offenders = Object.entries(SOURCES)
      .filter(([, source]) => /\b(localStorage|sessionStorage|indexedDB|fetch\(|XMLHttpRequest|WebSocket)\b/.test(source))
      .map(([path]) => path);
    expect(offenders).toEqual([]);
  });
});

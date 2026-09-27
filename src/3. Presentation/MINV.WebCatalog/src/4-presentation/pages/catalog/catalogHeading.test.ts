import { describe, expect, it } from 'vitest';
import type { Category } from '@/1-domain/catalog/types';
import { EMPTY_FILTERS } from './catalogFilters';
import { catalogHeading } from './catalogHeading';

const componentes: Category = {
  code: 'COMP',
  name: 'Componentes',
  slug: 'componentes',
  parent: null,
  icon: 'Cpu',
  description: 'Componentes para armar o actualizar tu PC',
  productCount: 63,
};

const procesadores: Category = {
  code: 'CPU',
  name: 'Procesadores',
  slug: 'procesadores',
  parent: 'COMP',
  icon: 'Cpu',
  description: 'Componentes > Procesadores',
  productCount: 10,
};

describe('encabezado del catálogo', () => {
  it('sin filtros presenta todo el catálogo con el total', () => {
    const heading = catalogHeading(EMPTY_FILTERS, { total: 159 });
    expect(heading.title).toBe('Todo el catálogo');
    expect(heading.subtitle).toContain('159 productos');
  });

  it('con una raíz usa su descripción y con una hija, la de la raíz', () => {
    expect(catalogHeading({ ...EMPTY_FILTERS, category: 'componentes' }, { category: componentes, root: componentes, total: 63 })).toEqual({
      eyebrow: 'Catálogo',
      title: 'Componentes',
      subtitle: 'Componentes para armar o actualizar tu PC',
    });
    expect(catalogHeading({ ...EMPTY_FILTERS, category: 'procesadores' }, { category: procesadores, root: componentes, total: 10 })).toEqual({
      eyebrow: 'Componentes',
      title: 'Procesadores',
      subtitle: 'Componentes para armar o actualizar tu PC',
    });
  });

  it('con búsqueda sin categoría titula «Resultados para …» y cuenta las coincidencias', () => {
    const heading = catalogHeading({ ...EMPTY_FILTERS, q: 'rtx 5070' }, { total: 3 });
    expect(heading.title).toBe('Resultados para “rtx 5070”');
    expect(heading.subtitle).toBe('3 productos coinciden en todo el catálogo.');
    expect(catalogHeading({ ...EMPTY_FILTERS, q: 'zzz' }, { total: 0 }).subtitle).toBe('No encontramos coincidencias en el catálogo.');
  });

  it('con búsqueda dentro de una categoría mantiene el nombre de la categoría como título', () => {
    const heading = catalogHeading({ ...EMPTY_FILTERS, category: 'procesadores', q: 'ryzen' }, { category: procesadores, root: componentes, total: 1 });
    expect(heading.title).toBe('Procesadores');
    expect(heading.subtitle).toBe('1 resultado para “ryzen” en Procesadores.');
  });

  it('con etiquetas titula Ofertas / Novedades / Destacados', () => {
    expect(catalogHeading({ ...EMPTY_FILTERS, tags: ['oferta'] }, { total: 27 }).title).toBe('Ofertas');
    expect(catalogHeading({ ...EMPTY_FILTERS, tags: ['oferta', 'nuevo'] }, { total: 5 }).title).toBe('Ofertas y Novedades');
    expect(catalogHeading({ ...EMPTY_FILTERS, tags: ['destacado'] }, { total: 42 }).subtitle).toBe('Los productos que más eligen nuestros clientes.');
  });
});

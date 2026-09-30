// Inventario › Catálogo: el catálogo del escritorio (CatalogView + CatalogTech) en la web, en tres pestañas:
//   - «Productos»: lista o galería con imágenes, filtros (categoría con sus subcategorías, marca, estado, imagen, precio y
//     margen, serie o IMEI, plataforma, condición y las especificaciones filtrables de la categoría), detalle lateral con
//     la ficha técnica, alta y edición por pestañas (Datos, Precios, Imágenes, Ficha técnica, Stock mínimo), activar y
//     desactivar, exportar CSV y el resumen PLEGADO.
//   - «Categorías»: nueva categoría, subcategoría (hereda las especificaciones) y cambio de nombre.
//   - «Especificaciones»: las fichas técnicas por categoría (tipo, opciones, filtrable, obligatoria, clave del armador).
// Las consultas comunes (opciones, catálogo y especificaciones) se hacen aquí una vez y se pasan a las pestañas.
//
// Dirección: `?pestana=categorias|especificaciones`, `?nuevo=producto|categoria|especificacion` (tablero y botones de
// arriba), `?editar=SKU`, `?producto=SKU` (detalle) y los filtros de cada pestaña.

import { FilePlus2, FolderPlus, ListTree, PackagePlus, SlidersHorizontal, Tags } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Page, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { ALL_CATALOG, ALL_DEFINITIONS } from './catalog';
import { CategoriesTab } from './CategoriesTab';
import { NEW_PARAM, TAB_PARAM, tabOf, type CatalogTab, type NewRequest } from './params';
import { ProductsTab } from './ProductsTab';
import { SpecsTab } from './SpecsTab';

export function CatalogPage() {
  const { canRun } = usePermissions();
  const [params, setParams] = useSearchParams();
  const tab = tabOf(params);

  const options = useRpcQuery('GetCatalogOptionsQuery', {});
  const catalog = useRpcQuery('GetCatalogQuery', ALL_CATALOG);
  const definitions = useRpcQuery('GetSpecDefinitionsQuery', ALL_DEFINITIONS);

  const changeTab = (next: CatalogTab) =>
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        if (next === 'productos') updated.delete(TAB_PARAM);
        else updated.set(TAB_PARAM, next);
        return updated;
      },
      { replace: true },
    );

  const request = (kind: NewRequest) =>
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        updated.set(NEW_PARAM, kind);
        return updated;
      },
      { replace: true },
    );

  const actions =
    tab === 'productos' ? (
      canRun('SaveProductCommand') && (
        <Button leftIcon={<PackagePlus />} onClick={() => request('producto')}>
          Nuevo producto
        </Button>
      )
    ) : tab === 'categorias' ? (
      canRun('SaveCategoryCommand') && (
        <Button leftIcon={<FolderPlus />} onClick={() => request('categoria')}>
          Nueva categoría
        </Button>
      )
    ) : (
      canRun('SaveSpecDefinitionCommand') && (
        <Button leftIcon={<FilePlus2 />} onClick={() => request('especificacion')}>
          Nueva especificación
        </Button>
      )
    );

  return (
    <Page
      title="Catálogo"
      description="Productos con sus precios, imágenes y ficha técnica, las categorías y las especificaciones de cada categoría."
      actions={actions}
    >
      <Tabs
        label="Secciones del catálogo"
        value={tab}
        onChange={changeTab}
        tabs={[
          { id: 'productos', label: 'Productos', icon: <Tags />, count: catalog.data?.length },
          { id: 'categorias', label: 'Categorías', icon: <ListTree />, count: options.data?.categories.length },
          { id: 'especificaciones', label: 'Especificaciones', icon: <SlidersHorizontal />, count: definitions.data?.length },
        ]}
      >
        <TabPanel id="productos">
          <ProductsTab options={options} catalog={catalog} definitions={definitions} />
        </TabPanel>
        <TabPanel id="categorias">
          <CategoriesTab options={options} catalog={catalog} definitions={definitions} />
        </TabPanel>
        <TabPanel id="especificaciones">
          <SpecsTab options={options} definitions={definitions} />
        </TabPanel>
      </Tabs>
    </Page>
  );
}

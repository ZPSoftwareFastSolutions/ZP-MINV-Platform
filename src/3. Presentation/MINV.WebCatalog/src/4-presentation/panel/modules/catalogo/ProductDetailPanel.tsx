// Módulo «Catálogo» · detalle lateral de un producto: imagen (`GetProductImagesQuery` de ESA variante), datos del catálogo,
// precio con y sin IVA, margen (guía), stock mínimo y máximo, y la FICHA TÉCNICA (`GetProductTechQuery`: serie o IMEI,
// garantía, unidades con serie en stock y las especificaciones). Las dos consultas corren recién con el panel abierto.
// Botones: editar (o solo la ficha técnica), «Ver ficha y kardex» (módulo Stock, `?ficha=SKU`), activar y desactivar.

import { CircleCheck, CircleOff, ClipboardList, ImageOff, PackageSearch, Pencil } from 'lucide-react';
import { useId } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DetailList, ErrorState, LoadingState, SidePanel, StatusBadge } from '@/4-presentation/panel/kit';
import { formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import { PRODUCT_STATES, includedTax, marginText, netOf, serialText, warrantyText, type ProductEntry, type TaxRule } from './catalog';
import { imageDataUrl } from './image';

export interface ProductDetailPanelProps {
  /** SKU del detalle (el último abierto, para que el panel se deslice con su contenido al cerrarse). */
  sku: string | null;
  entry: ProductEntry | null;
  /** El catálogo ya llegó (si el SKU no está, se avisa). */
  loaded: boolean;
  open: boolean;
  onClose: () => void;
  tax: TaxRule;
  /** Cambia después de guardar: la ficha y la imagen se vuelven a leer. */
  version: number;
  canProduct: boolean;
  canTech: boolean;
  onEdit: (mode: 'editar' | 'ficha') => void;
  onActivate: () => void;
  onDeactivate: () => void;
  busy: boolean;
  stockLink: (sku: string) => string;
}

export function ProductDetailPanel({ sku, entry, loaded, open, onClose, tax, version, canProduct, canTech, onEdit, onActivate, onDeactivate, busy, stockLink }: ProductDetailPanelProps) {
  const row = entry?.row ?? null;
  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={row ? row.name : 'Producto'}
      description={row ? `${row.sku} · ${row.category}` : undefined}
      headerExtra={entry && <StatusBadge status={entry.state} statuses={PRODUCT_STATES} />}
      loading={open && !loaded}
      footer={
        row && (
          <div className="flex flex-col gap-2">
            {canProduct && (
              <Button leftIcon={<Pencil />} fullWidth onClick={() => onEdit('editar')}>
                Editar producto
              </Button>
            )}
            {!canProduct && canTech && (
              <Button leftIcon={<ClipboardList />} fullWidth onClick={() => onEdit('ficha')}>
                Editar ficha técnica
              </Button>
            )}
            <Button variant="outline" leftIcon={<PackageSearch />} fullWidth to={stockLink(row.sku)}>
              Ver ficha y kardex
            </Button>
            {canProduct && !row.isActive && (
              <Button variant="outline" leftIcon={<CircleCheck />} fullWidth loading={busy} onClick={onActivate}>
                Activar
              </Button>
            )}
            {canProduct && row.isActive && (
              <Button variant="danger" leftIcon={<CircleOff />} fullWidth onClick={onDeactivate}>
                Desactivar
              </Button>
            )}
          </div>
        )
      }
    >
      {entry ? (
        <DetailBody key={`${entry.key}-${version}`} entry={entry} tax={tax} />
      ) : (
        loaded && sku && <p className="text-sm text-text-muted">No hay un producto con el SKU {sku} en el catálogo.</p>
      )}
    </SidePanel>
  );
}

function DetailBody({ entry, tax }: { entry: ProductEntry; tax: TaxRule }) {
  const { row } = entry;
  const techTitleId = useId();
  const image = useRpcQuery('GetProductImagesQuery', { variantIds: [row.variantId] }, { enabled: row.hasImage });
  const tech = useRpcQuery('GetProductTechQuery', { sku: row.sku });
  const picture = image.data?.[0];
  const net = row.salePrice > 0 ? netOf(row.salePrice, tax) : null;

  return (
    <div className="space-y-5" data-testid="detalle-producto">
      <div className="flex aspect-[4/3] items-center justify-center overflow-hidden rounded-card border border-border bg-surface-2">
        {picture ? (
          <img src={imageDataUrl(picture.contentType, picture.content)} alt={`Imagen de ${row.name}`} className="size-full object-contain" />
        ) : row.hasImage && image.loading ? (
          <LoadingState label="Cargando la imagen…" rows={1} />
        ) : (
          <span className="flex flex-col items-center gap-1 text-sm text-text-muted">
            <ImageOff aria-hidden="true" className="size-8" />
            {row.hasImage && image.error ? 'No se pudo cargar la imagen' : 'Sin imagen'}
          </span>
        )}
      </div>

      <DetailList
        items={[
          { label: 'SKU', value: row.sku },
          { label: 'Categoría', value: row.category },
          { label: 'Marca', value: entry.brand },
          { label: 'Unidad', value: row.unit },
          { label: 'Proveedor preferido', value: row.supplier ?? 'Sin proveedor' },
          { label: 'Código de barras', value: row.barcode },
          { label: 'Precio de venta (IVA incl.)', value: row.salePrice > 0 ? formatMoney(row.salePrice) : 'Sin precio' },
          { label: 'Precio sin IVA', value: net === null ? null : `${formatMoney(net)} · IVA ${formatMoney(includedTax(row.salePrice, tax))}` },
          { label: 'Costo unitario', value: formatMoney(row.unitCost) },
          { label: 'Margen (guía)', value: net === null ? null : `${marginText(entry.margin)} · ganancia ${formatMoney(net - row.unitCost)} por unidad` },
          { label: 'Stock mínimo / máximo', value: `${formatQuantity(row.minimum)} / ${formatQuantity(row.maximum)}` },
          { label: 'Posición principal', value: row.binCode },
          { label: 'Disponible (sucursal activa)', value: entry.available === null ? null : formatQuantity(entry.available, { unit: row.unit }) },
          { label: 'Descripción', value: row.description, wide: true },
        ]}
      />

      <section aria-labelledby={techTitleId} className="space-y-2">
        <h3 id={techTitleId} className="text-sm font-semibold text-text">
          Ficha técnica
        </h3>
        {tech.loading ? (
          <LoadingState label="Leyendo la ficha técnica…" rows={2} />
        ) : tech.error ? (
          <ErrorState error={tech.error} operation="GetProductTechQuery" onRetry={tech.reload} retrying={tech.fetching} />
        ) : tech.data ? (
          <div className="space-y-3" data-testid="ficha-tecnica">
            <DetailList
              items={[
                { label: 'Control por unidad', value: tech.data.trackSerials ? (tech.data.serialKind === 'Imei' ? 'IMEI (una por unidad)' : 'Número de serie (una por unidad)') : 'Sin serie (por cantidad)' },
                { label: 'Garantía', value: warrantyText(tech.data.warrantyMonths) },
                ...(tech.data.trackSerials ? [{ label: 'Unidades con serie en stock', value: formatQuantity(tech.data.serialsInStock) }] : []),
                ...(tech.data.brand ? [{ label: 'Marca', value: tech.data.brand }] : []),
              ]}
            />
            {tech.data.specs.length > 0 ? (
              <DetailList columns={1} items={tech.data.specs.map((spec) => ({ label: spec.name, value: spec.display || spec.values.join(', ') }))} />
            ) : (
              <p className="text-sm text-text-muted">Este producto todavía no tiene especificaciones cargadas.</p>
            )}
          </div>
        ) : (
          <p className="text-sm text-text-muted">{serialText(entry)}</p>
        )}
      </section>
    </div>
  );
}

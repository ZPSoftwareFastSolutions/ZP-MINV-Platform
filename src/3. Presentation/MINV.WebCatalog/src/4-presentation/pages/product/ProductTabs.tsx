// Pestañas de la ficha: Descripción (texto, características y ficha rápida), Especificaciones (todas) y
// Compatibilidad (informativa: socket, tipo de RAM, formato, potencia… si existen). Los paneles quedan montados con
// `hidden` para que `aria-controls` de cada pestaña apunte siempre a un elemento real.

import clsx from 'clsx';
import { Check, Cpu, FileText, Info, ListChecks, Puzzle } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { slotForProduct } from '@/1-domain/builder/slots';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { SpecTable } from '@/4-presentation/components/product/SpecTable';
import { Button } from '@/4-presentation/components/ui/Button';
import { Tabs, type TabItem } from '@/4-presentation/components/ui/Tabs';
import { compatibilitySpecs, warrantyLabel } from './productInfo';

type TabId = 'descripcion' | 'especificaciones' | 'compatibilidad';

function Panel({ id, activeId, children }: { id: TabId; activeId: string; children: ReactNode }) {
  return (
    <div role="tabpanel" id={`panel-${id}`} aria-labelledby={`tab-${id}`} tabIndex={0} hidden={id !== activeId} className="pt-6 animate-fade-up">
      {children}
    </div>
  );
}

function QuickFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 border-b border-border py-2.5 text-sm">
      <dt className="text-text-muted">{label}</dt>
      <dd className="text-right font-medium text-text">{value}</dd>
    </div>
  );
}

export function ProductTabs({ product }: { product: Product }) {
  const [tab, setTab] = useState<string>('descripcion');
  const compatibility = compatibilitySpecs(product);
  const slot = slotForProduct(product);

  const tabs: TabItem[] = [
    { id: 'descripcion', label: 'Descripción', icon: <FileText /> },
    { id: 'especificaciones', label: 'Especificaciones', icon: <ListChecks />, count: product.specs.length },
  ];
  if (compatibility.length > 0) tabs.push({ id: 'compatibilidad', label: 'Compatibilidad', icon: <Puzzle />, count: compatibility.length });

  return (
    <section aria-label="Detalle del producto">
      <div className="relative">
        <Tabs tabs={tabs} value={tab} onChange={setTab} label="Secciones del producto" />
        {/* En pantallas angostas la fila de pestañas se desplaza: el degradado insinúa que hay más a la derecha. */}
        <div aria-hidden="true" className="pointer-events-none absolute inset-y-0 right-0 w-10 bg-linear-to-l from-bg to-transparent sm:hidden" />
      </div>

      <Panel id="descripcion" activeId={tab}>
        <div className="grid gap-8 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <div>
            <h3 className="font-display text-lg font-semibold text-text">Sobre este producto</h3>
            <p className="mt-3 max-w-prose text-base leading-relaxed text-text-muted">{product.description}</p>
            {product.highlights.length > 0 && (
              <ul className="mt-5 space-y-2">
                {product.highlights.map((highlight) => (
                  <li key={highlight} className="flex items-start gap-2.5 text-sm text-text">
                    <Check aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                    <span>{highlight}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <dl aria-label="Ficha rápida" className="self-start rounded-card border border-border bg-surface p-4 sm:p-5">
            <QuickFact label="Marca" value={product.brand} />
            <QuickFact label="Categoría" value={product.categoryName} />
            <QuickFact label="Condición" value={product.condition} />
            <QuickFact label="Garantía" value={warrantyLabel(product.warrantyMonths)} />
            <QuickFact label="Número de serie" value={product.serialized ? 'Sí, registrado en la entrega' : 'No aplica'} />
            <QuickFact label="SKU" value={product.sku} />
          </dl>
        </div>
      </Panel>

      <Panel id="especificaciones" activeId={tab}>
        <SpecTable specs={product.specs} columns={2} caption={`Especificaciones de ${product.shortName}`} />
      </Panel>

      {compatibility.length > 0 && (
        <Panel id="compatibilidad" activeId={tab}>
          <p role="note" className="flex items-start gap-2.5 rounded-xl border border-warning/40 bg-warning-soft px-4 py-3 text-sm text-warning-text">
            <Info aria-hidden="true" className="mt-0.5 size-5 shrink-0" />
            <span>
              <strong className="font-semibold">Referencia visual:</strong> este catálogo no valida compatibilidad. Compará estos datos con los de tus otras
              piezas o consultá a nuestro equipo antes de comprar.
            </span>
          </p>
          <dl className="mt-5 flex flex-wrap gap-2.5">
            {compatibility.map((spec) => (
              <div
                key={spec.key}
                className={clsx(
                  'flex min-h-11 max-w-full flex-col justify-center rounded-xl border border-border bg-surface-2 px-3.5 py-2',
                  'transition-colors duration-200 hover:border-border-strong',
                )}
              >
                <dt className="text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">{spec.label}</dt>
                <dd className="text-sm font-semibold text-text">{spec.text}</dd>
              </div>
            ))}
          </dl>
          {slot && (
            <div className="mt-6 flex flex-col gap-3 rounded-card border border-border bg-surface p-4 sm:flex-row sm:items-center sm:justify-between sm:p-5">
              <p className="text-sm text-text-muted">
                <span className="font-semibold text-text">{slot.label}: </span>
                {slot.hint}
              </p>
              <Button to={ROUTES.builder} variant="outline" leftIcon={<Cpu />} className="shrink-0">
                Ir a Armá tu PC
              </Button>
            </div>
          )}
        </Panel>
      )}
    </section>
  );
}

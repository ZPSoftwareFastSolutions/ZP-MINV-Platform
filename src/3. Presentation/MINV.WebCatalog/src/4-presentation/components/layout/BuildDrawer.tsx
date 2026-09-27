// Cajón «Mi armado»: resumen del armado en memoria con cantidades, quitar, vaciar (con confirmación) y total.
// Lo usan la cabecera y la página del armador (useBuilder().openDrawer()).

import { ArrowRight, PcCase, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { slotByKey } from '@/1-domain/builder/slots';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductCardCompact } from '@/4-presentation/components/product/ProductCardCompact';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { Drawer } from '@/4-presentation/components/ui/Drawer';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { formatMoney, pluralize } from '@/shared/format';

export function BuildDrawer() {
  const builder = useBuilder();
  const navigate = useNavigate();
  const [confirmClear, setConfirmClear] = useState(false);
  const { lines, count, total, savings, progress, missing, isDrawerOpen, closeDrawer } = builder;

  const close = () => {
    setConfirmClear(false);
    closeDrawer();
  };

  const footer =
    lines.length > 0 ? (
      <div className="space-y-4">
        <div>
          <div className="mb-1.5 flex items-center justify-between text-xs">
            <span className="font-semibold text-text-muted">Piezas obligatorias</span>
            <span className="tabular-nums text-text">
              {progress.covered} de {progress.required}
            </span>
          </div>
          <div
            role="progressbar"
            aria-label="Piezas obligatorias del armado"
            aria-valuemin={0}
            aria-valuemax={progress.required}
            aria-valuenow={progress.covered}
            className="h-2 overflow-hidden rounded-full bg-surface-3"
          >
            <div
              className="h-full origin-left rounded-full bg-linear-to-r from-primary to-accent transition-transform duration-300"
              style={{ transform: `scaleX(${progress.ratio})` }}
            />
          </div>
          {missing.length > 0 ? (
            <p className="mt-2 text-xs text-text-muted">
              Falta: <span className="text-text">{missing.map((slot) => slot.label).join(', ')}</span>
            </p>
          ) : (
            <p className="mt-2 text-xs font-medium text-success-text">Tu armado tiene todas las piezas obligatorias.</p>
          )}
        </div>

        <div className="flex items-end justify-between gap-3">
          <div>
            <p className="text-xs text-text-muted">Total ({pluralize(count, 'pieza', 'piezas')})</p>
            {savings > 0 && <p className="text-xs font-medium text-cta-hover">Ahorrás {formatMoney(savings)}</p>}
          </div>
          <p className="font-display text-2xl font-semibold text-text tabular-nums">{formatMoney(total)}</p>
        </div>

        {confirmClear ? (
          <div className="flex items-center justify-between gap-3 rounded-xl border border-danger/40 bg-danger-soft p-3 text-sm" role="alert">
            <span className="text-text">¿Vaciar el armado?</span>
            <div className="flex gap-2">
              <Button size="sm" variant="ghost" onClick={() => setConfirmClear(false)}>
                No
              </Button>
              <Button
                size="sm"
                variant="cta"
                onClick={() => {
                  builder.clear();
                  setConfirmClear(false);
                }}
              >
                Sí, vaciar
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex gap-2">
            <Button variant="ghost" leftIcon={<Trash2 />} onClick={() => setConfirmClear(true)}>
              Vaciar
            </Button>
            <Button
              variant="brand"
              fullWidth
              rightIcon={<ArrowRight />}
              onClick={() => {
                close();
                navigate(ROUTES.builder);
              }}
            >
              Ir al armador
            </Button>
          </div>
        )}
      </div>
    ) : undefined;

  return (
    <Drawer
      open={isDrawerOpen}
      onClose={close}
      title="Mi armado"
      description={count > 0 ? pluralize(count, 'pieza elegida', 'piezas elegidas') : 'Todavía no elegiste piezas'}
      headerExtra={count > 0 ? <Badge tone="nuevo">{count}</Badge> : undefined}
      footer={footer}
    >
      {lines.length === 0 ? (
        <EmptyState
          icon={<PcCase />}
          title="Tu armado está vacío"
          description="Elegí piezas desde el catálogo o empezá con uno de nuestros armados sugeridos."
        >
          <Button to={ROUTES.builder} variant="brand" leftIcon={<PcCase />} onClick={close}>
            Armá tu PC
          </Button>
          <Button to={ROUTES.presets} variant="outline" onClick={close}>
            Ver PC armadas
          </Button>
        </EmptyState>
      ) : (
        <ul className="space-y-2">
          {lines.map((line) => {
            const slot = slotByKey(line.slot);
            return (
              <li key={line.product.sku}>
                <ProductCardCompact
                  product={line.product}
                  subtitle={slot.label}
                  quantity={line.quantity}
                  onQuantityChange={slot.multiple ? (quantity) => builder.setQuantity(line.product.sku, quantity) : undefined}
                  onRemove={() => builder.remove(line.product.sku)}
                  showLineTotal
                />
              </li>
            );
          })}
        </ul>
      )}
    </Drawer>
  );
}

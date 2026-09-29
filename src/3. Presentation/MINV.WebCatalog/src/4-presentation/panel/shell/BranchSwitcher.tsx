// Sucursal activa de la barra superior. Con más de una opción (varias sucursales, o la vista de todas de la gerencia
// global) es una lista desplegable: al cambiarla se envía `SelectBranchCommand`, se vuelve a leer la sesión y, como la
// sucursal activa cambió, cada pantalla vuelve a consultar sus datos sola (`useRpcQuery` depende de ella). Con una sola
// sucursal se muestra como texto.
//
// El servidor decide (regla B-01): elegir una sucursal ajena o «todas» sin ser gerencia global se rechaza y el aviso lo
// explica.

import clsx from 'clsx';
import { MapPin } from 'lucide-react';
import { useState } from 'react';
import { useSession } from '@/4-presentation/hooks/useSession';
import { useRpcCommand } from '../hooks';
import { SelectField, useNotify } from '../kit';

/**
 * La página no conoce el identificador de su sesión (viaja en la cookie HttpOnly, regla P-02): por `/api/v1/web/rpc` el
 * servidor aplica el cambio a la sesión de la cookie e ignora este valor (diseño §3.7). Va un UUID válido para que el
 * pedido se pueda leer.
 */
export const COOKIE_SESSION_ID = '00000000-0000-0000-0000-000000000000';

/** Valor de «Todas las sucursales» en la lista (sin sucursal activa: vista consolidada de la gerencia). */
const ALL = '';

export function BranchSwitcher({ className }: { className?: string }) {
  const { session, refresh } = useSession();
  const notify = useNotify();
  const select = useRpcCommand('SelectBranchCommand', { errorTitle: 'No se pudo cambiar la sucursal' });
  const [pending, setPending] = useState<string | null>(null);
  if (!session) return null;

  const { branches, allBranches, activeBranchId } = session.access;
  const nameOf = (id: string) => {
    const branch = branches.find((item) => item.id === id);
    return branch ? `${branch.code} · ${branch.name}` : 'Todas las sucursales';
  };

  if (branches.length + (allBranches ? 1 : 0) <= 1) {
    const only = branches.find((item) => item.id === activeBranchId) ?? branches[0];
    if (!only) return null;
    return (
      <p className={clsx('flex min-h-11 min-w-0 items-center gap-2 text-sm text-text-muted', className)} data-testid="sucursal-unica">
        <MapPin aria-hidden="true" className="size-4 shrink-0 text-accent" />
        <span className="min-w-0 truncate">
          Sucursal: <span className="font-medium text-text">{only.code}</span>
          <span className="max-sm:hidden"> · {only.name}</span>
        </span>
      </p>
    );
  }

  const change = async (next: string) => {
    if (next === (activeBranchId ?? ALL) || select.sending) return;
    setPending(next);
    const outcome = await select.run({ sessionId: COOKIE_SESSION_ID, branchId: next === ALL ? null : next });
    if (outcome.ok) {
      // La sesión trae la sucursal nueva: las consultas de la pantalla se repiten con ella.
      await refresh();
      notify.success('Sucursal activa cambiada', nameOf(next));
    }
    setPending(null);
  };

  return (
    <div className={clsx('flex min-w-0 items-center gap-2', className)}>
      <MapPin aria-hidden="true" className="size-4 shrink-0 text-accent" />
      <SelectField
        label="Sucursal activa"
        hideLabel
        value={pending ?? activeBranchId ?? ALL}
        onChange={(value) => void change(value)}
        options={branches.map((branch) => ({ value: branch.id, label: `${branch.code} · ${branch.name}` }))}
        allLabel={allBranches ? 'Todas las sucursales' : false}
        placeholder="Elija una sucursal"
        disabled={select.sending}
        className="min-w-0 flex-1 md:w-56 md:flex-none xl:w-72"
      />
    </div>
  );
}

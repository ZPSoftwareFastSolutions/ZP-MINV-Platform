// Módulo «Garantías» · «Imprimir la orden de servicio» de un caso: la vista previa de la orden (`GetWarrantyClaimQuery`) y
// «Imprimir», que imprime SOLO la orden con la impresora del navegador (área imprimible, `PrintArea`).
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { Printer } from 'lucide-react';
import { useState } from 'react';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Dialog, ErrorState, LoadingState } from '@/4-presentation/panel/kit';
import { branchLabel } from './claims';
import { PrintArea } from './PrintArea';
import { ServiceOrderSheet } from './ServiceOrderSheet';

export interface ServiceOrderDialogProps {
  /** Número del caso (null = cerrado). */
  number: string | null;
  onClose: () => void;
}

export function ServiceOrderDialog({ number, onClose }: ServiceOrderDialogProps) {
  const { session } = usePermissions();
  // El caso sigue a la vista mientras el diálogo se cierra.
  const [shown] = useState(number);
  const [printedAt] = useState(() => new Date());
  const current = number ?? shown;
  const open = number !== null;
  const detail = useRpcQuery('GetWarrantyClaimQuery', { number: current ?? '' }, { enabled: open && Boolean(current) });

  if (!current) return null;
  const branches = session?.access.branches ?? [];
  const data = detail.data;
  const sheet = (paper: boolean) =>
    data && <ServiceOrderSheet detail={data} company={session?.company ?? ''} branch={branchLabel(data.claim.branchCode, branches)} printedAt={printedAt} paper={paper} />;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title={`Orden de servicio ${current}`}
      description="Entréguela al cliente al recibir el equipo y pídala firmada al retirarlo."
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            Cerrar
          </Button>
          <Button leftIcon={<Printer />} disabled={!data} onClick={() => window.print()}>
            Imprimir
          </Button>
        </>
      }
    >
      {detail.error ? (
        <ErrorState error={detail.error} operation="GetWarrantyClaimQuery" onRetry={detail.reload} retrying={detail.fetching} />
      ) : data ? (
        <>
          {sheet(false)}
          {open && <PrintArea>{sheet(true)}</PrintArea>}
        </>
      ) : (
        <LoadingState label="Cargando la orden de servicio…" rows={4} />
      )}
    </Dialog>
  );
}

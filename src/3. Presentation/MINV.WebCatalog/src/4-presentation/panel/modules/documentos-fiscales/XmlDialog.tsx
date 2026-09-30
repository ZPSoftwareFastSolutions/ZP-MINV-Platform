// Módulo «Documentos fiscales» · el XML exacto que se envió al SIN (modalidad computarizada: sin firma digital), con
// sangría para leerlo y «Copiar XML» (para soporte o para el contador). El texto se muestra como texto, nunca como HTML.

import { Copy } from 'lucide-react';
import { useState } from 'react';
import { Button, Dialog, useNotify } from '@/4-presentation/panel/kit';
import { copyText } from './files';
import { prettyXml } from './fiscal';

export interface XmlDialogProps {
  /** null = cerrado. */
  target: { title: string; xml: string } | null;
  onClose: () => void;
}

export function XmlDialog({ target, onClose }: XmlDialogProps) {
  const notify = useNotify();
  const [shown, setShown] = useState(target);
  if (target && target !== shown) setShown(target);
  const current = target ?? shown;

  const copy = async () => {
    if (!current) return;
    if (await copyText(current.xml)) notify.success('XML copiado al portapapeles');
    else notify.warning('No se pudo copiar', 'El navegador no permite copiar aquí: seleccione el texto y cópielo a mano.');
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      size="xl"
      title={current ? `XML · ${current.title}` : 'XML'}
      description="El documento tal como se revisó y se envió al SIN (modalidad computarizada: sin firma digital)."
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            Cerrar
          </Button>
          <Button leftIcon={<Copy />} disabled={!current?.xml} onClick={() => void copy()}>
            Copiar XML
          </Button>
        </>
      }
    >
      <pre className="max-h-[60vh] overflow-auto rounded-xl border border-border bg-surface-2 p-3 font-mono text-xs leading-relaxed whitespace-pre text-text" tabIndex={0} aria-label="XML del documento" data-testid="xml-documento">
        {current ? prettyXml(current.xml) : ''}
      </pre>
    </Dialog>
  );
}

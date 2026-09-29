// Módulo «Integraciones» · muestra UNA vez un secreto (el token de una API Key o el secreto de firma de un webhook) con
// «Copiar». El servidor no lo vuelve a entregar nunca (en M-INV queda su huella o cifrado, regla B-11): vive solo en el
// estado de la pantalla mientras el diálogo está abierto y se borra al cerrarlo; nada se guarda en el navegador.

import { Check, Copy } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Alert, Button, Dialog, TextField, useNotify } from '@/4-presentation/panel/kit';
import { copyText } from './clipboard';

export interface SecretRevealProps {
  label: string;
  value: string;
  hint?: ReactNode;
}

/** El secreto en un campo de solo lectura con «Copiar» (si el navegador no deja copiar, queda seleccionado). */
export function SecretReveal({ label, value, hint }: SecretRevealProps) {
  const notify = useNotify();
  const input = useRef<HTMLInputElement>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const timer = setTimeout(() => setCopied(false), 2500);
    return () => clearTimeout(timer);
  }, [copied]);

  const copy = async () => {
    if (await copyText(value)) {
      setCopied(true);
      notify.success('Copiado al portapapeles');
      return;
    }
    input.current?.focus();
    input.current?.select();
    notify.warning('No se pudo copiar solo', 'Quedó seleccionado: cópielo con Ctrl+C (en el teléfono, mantenga pulsado y elija «Copiar»).');
  };

  return (
    <div className="space-y-3">
      <TextField
        label={label}
        value={value}
        onChange={() => undefined}
        readOnly
        ref={input}
        hint={hint}
        inputClassName="font-mono text-sm"
        autoComplete="off"
        autoCapitalize="none"
        spellCheck={false}
        onFocus={(event) => event.currentTarget.select()}
        data-testid="secreto"
      />
      <Button variant="outline" leftIcon={copied ? <Check /> : <Copy />} onClick={() => void copy()}>
        {copied ? 'Copiado' : 'Copiar'}
      </Button>
      <Alert tone="warning" title="Se muestra una sola vez">
        Cópielo ahora y guárdelo en la configuración de su sistema: M-INV no lo vuelve a mostrar.
      </Alert>
    </div>
  );
}

/** Lo que se muestra: qué pasó, el secreto y notas de uso. */
export interface SecretNotice {
  title: string;
  description?: string;
  message: ReactNode;
  label: string;
  value: string;
  /** Datos de uso debajo del secreto (permisos efectivos, cómo verificar la firma). */
  notes?: readonly string[];
}

export interface SecretDialogProps {
  /** null = cerrado. */
  secret: SecretNotice | null;
  onClose: () => void;
}

export function SecretDialog({ secret, onClose }: SecretDialogProps) {
  // Lo último mostrado sigue a la vista mientras el diálogo se cierra (animación de salida) y después se borra.
  const [shown, setShown] = useState<SecretNotice | null>(secret);
  if (secret && secret !== shown) setShown(secret);
  useEffect(() => {
    if (secret !== null || shown === null) return;
    const timer = setTimeout(() => setShown(null), 400);
    return () => clearTimeout(timer);
  }, [secret, shown]);

  return (
    <Dialog open={secret !== null} onClose={onClose} size="lg" title={shown?.title ?? ''} description={shown?.description} footer={<Button onClick={onClose}>Listo</Button>}>
      {shown && (
        <div className="space-y-4">
          <Alert tone="success">{shown.message}</Alert>
          <SecretReveal label={shown.label} value={shown.value} />
          {shown.notes && shown.notes.length > 0 && (
            <ul className="list-disc space-y-1 pl-5 text-sm text-text-muted">
              {shown.notes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Dialog>
  );
}

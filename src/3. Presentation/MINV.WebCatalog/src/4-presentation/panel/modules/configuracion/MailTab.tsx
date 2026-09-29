// Módulo «Configuración» · pestaña «Correo de la empresa»: el servidor SMTP con el que M-INV envía las facturas al
// comprador (XML y PDF) y las confirmaciones de las reservas (`GetSiatSettingsQuery` › correo). La contraseña nunca se
// muestra: solo si hay una guardada. Cambiarlo es de quien configura la facturación (`SaveMailSettingsCommand`), con
// ayuda para Gmail.

import { Mail, MailCheck, Pencil } from 'lucide-react';
import { usePermissions, type RpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Collapsible, DetailList, EmptyState, ErrorState, LoadingState, Section } from '@/4-presentation/panel/kit';
import { useState } from 'react';
import { MailSettingsDialog, type MailDialogMode } from './MailSettingsDialog';
import type { SiatSettingsData } from './settings';

export interface MailTabProps {
  siat: RpcQuery<SiatSettingsData>;
  companyName: string;
}

export function MailTab({ siat, companyName }: MailTabProps) {
  const { canRun } = usePermissions();
  const canSave = canRun('SaveMailSettingsCommand');
  const [mode, setMode] = useState<MailDialogMode | null>(null);

  if (siat.error && !siat.data) return <ErrorState error={siat.error} operation="GetSiatSettingsQuery" onRetry={siat.reload} retrying={siat.fetching} />;
  if (!siat.data) return <LoadingState label="Cargando el correo de la empresa…" rows={3} />;
  const view = siat.data;
  const mail = view.mail;
  const locked = !view.moduleActive;

  return (
    <div className="space-y-5">
      <Section
        title="Servidor de correo de la empresa"
        description="Con él M-INV envía las facturas al comprador (XML y PDF) y las confirmaciones de las reservas de la tienda web."
        actions={
          canSave && (
            <>
              <Button variant="outline" leftIcon={<Mail />} disabled={locked} onClick={() => setMode('gmail')}>
                Configurar con Gmail
              </Button>
              <Button leftIcon={<Pencil />} disabled={locked} onClick={() => setMode('normal')}>
                {mail ? 'Cambiar el correo' : 'Configurar el correo'}
              </Button>
            </>
          )
        }
      >
        <div className="space-y-4">
          {locked && <Alert tone="warning">La empresa no tiene habilitado el módulo «Facturación SIAT»: el correo de la empresa se puede revisar, pero no guardar.</Alert>}
          {mail ? (
            <DetailList
              items={[
                { label: 'Servidor', value: `${mail.host}, puerto ${mail.port}` },
                { label: 'Conexión segura', value: mail.useSsl ? 'Sí (STARTTLS o SSL)' : 'No (sin cifrar)' },
                { label: 'Usuario', value: mail.userName },
                { label: 'Contraseña', value: mail.hasPassword ? 'Guardada (cifrada; nunca se muestra)' : 'Sin contraseña' },
                { label: 'Remitente', value: `${mail.fromName} <${mail.fromAddress}>` },
                { label: 'Envío', value: mail.isEnabled ? 'Activo: se envían los correos' : 'Desactivado' },
              ]}
            />
          ) : (
            <EmptyState
              size="sm"
              icon={<MailCheck />}
              title="Todavía no configuró el correo de la empresa"
              description="Mientras tanto, las reservas usan el servidor de correo de M-INV (si lo configuró el instalador) y las facturas no se envían por correo."
            />
          )}
        </div>
      </Section>

      <Collapsible label="Ver cómo usar una cuenta de Gmail" openLabel="Ocultar la ayuda de Gmail" icon={<Mail />} description="Servidor, puerto y la contraseña de aplicación.">
        <ol className="list-decimal space-y-2 pl-5 text-sm text-text">
          <li>En la cuenta de Google de la empresa, active la verificación en dos pasos.</li>
          <li>Cree una «contraseña de aplicación» (Cuenta de Google › Seguridad › Contraseñas de aplicaciones) y cópiela: son 16 letras. Solo el dueño de la cuenta puede crearla.</li>
          <li>Aquí pulse «Configurar con Gmail»: servidor smtp.gmail.com, puerto 587 y conexión segura (STARTTLS) quedan puestos.</li>
          <li>En «Usuario» y en «Remitente (correo)» escriba el correo de Gmail; en la contraseña, la de aplicación (sin espacios).</li>
          <li>Guarde y pruebe: en Integraciones › Correos de reservas, reenvíe la confirmación de una reserva vigente.</li>
        </ol>
      </Collapsible>

      <MailSettingsDialog mode={mode} mail={mail} companyName={view.businessName ?? companyName} onClose={() => setMode(null)} onSaved={siat.reload} />
    </div>
  );
}

// Módulo «Caja» · datos de la factura del comprador (nominatividad), como el escritorio (`BuyerForm`): tipo de documento
// en lista desplegable (catálogo del SIN), número, complemento (solo CI), nombre o razón social y correo; NIT especiales
// del SIN; «¿ya compró antes?» al salir del número (`FindFiscalBuyerQuery`: autocompleta y elige al cliente) y
// «Verificar NIT» en el Padrón (`VerifyNitCommand`). La regla que manda es la del servidor.

import { BadgeCheck, IdCard } from 'lucide-react';
import { useEffect, useId, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, FormGrid, SelectField, TextField } from '@/4-presentation/panel/kit';
import {
  DOC_CI,
  DOC_NIT,
  SPECIAL_NITS,
  applyLookup,
  applySpecialNit,
  lookupMessages,
  lookupPayload,
  nitCheckMessage,
  nitToVerify,
  withDocumentType,
  type BuyerDraft,
  type BuyerErrors,
  type DocumentTypeOption,
  type FiscalMessage,
} from './fiscal';

export interface BuyerFieldsProps {
  buyer: BuyerDraft;
  onChange: Dispatch<SetStateAction<BuyerDraft>>;
  documentTypes: readonly DocumentTypeOption[];
  errors: BuyerErrors;
  /** Cliente elegido en el cobro (para guardar la verificación del NIT en su ficha). */
  customerCode: string;
  /** El comprador ya es cliente: el cobro lo elige. */
  onCustomerFound: (code: string) => void;
  canLookup: boolean;
  canVerifyNit: boolean;
}

const CONSUMER = 'CF';

export function BuyerFields({ buyer, onChange, documentTypes, errors, customerCode, onCustomerFound, canLookup, canVerifyNit }: BuyerFieldsProps) {
  const groupId = useId();
  const [messages, setMessages] = useState<FiscalMessage[]>([]);
  const lookup = useRpcCommand('FindFiscalBuyerQuery', { notifyError: false });
  const verify = useRpcCommand('VerifyNitCommand', { notifyError: false });
  // El número que se está escribiendo: una respuesta de otro número (el cajero siguió escribiendo) se descarta.
  const latestNumber = useRef(buyer.documentNumber);
  useEffect(() => {
    latestNumber.current = buyer.documentNumber;
  });

  const isCi = buyer.documentType === DOC_CI;
  const isNit = buyer.documentType === DOC_NIT;
  const nit = nitToVerify(buyer);

  const change = (update: (current: BuyerDraft) => BuyerDraft) => {
    setMessages([]);
    onChange(update);
  };

  const lookUp = async () => {
    const payload = lookupPayload(buyer);
    if (!canLookup || !payload) return;
    const outcome = await lookup.run(payload);
    if (!outcome.ok || latestNumber.current.trim() !== payload.documentNumber) return;
    const found = outcome.result;
    onChange((current) => applyLookup(current, found));
    setMessages(lookupMessages(found));
    if (found.found && found.customerCode) onCustomerFound(found.customerCode);
  };

  const verifyNit = async () => {
    if (nit === null) return;
    const outcome = await verify.run({ nit, customerCode: customerCode && customerCode !== CONSUMER ? customerCode : null });
    setMessages([outcome.ok ? nitCheckMessage(outcome.result) : { tone: 'warning', text: `No se pudo verificar ahora: ${outcome.message}` }]);
  };

  return (
    <fieldset className="min-w-0 space-y-3" aria-labelledby={`${groupId}-titulo`}>
      <legend id={`${groupId}-titulo`} className="flex items-center gap-2 text-base font-semibold text-text">
        <IdCard aria-hidden="true" className="size-5 text-accent" />
        Datos de la factura
      </legend>
      <FormGrid>
        <SelectField
          label="Tipo de documento"
          value={String(buyer.documentType)}
          onChange={(value) => change((current) => withDocumentType(current, Number(value) || DOC_CI))}
          options={documentTypes.map((option) => ({ value: option.value, label: option.label }))}
          allLabel={false}
        />
        <TextField
          label="Número de documento"
          value={buyer.documentNumber}
          onChange={(value) => change((current) => ({ ...current, documentNumber: value.trim() }))}
          onBlur={() => void lookUp()}
          error={errors.documentNumber}
          inputMode={isCi || isNit ? 'numeric' : 'text'}
          autoComplete="off"
          maxLength={20}
          hint={canLookup ? 'Al salir del campo se busca si ya compró antes.' : undefined}
        />
        {isCi && (
          <TextField
            label="Complemento"
            value={buyer.complement}
            onChange={(value) => change((current) => ({ ...current, complement: value.toUpperCase() }))}
            error={errors.complement}
            maxLength={5}
            autoComplete="off"
            optional
          />
        )}
        <TextField
          label="Nombre o razón social"
          value={buyer.name}
          onChange={(value) => onChange((current) => ({ ...current, name: value }))}
          autoComplete="off"
          className={isCi ? undefined : 'sm:col-span-2'}
          optional
        />
        <TextField
          label="Correo para enviar la factura"
          type="email"
          value={buyer.email}
          onChange={(value) => onChange((current) => ({ ...current, email: value }))}
          error={errors.email}
          autoComplete="off"
          className="sm:col-span-2"
          optional
        />
      </FormGrid>

      {isNit && (
        <div className="flex flex-wrap items-center gap-2">
          {canVerifyNit && (
            <Button variant="outline" leftIcon={<BadgeCheck />} loading={verify.sending} disabled={nit === null} onClick={() => void verifyNit()}>
              Verificar NIT
            </Button>
          )}
          <Checkbox
            label="Facturar aunque el NIT no sea válido"
            description="El SIN acepta la factura con el código de excepción."
            checked={buyer.exceptionRequested}
            onChange={(checked) => onChange((current) => ({ ...current, exceptionRequested: checked }))}
          />
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2" role="group" aria-label="NIT especiales del SIN">
        <span className="text-sm text-text-muted">NIT especial:</span>
        {SPECIAL_NITS.map((special) => (
          <Button
            key={special.code}
            variant="subtle"
            onClick={() => {
              onChange((current) => applySpecialNit(current, special));
              setMessages([{ tone: 'info', text: special.explanation }]);
            }}
          >
            {special.label}
          </Button>
        ))}
      </div>

      {lookup.sending && (
        <p role="status" className="text-sm text-text-muted">
          Buscando al comprador…
        </p>
      )}
      {messages.map((message) => (
        <Alert key={message.text} tone={message.tone}>
          {message.text}
        </Alert>
      ))}
    </fieldset>
  );
}

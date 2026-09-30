// Módulo «Estado del SIAT» · datos del comprador (nominatividad) al transcribir una factura manual, como `BuyerForm` del escritorio: tipo de
// documento del catálogo del SIN, número, complemento (solo con CI), nombre o razón social, correo, «facturar aunque el
// NIT no sea válido» y los NIT especiales del SIN con un clic. La validación que manda es la del servidor.

import { FormGrid, Button, Checkbox, SelectField, TextField } from '@/4-presentation/panel/kit';
import { DOC_CI, DOC_NIT, SPECIAL_NITS, applySpecialNit, withDocumentType, type BuyerDraft, type BuyerErrors } from './siat';

export interface BuyerFieldsProps {
  buyer: BuyerDraft;
  onChange: (buyer: BuyerDraft) => void;
  typeOptions: readonly { value: string; label: string }[];
  /** Errores a la vista (después de intentar enviar). */
  errors: BuyerErrors;
  disabled?: boolean;
}

export function BuyerFields({ buyer, onChange, typeOptions, errors, disabled = false }: BuyerFieldsProps) {
  const set = (patch: Partial<BuyerDraft>) => onChange({ ...buyer, ...patch });
  return (
    <div className="space-y-3">
      <FormGrid>
        <SelectField
          label="Tipo de documento"
          allLabel={false}
          value={String(buyer.documentType)}
          onChange={(value) => onChange(withDocumentType(buyer, Number(value)))}
          options={typeOptions}
          disabled={disabled}
          required
        />
        <TextField
          label="Número de documento"
          value={buyer.documentNumber}
          onChange={(value) => set({ documentNumber: value })}
          error={errors.documentNumber}
          inputMode={buyer.documentType === DOC_CI || buyer.documentType === DOC_NIT ? 'numeric' : 'text'}
          autoComplete="off"
          maxLength={20}
          disabled={disabled}
          required
        />
        {buyer.documentType === DOC_CI && (
          <TextField
            label="Complemento"
            value={buyer.complement}
            onChange={(value) => set({ complement: value.toUpperCase() })}
            error={errors.complement}
            hint="Solo si la cédula lo tiene (por ejemplo 1A)."
            maxLength={5}
            autoComplete="off"
            disabled={disabled}
            optional
          />
        )}
        <TextField label="Nombre o razón social" value={buyer.name} onChange={(value) => set({ name: value })} maxLength={200} disabled={disabled} optional />
        <TextField
          label="Correo del comprador"
          type="email"
          value={buyer.email}
          onChange={(value) => set({ email: value })}
          error={errors.email}
          autoComplete="off"
          hint="Recibe el XML y el PDF de la factura transcrita."
          disabled={disabled}
          optional
        />
      </FormGrid>
      {buyer.documentType === DOC_NIT && (
        <Checkbox
          label="Facturar aunque el NIT no sea válido"
          description="Código de excepción 1: el SIN acepta la factura aunque el NIT no figure activo en el Padrón."
          checked={buyer.exceptionRequested}
          onChange={(checked) => set({ exceptionRequested: checked })}
          disabled={disabled}
        />
      )}
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label="NIT especiales del SIN">
        <span className="text-sm text-text-muted">NIT especiales:</span>
        {SPECIAL_NITS.map((special) => (
          <Button key={special.code} variant="subtle" disabled={disabled} onClick={() => onChange(applySpecialNit(buyer, special.code))}>
            {special.label}
          </Button>
        ))}
      </div>
    </div>
  );
}

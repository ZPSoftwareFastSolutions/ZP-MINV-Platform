// Documento de identidad del comprador para la factura (catálogo del SIN): las mismas reglas que aplica el servidor
// (`FiscalRules.EnsureBuyerDocument`): CI y NIT solo dígitos, complemento solo con CI (hasta 5 caracteres). Lo usan
// «Mis datos» y la página de reserva.

export type DocumentTypeCode = 1 | 2 | 3 | 4 | 5;

export interface DocumentTypeInfo {
  code: DocumentTypeCode;
  /** Sigla del SIN. */
  short: string;
  label: string;
  /** Solo admite dígitos (CI y NIT). */
  numeric: boolean;
  /** Admite complemento (solo la cédula de identidad). */
  complement: boolean;
}

export const DOCUMENT_TYPES: readonly DocumentTypeInfo[] = [
  { code: 1, short: 'CI', label: 'Cédula de identidad (CI)', numeric: true, complement: true },
  { code: 2, short: 'CEX', label: 'Cédula de extranjero (CEX)', numeric: false, complement: false },
  { code: 3, short: 'PAS', label: 'Pasaporte (PAS)', numeric: false, complement: false },
  { code: 4, short: 'OD', label: 'Otro documento (OD)', numeric: false, complement: false },
  { code: 5, short: 'NIT', label: 'NIT', numeric: true, complement: false },
];

export const DOCUMENT_LIMITS = { numberMaxLength: 20, complementMaxLength: 5 } as const;

export function documentType(code: number | null | undefined): DocumentTypeInfo | undefined {
  return DOCUMENT_TYPES.find((type) => type.code === code);
}

export function isDocumentTypeCode(value: unknown): value is DocumentTypeCode {
  return typeof value === 'number' && DOCUMENT_TYPES.some((type) => type.code === value);
}

export interface BuyerDocumentInput {
  /** Código del tipo como texto del `<select>` («» = sin documento). */
  documentType: string;
  documentNumber: string;
  complement: string;
}

export type BuyerDocumentErrors = Partial<Record<keyof BuyerDocumentInput, string>>;

/** Código del tipo elegido en el formulario, o null si no eligió ninguno. */
export function parseDocumentType(value: string): DocumentTypeCode | null {
  const code = Number.parseInt(value, 10);
  return isDocumentTypeCode(code) ? code : null;
}

/**
 * Errores por campo del documento (vacío si está bien). El documento es OPCIONAL: sin tipo y sin número no hay nada
 * que validar; con uno de los dos, se exige el otro.
 */
export function validateBuyerDocument(input: BuyerDocumentInput): BuyerDocumentErrors {
  const errors: BuyerDocumentErrors = {};
  const type = documentType(parseDocumentType(input.documentType));
  const number = input.documentNumber.trim();
  const complement = input.complement.trim();

  if (!type) {
    if (number.length > 0) errors.documentType = 'Elegí el tipo de documento.';
    if (complement.length > 0) errors.complement = 'El complemento solo se usa con cédula de identidad.';
    return errors;
  }
  if (number.length === 0) errors.documentNumber = 'Falta el número de documento.';
  else if (number.length > DOCUMENT_LIMITS.numberMaxLength) errors.documentNumber = `El número de documento tiene como máximo ${DOCUMENT_LIMITS.numberMaxLength} caracteres.`;
  else if (type.numeric && !/^[0-9]+$/.test(number)) errors.documentNumber = `Con ${type.short} el número de documento solo admite dígitos.`;
  else if (/\p{Cc}/u.test(number)) errors.documentNumber = 'El número de documento tiene caracteres no permitidos.';

  if (complement.length > 0) {
    if (!type.complement) errors.complement = 'El complemento solo se usa con cédula de identidad.';
    else if (complement.length > DOCUMENT_LIMITS.complementMaxLength) errors.complement = `El complemento tiene como máximo ${DOCUMENT_LIMITS.complementMaxLength} caracteres.`;
    else if (!/^[0-9A-Za-z]+$/.test(complement)) errors.complement = 'El complemento solo admite letras y números.';
  }
  return errors;
}

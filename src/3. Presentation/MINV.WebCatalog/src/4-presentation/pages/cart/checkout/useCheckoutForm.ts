// Estado del formulario de reserva: valores, qué campos ya se visitaron y los errores que devolvió el servidor. Un campo
// se valida al salir de él (y desde entonces, mientras se corrige) y todos al enviar. Un error del servidor queda en su
// campo hasta que la persona lo cambia. Todo en memoria: si la reserva falla, lo escrito NO se pierde.

import { useCallback, useMemo, useState } from 'react';
import {
  checkoutFieldError,
  emptyCheckoutForm,
  validateCheckoutForm,
  type CheckoutField,
  type CheckoutFormErrors,
  type CheckoutFormInput,
  type CheckoutRules,
} from '@/1-domain/storefront/checkoutForm';

export interface CheckoutFormState {
  values: CheckoutFormInput;
  /** Error visible de un campo (del servidor, o de la validación si ya se visitó o se intentó enviar). */
  errorOf(field: CheckoutField): string | undefined;
  change(field: CheckoutField, value: string): void;
  /** Al salir del campo: desde ahora se muestra su error. */
  blur(field: CheckoutField): void;
  /** Al enviar: valida todo y marca todos los campos. Devuelve los errores (vacío = se puede enviar). */
  validateAll(): CheckoutFormErrors;
  /** Errores que devolvió el servidor, ubicados en su campo. */
  setServerErrors(errors: CheckoutFormErrors): void;
}

export function useCheckoutForm(rules: CheckoutRules): CheckoutFormState {
  const [values, setValues] = useState<CheckoutFormInput>(() => emptyCheckoutForm(rules.policy));
  const [touched, setTouched] = useState<ReadonlySet<CheckoutField>>(() => new Set());
  const [submitted, setSubmitted] = useState(false);
  const [serverErrors, setServerErrorsState] = useState<CheckoutFormErrors>({});

  const change = useCallback((field: CheckoutField, value: string) => {
    setValues((current) => ({ ...current, [field]: value }));
    // Lo que dijo el servidor de ese campo deja de valer en cuanto la persona lo cambia.
    setServerErrorsState((current) => {
      if (!current[field]) return current;
      const next = { ...current };
      delete next[field];
      return next;
    });
  }, []);

  const blur = useCallback((field: CheckoutField) => {
    setTouched((current) => (current.has(field) ? current : new Set(current).add(field)));
  }, []);

  const validateAll = useCallback((): CheckoutFormErrors => {
    setSubmitted(true);
    return validateCheckoutForm(values, rules);
  }, [values, rules]);

  const errorOf = useCallback(
    (field: CheckoutField): string | undefined => {
      if (serverErrors[field]) return serverErrors[field];
      if (!submitted && !touched.has(field)) return undefined;
      return checkoutFieldError(field, values, rules);
    },
    [serverErrors, submitted, touched, values, rules],
  );

  const setServerErrors = useCallback((errors: CheckoutFormErrors) => setServerErrorsState(errors), []);

  return useMemo(() => ({ values, errorOf, change, blur, validateAll, setServerErrors }), [values, errorOf, change, blur, validateAll, setServerErrors]);
}

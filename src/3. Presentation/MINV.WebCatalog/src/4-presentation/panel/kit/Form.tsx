// USO · Formulario del panel: sin la validación del navegador (la hace la pantalla y, siempre, el servidor), con el
// error general arriba y los botones al pie. Después de enviar, el foco va al primer campo marcado con error.
//
//   <Form onSubmit={guardar} error={comando.errorText} busy={comando.sending}
//     actions={<><Button variant="outline" onClick={cancelar}>Cancelar</Button>
//               <Button type="submit" loading={comando.sending}>Guardar</Button></>}>
//     <FormGrid>
//       <TextField label="Nombre" value={nombre} onChange={setNombre} error={errores.nombre} required />
//       <MoneyField label="Precio" value={precio} onChange={setPrecio} error={errores.precio} required />
//       <TextArea label="Notas" value={notas} onChange={setNotas} className="sm:col-span-2" optional />
//     </FormGrid>
//   </Form>
//
// Dentro de un Dialog, ponga los botones en el pie del diálogo y únalos con `id`: `<Form id="form-x">` y
// `<Button type="submit" form="form-x">`.

import clsx from 'clsx';
import { useRef, type FormHTMLAttributes, type ReactNode } from 'react';
import { Alert } from '@/4-presentation/components/ui/Alert';

export interface FormProps extends Omit<FormHTMLAttributes<HTMLFormElement>, 'onSubmit' | 'noValidate' | 'children'> {
  /** Validar y enviar. Puede devolver una promesa (el foco va al primer error cuando termina). */
  onSubmit: () => unknown;
  /** Error general (por ejemplo `comando.errorText`). */
  error?: ReactNode;
  /** Botones al pie (el principal a la derecha). */
  actions?: ReactNode;
  /** Se está enviando (aria-busy). */
  busy?: boolean;
  children: ReactNode;
}

export function Form({ onSubmit, error, actions, busy = false, className, children, ...form }: FormProps) {
  const ref = useRef<HTMLFormElement>(null);
  return (
    <form
      ref={ref}
      noValidate
      aria-busy={busy || undefined}
      onSubmit={(event) => {
        event.preventDefault();
        if (busy) return;
        void Promise.resolve(onSubmit()).finally(() => {
          setTimeout(() => ref.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), 0);
        });
      }}
      className={clsx('min-w-0 space-y-4', className)}
      {...form}
    >
      {error && <Alert tone="danger">{error}</Alert>}
      {children}
      {actions && <div className="flex flex-col-reverse gap-2 pt-2 sm:flex-row sm:justify-end">{actions}</div>}
    </form>
  );
}

/** Grilla de campos: una columna en el teléfono y dos desde 640 px (`className="sm:col-span-2"` para ocupar las dos). */
export function FormGrid({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={clsx('grid grid-cols-1 gap-4 sm:grid-cols-2', className)}>{children}</div>;
}

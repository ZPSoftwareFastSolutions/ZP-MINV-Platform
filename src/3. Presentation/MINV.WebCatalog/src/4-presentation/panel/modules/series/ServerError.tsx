// Módulo «Series» · el error de un comando DENTRO de su formulario o diálogo: el mensaje del servidor y, si trae más de
// uno (validación de varios campos), la lista completa. Se pasa como `error` de `Form` o de `ConfirmDialog` SOLO cuando
// hay error: `error={comando.errorText && <ServerError text={comando.errorText} details={comando.error?.errors} />}`.

export interface ServerErrorProps {
  /** `comando.errorText` (en palabras, con el permiso que falta si fue por permisos). */
  text: string;
  /** `comando.error?.errors` (los mensajes de validación del servidor). */
  details?: readonly string[];
}

export function ServerError({ text, details = [] }: ServerErrorProps) {
  const extra = details.filter((detail) => detail.trim() && detail !== text);
  return (
    <div data-testid="error-del-servidor">
      <p>{text}</p>
      {extra.length > 0 && (
        <ul className="mt-1 list-disc space-y-0.5 pl-5">
          {extra.map((detail) => (
            <li key={detail}>{detail}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

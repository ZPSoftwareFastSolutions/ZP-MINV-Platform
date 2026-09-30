// Módulo «Estado del SIAT» · la hora actual para las cuentas regresivas de los plazos (se actualiza sola cada 30 s mientras
// la pantalla está abierta, como el escritorio).

import { useEffect, useState } from 'react';

export function useNow(intervalMs = 30_000): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

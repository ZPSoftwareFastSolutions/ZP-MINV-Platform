// Módulo «Reservas» · la hora de ahora, que se renueva sola cada minuto: así el resaltado «vence en menos de 6 horas» y
// el estado «Vencida» cambian mientras la lista está abierta, sin recargar.

import { useEffect, useState } from 'react';

export const NOW_REFRESH_MS = 60_000;

export function useNow(intervalMs: number = NOW_REFRESH_MS): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

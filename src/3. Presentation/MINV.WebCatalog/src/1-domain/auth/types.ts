// Dominio de la sesión web (V7): quién ingresó, con qué roles y permisos y sobre qué sucursales. Son tipos del DOMINIO
// (fechas como Date): el JSON exacto del servidor vive en 3-infrastructure/http/contract.generated.ts. La sesión NO
// trae el token: viaja en una cookie HttpOnly que el JavaScript no ve (regla P-02) y nada de esto se guarda en el navegador.

/** «staff» = personal de la tienda (entra al panel) · «customer» = cuenta de cliente (su único rol es CLIENTE). */
export type SessionKind = 'staff' | 'customer';

export interface SessionBranch {
  id: string;
  code: string;
  name: string;
}

/** Alcance por sucursal que calculó el servidor (regla B-01): todas, las asignadas y la activa. */
export interface BranchAccess {
  allBranches: boolean;
  branches: SessionBranch[];
  activeBranchId: string | null;
}

export interface Session {
  displayName: string;
  email: string;
  /** Códigos de rol (`ADMIN`, `VENTAS`, `CLIENTE`…). */
  roles: string[];
  /** Códigos de permiso (`sales.view`, `account.manage`…). Ocultar un botón es comodidad: el servidor decide (P-01). */
  permissions: string[];
  /** El administrador asignó la contraseña: hay que cambiarla antes de usar cualquier pantalla protegida. */
  mustChangePassword: boolean;
  access: BranchAccess;
  kind: SessionKind;
  /** Vence sin actividad (12 h); el servidor la renueva con cada petición. */
  expiresAt: Date;
  serverVersion: string;
  /** Nombre de la empresa de la sesión. */
  company: string;
}

export interface Credentials {
  email: string;
  password: string;
}

/** Datos para crear una cuenta de cliente. El rol NO viaja: el registro crea siempre una cuenta CLIENTE (regla P-03). */
export interface Registration {
  name: string;
  email: string;
  phone: string;
  password: string;
}

/** Por qué dejó de haber sesión: venció (401 del servidor) o la persona la cerró. */
export type SessionEnd = 'expired' | 'logout';

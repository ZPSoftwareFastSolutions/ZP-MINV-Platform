/**
 * M-INV V2 · Pruebas automáticas de los Office Scripts (sin Excel).
 *
 *   node tests/office-scripts/pruebas.mts            (requiere build/v2/fixture.json: tools/build_minv_v2.py)
 *
 * Cada prueba carga el script REAL de src/office-scripts (Node elimina los tipos de TypeScript), lo ejecuta contra
 * un libro simulado con el estado del Core generado y verifica: autorización por correo, poka-yoke de stock,
 * inmutabilidad y auditoría de la bitácora, registros simultáneos, protección de hojas siempre restaurada y que
 * RecalcularStock produzca exactamente la misma instantánea que el generador (misma regla, dos implementaciones).
 */
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { stripTypeScriptTypes } from "node:module";
import { ENUMS, MockLibro, type Celda, type Fixture } from "./mock-excelscript.mts";

const RAIZ = path.resolve(import.meta.dirname, "..", "..");
const FIXTURE = path.join(RAIZ, "build", "v2", "fixture.json");
const fx: Fixture = JSON.parse(fs.readFileSync(FIXTURE, "utf8"));
const [anio, mes, dia] = fx.hoy.split("-").map(Number);
const AHORA = new Date(anio, mes - 1, dia, 18, 30, 0);
const HOY = (Date.UTC(anio, mes - 1, dia) - Date.UTC(1899, 11, 30)) / 86400000;
const ESP = fx.esperado as { stock: Celda[][]; alertas: Celda[][]; ejemplo_agotado: string; ejemplo_con_stock: string;
  stock_con_stock: number };

const U = {
  admin: { correo: "admin@distribuidorademo.example", nombre: "Administrador M-INV" },
  ana: { correo: "ana.gomez@distribuidorademo.example", nombre: "Ana Gómez" },
  laura: { correo: "laura.mendez@distribuidorademo.example", nombre: "Laura Méndez" },
  carlos: { correo: "carlos.ruiz@distribuidorademo.example", nombre: "Carlos Ruiz" },
  sofia: { correo: "sofia.lopez@distribuidorademo.example", nombre: "Sofía López" },
  gerencia: { correo: "gerencia@distribuidorademo.example", nombre: "Gerencia" },
};
const ID_S = /^S-\d{8}-\d{6}-[0-9A-F]{4}$/;
const ID_E = /^E-\d{8}-\d{6}-[0-9A-F]{4}$/;

// ------------------------------------------------------------------------------------------------ carga de scripts
const PRELUDIO = `
const __DateReal = Date;
Date = class extends __DateReal {
  constructor(...a) { if (a.length === 0) { super(__ahora()); } else { super(...a); } }
  static now() { return __ahora(); }
};
Math.random = __azar;
`;

type Main = (wb: MockLibro) => string;

function cargar(nombre: string, clave = fx.password): Main {
  const ts = fs.readFileSync(path.join(RAIZ, "src", "office-scripts", nombre + ".ts"), "utf8")
    .replace('"__MINV_PASSWORD__"', JSON.stringify(clave));
  let semilla = 20260925;
  const contexto: Record<string, unknown> = {
    console: { log: (): void => undefined },
    ExcelScript: ENUMS,
    __ahora: (): number => AHORA.getTime(),
    __azar: (): number => {
      semilla = (semilla * 1103515245 + 12345) % 2147483648;
      return semilla / 2147483648;
    },
  };
  vm.createContext(contexto);
  vm.runInContext(PRELUDIO + stripTypeScriptTypes(ts) + "\n;globalThis.__main = main;", contexto,
    { filename: nombre + ".ts" });
  return contexto.__main as Main;
}

const SCRIPTS = {
  entrada: cargar("RegistrarEntrada"),
  salida: cargar("RegistrarSalida"),
  recalcular: cargar("RecalcularStock"),
  diagnostico: cargar("DiagnosticoInstalacion"),
};

function libro(usuario: { correo: string; nombre: string }): MockLibro {
  const wb = new MockLibro(fx);
  wb.usuario = { ...usuario };
  return wb;
}

// ------------------------------------------------------------------------------------------------ mini framework
let fallas = 0;
let total = 0;

function prueba(nombre: string, cuerpo: () => void): void {
  total++;
  try {
    cuerpo();
    console.log("  [OK]    " + nombre);
  } catch (e) {
    fallas++;
    console.log("  [FALLA] " + nombre + "\n          " + (e as Error).message);
  }
}

function afirmar(condicion: boolean, mensaje: string): void {
  if (!condicion) {
    throw new Error(mensaje);
  }
}

function lanza(fn: () => void, contiene: string): string {
  try {
    fn();
  } catch (e) {
    const m = (e as Error).message;
    afirmar(m.indexOf(contiene) >= 0, `se esperaba un error con «${contiene}» y llegó «${m}»`);
    return m;
  }
  throw new Error(`se esperaba un error con «${contiene}» y no hubo error`);
}

function igualAprox(a: Celda, b: Celda): boolean {
  if (typeof a === "number" && typeof b === "number") {
    return Math.abs(a - b) < 1e-6;
  }
  return a === b;
}

function hojasIntactas(wb: MockLibro, hojas: string[]): void {
  for (const h of hojas) {
    const p = wb.hojas.get(h)!.proteccion;
    afirmar(p.protegida && !p.pausada && p.clave === fx.password, `la protección de ${h} no quedó restaurada`);
  }
}

function disponible(wb: MockLibro, sku: string): number {
  let s = 0;
  for (const t of ["tblEntradas", "tblSalidas"]) {
    const tabla = wb.tabla(t);
    const cS = tabla.encabezados.indexOf("SKU"), cN = tabla.encabezados.indexOf("CantidadNeta");
    const cE = tabla.encabezados.indexOf("Estado");
    for (const f of tabla.filas) {
      if (f[cS] === sku && String(f[cE]).startsWith("✔")) {
        s += Number(f[cN]);
      }
    }
  }
  return Math.round(s * 1e6) / 1e6;
}

function etiqueta(sku: string): string {
  const p = fx.tablas.tblProductos;
  const f = p.filas.find((x: Celda[]) => x[p.encabezados.indexOf("SKU")] === sku)!;
  return sku + " · " + f[p.encabezados.indexOf("Producto")];
}

const CAP_E = "tblCapturaEntradas", CAP_S = "tblCapturaSalidas";

// ================================================================================================= PRUEBAS
console.log("M-INV V2 · pruebas de los Office Scripts (" + fx.hoy + ")");

prueba("RegistrarSalida: salida válida → bitácora oficial auditada, captura limpia, hoja protegida", () => {
  const wb = libro(U.sofia);
  const sku = ESP.ejemplo_con_stock;
  const antes = wb.tabla("tblSalidas").filas.length;
  const m = SCRIPTS.salida(wb);
  afirmar(m.startsWith("✔ Registrado S-"), "mensaje: " + m);
  const t = wb.tabla("tblSalidas");
  afirmar(t.filas.length === antes + 1, "no se agregó exactamente una fila");
  const r = wb.fila("tblSalidas", t.filas.length - 1);
  afirmar(ID_S.test(String(r.ID)), "ID con formato inesperado: " + r.ID);
  afirmar(r.Usuario_O365 === U.sofia.correo && r["Registró"] === U.sofia.nombre, "auditoría de usuario incorrecta");
  afirmar(typeof r.Timestamp === "number" && Math.floor(r.Timestamp as number) === HOY, "Timestamp: " + r.Timestamp);
  afirmar(r.CantidadNeta === -2 && r.FactorStock === -1 && r.Tipo === "SALIDA", "cantidad neta/factor/tipo incorrectos");
  afirmar(r.Estado === "✔ Consolidado" && r.SKU === sku && r.Producto === etiqueta(sku), "estado/SKU/etiqueta");
  afirmar(disponible(wb, sku) === ESP.stock_con_stock - 2, "el disponible no bajó en 2");
  const c = wb.fila(CAP_S, wb.filaPorCorreo(CAP_S, U.sofia.correo));
  afirmar(c.Producto === "" && c.Cantidad === "" && c.Documento === "", "la fila de captura no quedó limpia");
  afirmar(String(c.Resultado).startsWith("✔ Registrado " + r.ID), "resultado en la captura: " + c.Resultado);
  hojasIntactas(wb, ["10B_SALIDAS"]);
});

prueba("Poka-yoke: salida mayor que el disponible → bloqueada, nada se escribe en la bitácora", () => {
  const wb = libro(U.carlos);
  const antes = wb.tabla("tblSalidas").filas.length;
  const m = SCRIPTS.salida(wb);
  afirmar(m.startsWith("✖ Bloqueado: stock insuficiente: disponible 0"), "mensaje: " + m);
  afirmar(wb.tabla("tblSalidas").filas.length === antes, "no debía agregar filas");
  const c = wb.fila(CAP_S, wb.filaPorCorreo(CAP_S, U.carlos.correo));
  afirmar(c.Cantidad === 3 && String(c.Resultado).startsWith("✖ Bloqueado"), "la captura debía conservarse con el motivo");
  hojasIntactas(wb, ["10B_SALIDAS"]);
});

prueba("Registros simultáneos: otra salida se adelanta → el propio registro queda «✖ Rechazado» y no suma", () => {
  const wb = libro(U.sofia);
  const sku = ESP.ejemplo_con_stock;
  const s0 = ESP.stock_con_stock;
  wb.antesDeAgregar.set("tblSalidas", (t) => {
    const fila: Celda[] = t.encabezados.map((e: string) => ({
      ID: "S-20260925-182959-BEEF", Tipo: "SALIDA", Fecha: HOY, Producto: etiqueta(sku), Cantidad: s0 - 1,
      Documento: "Otro vendedor", Observaciones: "", CantidadNeta: -(s0 - 1), Estado: "✔ Consolidado",
      "Registró": "Carlos Ruiz", Unidad: "UND", FactorStock: -1, Usuario_O365: U.carlos.correo, SKU: sku,
      Timestamp: HOY + 0.77
    } as Record<string, Celda>)[e] ?? "");
    t.filas.push(fila);
  });
  const m = SCRIPTS.salida(wb);
  afirmar(m.startsWith("✖ Rechazado S-"), "mensaje: " + m);
  const t = wb.tabla("tblSalidas");
  const propia = wb.fila("tblSalidas", t.filas.length - 1);
  afirmar(String(propia.Estado).startsWith("✖ Rechazado"), "el registro propio debía quedar rechazado");
  afirmar(disponible(wb, sku) === 1, "el stock consolidado debía quedar en 1 (la salida rechazada no suma)");
  const c = wb.fila(CAP_S, wb.filaPorCorreo(CAP_S, U.sofia.correo));
  afirmar(String(c.Resultado).startsWith("✖ Bloqueado: otra persona registró antes"), "captura: " + c.Resultado);
  hojasIntactas(wb, ["10B_SALIDAS"]);
});

prueba("RegistrarEntrada: ENTRADA válida (+20) con ID «E-», conserva el Tipo para el siguiente registro", () => {
  const wb = libro(U.ana);
  const sku = ESP.ejemplo_agotado;
  const m = SCRIPTS.entrada(wb);
  afirmar(m.startsWith("✔ Registrado E-"), "mensaje: " + m);
  const t = wb.tabla("tblEntradas");
  const r = wb.fila("tblEntradas", t.filas.length - 1);
  afirmar(ID_E.test(String(r.ID)) && r.CantidadNeta === 20 && r.FactorStock === 1 && r.SKU === sku, "fila: " + JSON.stringify(r));
  afirmar(r.Usuario_O365 === U.ana.correo && r.Documento === "FC-10290", "auditoría/documento");
  afirmar(disponible(wb, sku) === 20, "el disponible debía ser 20");
  const c = wb.fila(CAP_E, wb.filaPorCorreo(CAP_E, U.ana.correo));
  afirmar(c.Tipo === "ENTRADA" && c.Producto === "" && c.Cantidad === "", "la captura debía conservar solo el Tipo");
  hojasIntactas(wb, ["10A_ENTRADAS"]);
});

prueba("Validaciones de Bodega: ajuste sin observación, ajuste (-) sin stock, saldo inicial repetido, fecha futura, decimales", () => {
  const casos: [Record<string, Celda>, string][] = [
    [{ Tipo: "AJUSTE (+)", Producto: etiqueta(ESP.ejemplo_con_stock), Cantidad: 1, Observaciones: "" }, "exigen una observación"],
    [{ Tipo: "AJUSTE (-)", Producto: etiqueta(ESP.ejemplo_agotado), Cantidad: 1, Observaciones: "Merma" }, "stock insuficiente"],
    [{ Tipo: "SALDO INICIAL", Producto: etiqueta(ESP.ejemplo_con_stock), Cantidad: 5 }, "ya tiene SALDO INICIAL"],
    [{ Tipo: "ENTRADA", Producto: etiqueta(ESP.ejemplo_con_stock), Cantidad: 5, Fecha: HOY + 10 }, "fecha no es válida"],
    [{ Tipo: "ENTRADA", Producto: etiqueta(ESP.ejemplo_con_stock), Cantidad: 1.5 }, "no admite decimales"],
    [{ Tipo: "SALIDA", Producto: etiqueta(ESP.ejemplo_con_stock), Cantidad: 1 }, "no es válido en este fragmento"],
    [{ Tipo: "ENTRADA", Producto: "ZZZ-999 · No existe", Cantidad: 1 }, "no existe en el catálogo"],
  ];
  for (const [valores, esperado] of casos) {
    const wb = libro(U.laura);
    const i = wb.filaPorCorreo(CAP_E, U.laura.correo);
    wb.poner(CAP_E, i, valores);
    const antes = wb.tabla("tblEntradas").filas.length;
    const m = SCRIPTS.entrada(wb);
    afirmar(m.indexOf(esperado) >= 0, `caso ${JSON.stringify(valores)}: ${m}`);
    afirmar(wb.tabla("tblEntradas").filas.length === antes, "un caso inválido escribió en la bitácora");
    hojasIntactas(wb, ["10A_ENTRADAS"]);
  }
});

prueba("RBAC: rol equivocado, usuario inactivo y cuenta sin fila de captura", () => {
  let wb = libro(U.carlos);
  const i = 5;
  wb.poner(CAP_E, i, { Correo: U.carlos.correo, Usuario: U.carlos.nombre, Tipo: "ENTRADA",
    Producto: etiqueta(ESP.ejemplo_con_stock), Cantidad: 1 });
  afirmar(SCRIPTS.entrada(wb).indexOf("su rol (VENTAS) no permite") >= 0, "rol VENTAS no debía registrar en Bodega");
  wb = libro(U.sofia);
  const u = wb.tabla("tblUsuarios");
  const fila = u.filas.findIndex((f: Celda[]) => f[0] === U.sofia.correo);
  u.filas[fila][u.encabezados.indexOf("Activo")] = "NO";
  afirmar(SCRIPTS.salida(wb).indexOf("inactivo") >= 0, "un usuario inactivo no debía registrar");
  wb = libro(U.gerencia);
  lanza(() => SCRIPTS.salida(wb), "no tiene fila de captura");
});

prueba("Identidad: reintenta si la celda del comentario está ocupada; sin correo de Microsoft 365 no registra", () => {
  let wb = libro(U.sofia);
  wb.fallosComentario = 1;
  afirmar(SCRIPTS.salida(wb).startsWith("✔"), "debía registrar tras reintentar la identificación");
  afirmar(wb.comentarios.size === 0, "el comentario temporal debía borrarse");
  wb = libro({ correo: "", nombre: "Invitado" });
  lanza(() => SCRIPTS.salida(wb), "No se pudo identificar");
});

prueba("Protección: contraseña equivocada no escribe nada; sin pauseProtection usa desproteger/proteger", () => {
  let wb = libro(U.sofia);
  const antes = wb.tabla("tblSalidas").filas.length;
  lanza(() => cargar("RegistrarSalida", "otra-clave")(wb), "contraseña del script no coincide");
  afirmar(wb.tabla("tblSalidas").filas.length === antes, "no debía escribir con la contraseña equivocada");
  hojasIntactas(wb, ["10B_SALIDAS"]);
  wb = libro(U.sofia);
  wb.permitePausa = false;
  afirmar(SCRIPTS.salida(wb).startsWith("✔"), "debía registrar desprotegiendo y protegiendo");
  hojasIntactas(wb, ["10B_SALIDAS"]);
});

prueba("Release vacío: el primer registro ocupa la fila en blanco de la tabla (no deja un hueco)", () => {
  const wb = libro(U.ana);
  const t = wb.tabla("tblEntradas");
  t.filas = [t.encabezados.map(() => "" as Celda)];
  const m = SCRIPTS.entrada(wb);
  afirmar(m.startsWith("✖ Bloqueado") || m.startsWith("✔"), m);
  afirmar(t.filas.length === 1 && String(t.filas[0][0]).startsWith("E-"), "debía usar la fila en blanco existente");
});

prueba("RecalcularStock: instantánea idéntica a la del generador (stock, semáforo y alertas priorizadas)", () => {
  const wb = libro(U.admin);
  const m = SCRIPTS.recalcular(wb);
  afirmar(m.startsWith("✔ Stock recalculado: " + ESP.stock.length + " productos"), "mensaje: " + m);
  for (const [tabla, esperado] of [["tblStock", ESP.stock], ["tblAlertas", ESP.alertas]] as [string, Celda[][]][]) {
    const filas = wb.tabla(tabla).filas;
    esperado.forEach((fila: Celda[], i: number) => fila.forEach((v: Celda, j: number) => {
      afirmar(igualAprox(filas[i][j], v), `${tabla} fila ${i + 1} col ${wb.tabla(tabla).encabezados[j]}: ${filas[i][j]} ≠ ${v}`);
    }));
    afirmar(filas.slice(esperado.length).every((f: Celda[]) => f.every((v: Celda) => v === "")), tabla + ": sobran filas");
  }
  afirmar(wb.nombres.get("stkActualizadoPor")!.valor === U.admin.correo, "no registró quién recalculó");
  afirmar(Math.floor(wb.nombres.get("stkActualizado")!.valor as number) === HOY, "fecha del cálculo");
  afirmar(wb.calculos === 1, "debía pedir un recálculo completo del libro");
  hojasIntactas(wb, ["15_STOCK", "16_ALERTAS", "01_CONFIG"]);
});

prueba("Comando + proyección: tras una salida, RecalcularStock refleja el nuevo stock", () => {
  const wb = libro(U.sofia);
  SCRIPTS.salida(wb);
  SCRIPTS.recalcular(wb);
  const t = wb.tabla("tblStock");
  const f = t.filas.find((x: Celda[]) => x[0] === ESP.ejemplo_con_stock)!;
  afirmar(f[t.encabezados.indexOf("StockActual")] === ESP.stock_con_stock - 2, "stock en la instantánea: " + f[8]);
});

prueba("DiagnosticoInstalacion: informe completo y sin problemas; detecta la contraseña equivocada", () => {
  let wb = libro(U.sofia);
  const m = SCRIPTS.diagnostico(wb);
  afirmar(m.indexOf("RESULTADO: instalación correcta") >= 0, m);
  afirmar(String(wb.hojas.get("92_SESION")!.celdas.get("A3")).startsWith("M-INV V2"), "el informe no quedó en 92_SESION");
  wb = libro(U.sofia);
  const m2 = cargar("DiagnosticoInstalacion", "otra-clave")(wb);
  afirmar(m2.indexOf("problema(s) por resolver") >= 0 && m2.indexOf("contraseña del script no coincide") >= 0, m2);
});

console.log(fallas === 0 ? `RESULTADO: ${total} pruebas sin fallas` : `RESULTADO: ${fallas} de ${total} pruebas fallaron`);
process.exit(fallas === 0 ? 0 : 1);

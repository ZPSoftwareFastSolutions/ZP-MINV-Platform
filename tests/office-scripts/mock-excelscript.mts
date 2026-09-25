/**
 * M-INV V2 · Simulador mínimo de la API ExcelScript para probar los Office Scripts fuera de Excel.
 *
 * Implementa solo lo que usan los scripts y reproduce las reglas que importan para la coautoría:
 *  - una hoja protegida rechaza escrituras salvo que la protección esté pausada (sesión) o quitada;
 *  - pauseProtection/unprotect exigen la contraseña;
 *  - un comentario lo crea el usuario de la sesión (getAuthorEmail) y una celda no admite dos comentarios;
 *  - Table.addRow agrega al final (ganchos para simular a otra persona registrando al mismo tiempo).
 * No evalúa fórmulas: el fixture trae los valores ya calculados (lo que Excel devolvería con getValues).
 */
export type Celda = string | number | boolean;

export interface FixtureTabla {
  hoja: string;
  encabezados: string[];
  filas: Celda[][];
}

export interface Fixture {
  password: string;
  hoy: string;
  hojas: { nombre: string; protegida: boolean }[];
  tablas: Record<string, FixtureTabla>;
  nombres: Record<string, { hoja: string; valor: Celda }>;
  esperado: Record<string, unknown>;
}

function protegidaError(hoja: string): Error {
  return new Error("AccessDenied: la celda o el gráfico que intenta cambiar está en una hoja protegida (" + hoja + ")");
}

export class MockProteccion {
  protegida: boolean;
  pausada = false;
  clave: string;
  opciones: Record<string, boolean> = { allowAutoFilter: true, allowFormatColumns: true };
  libro: MockLibro;

  constructor(libro: MockLibro, protegida: boolean, clave: string) {
    this.libro = libro;
    this.protegida = protegida;
    this.clave = clave;
  }

  getProtected(): boolean {
    return this.protegida;
  }

  getIsPaused(): boolean {
    return this.pausada;
  }

  getOptions(): Record<string, boolean> {
    return { ...this.opciones };
  }

  pauseProtection(password?: string): void {
    if (!this.libro.permitePausa) {
      throw new Error("ApiNotFound: pauseProtection no está disponible en este anfitrión");
    }
    if (!this.protegida) {
      return;
    }
    if (password !== this.clave) {
      throw new Error("InvalidArgument: la contraseña no es correcta");
    }
    this.pausada = true;
  }

  resumeProtection(): void {
    this.pausada = false;
  }

  unprotect(password?: string): void {
    if (this.protegida && password !== this.clave) {
      throw new Error("InvalidArgument: la contraseña no es correcta");
    }
    this.protegida = false;
    this.pausada = false;
  }

  protect(opciones?: Record<string, boolean>, password?: string): void {
    this.protegida = true;
    this.pausada = false;
    this.clave = password ?? "";
    if (opciones) {
      this.opciones = { ...opciones };
    }
  }

  bloqueada(): boolean {
    return this.protegida && !this.pausada;
  }
}

export class MockHoja {
  nombre: string;
  proteccion: MockProteccion;
  celdas = new Map<string, Celda>();

  constructor(libro: MockLibro, nombre: string, protegida: boolean, clave: string) {
    this.nombre = nombre;
    this.proteccion = new MockProteccion(libro, protegida, clave);
  }

  getName(): string {
    return this.nombre;
  }

  getProtection(): MockProteccion {
    return this.proteccion;
  }

  /** Solo rangos de una columna del tipo "A3:A20" (lo que usa el diagnóstico). */
  getRange(direccion: string): MockRangoHoja {
    return new MockRangoHoja(this, direccion);
  }

  exigirEscritura(): void {
    if (this.proteccion.bloqueada()) {
      throw protegidaError(this.nombre);
    }
  }
}

export class MockRangoHoja {
  hoja: MockHoja;
  direccion: string;

  constructor(hoja: MockHoja, direccion: string) {
    this.hoja = hoja;
    this.direccion = direccion;
  }

  setValues(valores: Celda[][]): void {
    this.hoja.exigirEscritura();
    const m = /^([A-Z]+)(\d+):([A-Z]+)(\d+)$/.exec(this.direccion);
    if (!m) {
      throw new Error("Dirección no soportada por el simulador: " + this.direccion);
    }
    const r0 = Number(m[2]), r1 = Number(m[4]);
    if (valores.length !== r1 - r0 + 1) {
      throw new Error("InvalidArgument: las dimensiones de setValues no coinciden con el rango");
    }
    valores.forEach((f: Celda[], i: number) => this.hoja.celdas.set(m[1] + (r0 + i), f[0]));
  }
}

export class MockTabla {
  nombre: string;
  hoja: MockHoja;
  encabezados: string[];
  filas: Celda[][];
  libro: MockLibro;

  constructor(libro: MockLibro, nombre: string, hoja: MockHoja, encabezados: string[], filas: Celda[][]) {
    this.libro = libro;
    this.nombre = nombre;
    this.hoja = hoja;
    this.encabezados = encabezados.slice();
    this.filas = filas.map((f: Celda[]) => f.slice());
  }

  getName(): string {
    return this.nombre;
  }

  getWorksheet(): MockHoja {
    return this.hoja;
  }

  getRowCount(): number {
    return this.filas.length;
  }

  getHeaderRowRange(): MockRango {
    return new MockRango(this, -1, 0, 1, this.encabezados.length);
  }

  getRangeBetweenHeaderAndTotal(): MockRango {
    return new MockRango(this, 0, 0, this.filas.length, this.encabezados.length);
  }

  getColumnByName(nombre: string): MockColumna | undefined {
    const i = this.encabezados.indexOf(nombre);
    return i < 0 ? undefined : new MockColumna(this, i);
  }

  addRow(indice?: number, valores?: Celda[]): void {
    this.hoja.exigirEscritura();
    if (!valores || valores.length !== this.encabezados.length) {
      throw new Error("InvalidArgument: addRow espera " + this.encabezados.length + " valores");
    }
    const gancho = this.libro.antesDeAgregar.get(this.nombre);
    if (gancho) {
      this.libro.antesDeAgregar.delete(this.nombre);
      gancho(this);
    }
    if (indice === undefined || indice === null || indice === -1) {
      this.filas.push(valores.slice());
    } else {
      this.filas.splice(indice, 0, valores.slice());
    }
    this.libro.registro.push("addRow " + this.nombre);
  }
}

export class MockColumna {
  tabla: MockTabla;
  indice: number;

  constructor(tabla: MockTabla, indice: number) {
    this.tabla = tabla;
    this.indice = indice;
  }

  getRangeBetweenHeaderAndTotal(): MockRango {
    return new MockRango(this.tabla, 0, this.indice, this.tabla.filas.length, 1);
  }
}

/** Vista rectangular sobre una tabla (fila -1 = encabezados). */
export class MockRango {
  tabla: MockTabla;
  fila0: number;
  col0: number;
  nFilas: number;
  nCols: number;

  constructor(tabla: MockTabla, fila0: number, col0: number, nFilas: number, nCols: number) {
    this.tabla = tabla;
    this.fila0 = fila0;
    this.col0 = col0;
    this.nFilas = nFilas;
    this.nCols = nCols;
  }

  getValues(): Celda[][] {
    const salida: Celda[][] = [];
    for (let r = 0; r < this.nFilas; r++) {
      const fila: Celda[] = [];
      for (let c = 0; c < this.nCols; c++) {
        fila.push(this.fila0 < 0 ? this.tabla.encabezados[this.col0 + c] : this.tabla.filas[this.fila0 + r][this.col0 + c]);
      }
      salida.push(fila);
    }
    return salida;
  }

  getValue(): Celda {
    return this.getValues()[0][0];
  }

  setValues(valores: Celda[][]): void {
    this.tabla.hoja.exigirEscritura();
    if (this.fila0 < 0) {
      throw new Error("El simulador no permite cambiar encabezados");
    }
    if (valores.length !== this.nFilas || valores.some((f: Celda[]) => f.length !== this.nCols)) {
      throw new Error("InvalidArgument: las dimensiones de setValues no coinciden con el rango");
    }
    valores.forEach((f: Celda[], r: number) => f.forEach((v: Celda, c: number) => {
      this.tabla.filas[this.fila0 + r][this.col0 + c] = v;
    }));
  }

  setValue(valor: Celda): void {
    this.setValues([[valor]]);
  }

  getCell(fila: number, col: number): MockRango {
    return new MockRango(this.tabla, this.fila0 + fila, this.col0 + col, 1, 1);
  }
}

export class MockNombre {
  libro: MockLibro;
  nombre: string;

  constructor(libro: MockLibro, nombre: string) {
    this.libro = libro;
    this.nombre = nombre;
  }

  getRange(): { getValue: () => Celda; setValue: (v: Celda) => void } {
    const n = this.libro.nombres.get(this.nombre)!;
    const hoja = this.libro.hojas.get(n.hoja)!;
    return {
      getValue: (): Celda => n.valor,
      setValue: (v: Celda): void => {
        hoja.exigirEscritura();
        n.valor = v;
      }
    };
  }
}

export class MockComentario {
  libro: MockLibro;
  celda: string;
  autor: { correo: string; nombre: string };

  constructor(libro: MockLibro, celda: string, autor: { correo: string; nombre: string }) {
    this.libro = libro;
    this.celda = celda;
    this.autor = autor;
  }

  getAuthorEmail(): string {
    return this.autor.correo;
  }

  getAuthorName(): string {
    return this.autor.nombre;
  }

  delete(): void {
    this.libro.comentarios.delete(this.celda);
  }
}

export class MockLibro {
  hojas = new Map<string, MockHoja>();
  tablas = new Map<string, MockTabla>();
  nombres = new Map<string, { hoja: string; valor: Celda }>();
  comentarios = new Map<string, MockComentario>();
  usuario = { correo: "", nombre: "" };
  permitePausa = true;
  calculos = 0;
  fallosComentario = 0;
  registro: string[] = [];
  antesDeAgregar = new Map<string, (t: MockTabla) => void>();

  constructor(fx: Fixture) {
    for (const h of fx.hojas) {
      this.hojas.set(h.nombre, new MockHoja(this, h.nombre, h.protegida, fx.password));
    }
    for (const [nombre, t] of Object.entries(fx.tablas)) {
      this.tablas.set(nombre, new MockTabla(this, nombre, this.hojas.get(t.hoja)!, t.encabezados, t.filas));
    }
    for (const [nombre, n] of Object.entries(fx.nombres)) {
      this.nombres.set(nombre, { hoja: n.hoja, valor: n.valor });
    }
  }

  getWorksheet(nombre: string): MockHoja | undefined {
    return this.hojas.get(nombre);
  }

  getTable(nombre: string): MockTabla | undefined {
    return this.tablas.get(nombre);
  }

  getNamedItem(nombre: string): MockNombre | undefined {
    return this.nombres.has(nombre) ? new MockNombre(this, nombre) : undefined;
  }

  addComment(celda: string, _contenido: string): MockComentario {
    const m = /^'([^']+)'!([A-Z]+\d+)$/.exec(celda);
    if (!m || !this.hojas.has(m[1])) {
      throw new Error("InvalidArgument: dirección de comentario no válida " + celda);
    }
    this.hojas.get(m[1])!.exigirEscritura();
    if (this.fallosComentario > 0) {
      this.fallosComentario--;
      throw new Error("InvalidOperation: la celda ya tiene un comentario");
    }
    if (this.comentarios.has(celda)) {
      throw new Error("InvalidOperation: la celda ya tiene un comentario");
    }
    const c = new MockComentario(this, celda, { ...this.usuario });
    this.comentarios.set(celda, c);
    return c;
  }

  getApplication(): { calculate: (t: string) => void; getCalculationMode: () => string } {
    return {
      calculate: (_t: string): void => {
        this.calculos++;
      },
      getCalculationMode: (): string => "Automatic"
    };
  }

  // ------------------------------------------------------------------ ayudas para las pruebas
  tabla(nombre: string): MockTabla {
    return this.tablas.get(nombre)!;
  }

  fila(tabla: string, i: number): Record<string, Celda> {
    const t = this.tabla(tabla);
    const salida: Record<string, Celda> = {};
    t.encabezados.forEach((e: string, c: number) => {
      salida[e] = t.filas[i][c];
    });
    return salida;
  }

  filaPorCorreo(tabla: string, correo: string): number {
    const t = this.tabla(tabla);
    const c = t.encabezados.indexOf("Correo");
    return t.filas.findIndex((f: Celda[]) => String(f[c]).toLowerCase() === correo);
  }

  poner(tabla: string, i: number, valores: Record<string, Celda>): void {
    const t = this.tabla(tabla);
    for (const [k, v] of Object.entries(valores)) {
      t.filas[i][t.encabezados.indexOf(k)] = v;
    }
  }
}

/** Enumeraciones de ExcelScript que usan los scripts. */
export const ENUMS = {
  CalculationType: { recalculate: "Recalculate", full: "Full", fullRebuild: "FullRebuild" },
  ClearApplyTo: { all: "All", formats: "Formats", contents: "Contents" }
};

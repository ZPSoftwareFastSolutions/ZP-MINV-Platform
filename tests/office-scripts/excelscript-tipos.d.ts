/**
 * M-INV V2.1 · Declaraciones mínimas de la API ExcelScript para verificar los tipos de los Office Scripts fuera de
 * Excel (tools/build_v2.ps1, paso opcional con TypeScript). Cubre solo lo que usan los scripts de src/office-scripts,
 * con las mismas firmas de la API oficial; si un script empieza a usar otra función, agréguela aquí.
 */
declare const console: { log(...datos: unknown[]): void };
declare namespace ExcelScript {
  type Valor = string | number | boolean;
  enum CalculationType { recalculate = "Recalculate", full = "Full", fullRebuild = "FullRebuild" }
  enum CalculationMode { automatic = "Automatic", automaticExceptTables = "AutomaticExceptTables", manual = "Manual" }
  interface WorksheetProtectionOptions { allowAutoFilter?: boolean; allowFormatColumns?: boolean; selectionMode?: string }
  interface WorksheetProtection {
    getProtected(): boolean; getIsPaused(): boolean; getOptions(): WorksheetProtectionOptions;
    pauseProtection(password?: string): void; resumeProtection(): void; unprotect(password?: string): void;
    protect(options?: WorksheetProtectionOptions, password?: string): void;
  }
  interface Range {
    getValues(): Valor[][]; getValue(): Valor; setValues(values: Valor[][]): void; setValue(value: Valor): void;
    getCell(row: number, column: number): Range;
  }
  interface TableColumn { getRangeBetweenHeaderAndTotal(): Range }
  interface Table {
    getName(): string; getRowCount(): number; getHeaderRowRange(): Range; getRangeBetweenHeaderAndTotal(): Range;
    getColumnByName(key: string): TableColumn | undefined; addRow(index?: number, values?: Valor[]): void;
    addRows(index?: number, values?: Valor[][]): void;
  }
  interface Worksheet { getName(): string; getProtection(): WorksheetProtection; getRange(address?: string): Range }
  interface NamedItem { getRange(): Range }
  interface Comment { getAuthorEmail(): string; getAuthorName(): string; delete(): void }
  interface Application { calculate(calculationType: CalculationType): void; getCalculationMode(): CalculationMode }
  interface Workbook {
    getWorksheet(name: string): Worksheet | undefined; getTable(key: string): Table | undefined;
    getNamedItem(name: string): NamedItem | undefined; addComment(cellAddress: Range | string, content: string): Comment;
    getApplication(): Application;
  }
}

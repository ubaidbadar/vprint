export interface Printer {
  name: string;
  label: string;
  driver: string;
  port: string;
}
export interface PrintJob {
  id: string;
  printerName: string;
  documentName: string;
  userName: string;
  jobNumber: number;
  pages: number;
  sizeBytes: number;
  printedAt: string;
}
export interface Snapshot {
  printers: Printer[];
  jobs: PrintJob[];
  totalCount: number;
  todayCount: number;
  historyEnabled: boolean;
  historyWarning: string | null;
}

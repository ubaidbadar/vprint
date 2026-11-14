import { invoke } from '@tauri-apps/api/core';
import type { Snapshot } from './types';

export const api = {
  getSnapshot: () => invoke<Snapshot>('get_snapshot'),
  createPrinter: (label: string) => invoke<void>('add_printer', { label }),
  deletePrinter: (name: string) => invoke<void>('delete_printer', { name }),
};
export function errorMessage(error: unknown): string {
  if (typeof error === 'string') return error;
  if (error instanceof Error) return error.message;
  return 'Unexpected Windows error. Please try again.';
}

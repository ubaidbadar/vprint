import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    const printers = [
      { name: 'VPrint - Kitchen', label: 'Kitchen', driver: 'Microsoft Print To PDF', port: 'PORTPROMPT:' },
      { name: 'VPrint - Counter', label: 'Counter', driver: 'Microsoft Print To PDF', port: 'PORTPROMPT:' },
      { name: 'VPrint - Receipt', label: 'Receipt', driver: 'Microsoft Print To PDF', port: 'PORTPROMPT:' },
    ];
    const jobs = [
      { id: '1', printerName: 'VPrint - Kitchen', documentName: 'Order #1042', userName: 'Cashier',
        jobNumber: 19, pages: 1, sizeBytes: 1024, printedAt: '2026-11-15T08:30:00Z' },
      { id: '2', printerName: 'VPrint - Counter', documentName: 'Invoice #278', userName: 'Manager',
        jobNumber: 20, pages: 3, sizeBytes: 2402, printedAt: '2026-11-15T08:25:00Z' },
      { id: '3', printerName: 'VPrint - Receipt', documentName: 'Receipt #771', userName: 'Cashier',
        jobNumber: 21, pages: 1, sizeBytes: 400, printedAt: '2026-11-15T08:20:00Z' },
    ];
    // Mock only the trusted Rust IPC boundary. UI code is otherwise production code.
    Object.assign(window, {
      __TAURI_INTERNALS__: {
        invoke: async (command: string, args?: Record<string, string>) => {
          if (command === 'get_snapshot') return {
            printers: [...printers], jobs: [...jobs],
            totalCount: jobs.length, todayCount: 0, historyEnabled: true, historyWarning: null,
          };
          if (command === 'add_printer') {
            const label = args?.label || '';
            printers.push({ name: 'VPrint - ' + label, label, driver: 'Microsoft Print To PDF', port: 'PORTPROMPT:' });
            return;
          }
          if (command === 'delete_printer') {
            const index = printers.findIndex(item => item.name === args?.name);
            if (index >= 0) printers.splice(index, 1);
            return;
          }
          if (command === 'enable_history') return;
          throw new Error('Unknown mock command: ' + command);
        },
      },
    });
  });
});

test('dashboard loads with three printers and no repeated blocking loader', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Print overview' })).toBeVisible();
  await expect(page.locator('.printer-card')).toHaveCount(3);
  await expect(page.locator('.job-row')).toHaveCount(3);
  await expect(page.locator('.initial-loading')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/desktop-overview.png', fullPage: true });
});

test('filter by printer and search, with inspectable job details', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /Kitchen.*1/ }).first().click();
  await expect(page.locator('.job-row')).toHaveCount(1);
  await expect(page.getByRole('button', { name: /View Order #1042 details/ })).toBeVisible();
  await page.getByRole('button', { name: /View Order #1042 details/ }).click();
  await expect(page.getByRole('dialog', { name: 'Print job details' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: 'Print job details' })).toHaveCount(0);
  await page.getByRole('button', { name: /All print jobs/ }).click();
  await page.getByRole('searchbox').fill('Invoice');
  await expect(page.locator('.job-row')).toHaveCount(1);
});

test('create printer does not open PowerShell or leave the dialog in a loading state', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Add printer' }).click();
  const modal = page.getByRole('dialog', { name: 'Add virtual printer' });
  await expect(modal).toBeVisible();
  await modal.getByLabel('Printer name').fill('Front Desk');
  await modal.getByRole('button', { name: 'Create printer' }).click();
  await expect(modal).toHaveCount(0);
  await expect(page.locator('.printer-card')).toHaveCount(4);
});

test('fits a compact Windows desktop viewport without horizontal page overflow', async ({ page }) => {
  await page.setViewportSize({ width: 1000, height: 720 });
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Print overview' })).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflow).toBe(false);
  await page.screenshot({ path: 'test-results/compact-desktop.png', fullPage: true });
});

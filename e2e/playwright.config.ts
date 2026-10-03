import { defineConfig, devices } from '@playwright/test'

// El sistema ya debe estar arriba (AppHost de Aspire): estas pruebas no lo levantan.
// Las URLs por defecto son los puertos fijos del AppHost; en CI se pueden sobrescribir.
export const urls = {
  gateway: process.env.GATEWAY_URL ?? 'http://localhost:5090',
  insured: process.env.INSURED_URL ?? 'http://localhost:5173',
  adjuster: process.env.ADJUSTER_URL ?? 'http://localhost:5174',
  tower: process.env.TOWER_URL ?? 'http://localhost:5175',
}

export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',
  // Un solo trabajador: las pruebas comparten los 5 ajustadores simulados y cada siniestro ocupa uno.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 30_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    // PW_CHANNEL=chrome usa el Chrome instalado en vez del Chromium de Playwright.
    channel: process.env.PW_CHANNEL || undefined,
    locale: 'es-MX',
    timezoneId: 'America/Monterrey',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], channel: process.env.PW_CHANNEL || undefined } }],
})

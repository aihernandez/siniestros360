import { expect } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import type { Page } from '@playwright/test'
import { urls } from '../playwright.config'

export const password = 'Demo!2026'
export const users = { insured: 'insured.demo@demo.com', adjuster: 'adjuster.demo@demo.com', tower: 'tower.demo@demo.com' } as const
// El usuario adjuster.demo es la unidad "Ajustador 01" del simulador.
export const demoAdjusterId = '11111111-1111-1111-1111-111111111111'

export type ClaimView = { claimId: string; folio: string; status: string; coverageStatus: string; adjusterId: string | null; adjusterName: string | null; slaStage: string | null; slaDueAt: string | null; requiresAmbulance: boolean; documentCount: number; reportedAt: string }
export type AdjusterView = { adjusterId: string; displayName: string; status: string; isAvailable: boolean; latitude: number | null; longitude: number | null; capturedAt: string | null; gpsStale: boolean }

// El gateway limita el login a 10 por minuto por IP: los tokens se reutilizan dentro de la corrida.
const tokens = new Map<string, string>()
export const login = async (email: string) => {
  const cached = tokens.get(email)
  if (cached) return cached
  const response = await fetch(`${urls.gateway}/api/v1/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password }) })
  expect(response.status, response.status === 429 ? 'límite de login del gateway (10 por minuto por IP): espera un minuto entre corridas' : `login ${email}`).toBe(200)
  const token = (await response.json() as { accessToken: string }).accessToken
  tokens.set(email, token)
  return token
}

export const api = (token: string) => {
  const call = (path: string, init: RequestInit = {}) => fetch(`${urls.gateway}${path}`, { ...init, headers: { authorization: `Bearer ${token}`, ...init.headers } })
  return {
    call,
    get: async <T>(path: string) => { const response = await call(path); expect(response.status, `GET ${path}`).toBe(200); return await response.json() as T },
    // Comando idempotente: cada intención lleva su propia Idempotency-Key.
    command: (path: string, body?: unknown, key: string = crypto.randomUUID()) => call(path, {
      method: 'POST',
      headers: { 'Idempotency-Key': key, ...(body === undefined ? {} : { 'content-type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  }
}

// Cancela los siniestros abiertos de corridas anteriores para que la saga libere a sus ajustadores.
export const cancelOpenClaims = async (tower: ReturnType<typeof api>) => {
  const claims = await tower.get<ClaimView[]>('/api/v1/operations/claims')
  for (const claim of claims.filter(x => x.status !== 'Closed' && x.status !== 'Cancelled')) {
    const response = await tower.command(`/api/v1/claims/${claim.claimId}/cancel`)
    expect([200, 409], `cancelar ${claim.folio}`).toContain(response.status)
  }
}

// Espera a que el ajustador indicado esté libre, visible para la torre y con GPS de los últimos 30 s.
export const waitForAvailableAdjuster = async (tower: ReturnType<typeof api>, adjusterId = demoAdjusterId) => {
  let adjuster: AdjusterView | undefined
  await expect.poll(async () => {
    adjuster = (await tower.get<AdjusterView[]>('/api/v1/operations/adjusters/map')).find(x => x.adjusterId === adjusterId)
    const fresh = adjuster?.capturedAt && Date.now() - new Date(adjuster.capturedAt).getTime() < 30_000
    return Boolean(adjuster?.isAvailable && adjuster.status === 'Available' && fresh && adjuster.latitude !== null)
  }, { message: 'Ajustador 01 disponible con GPS reciente', timeout: 90_000, intervals: [1000] }).toBe(true)
  return adjuster!
}

// Espera a que la vista de la torre cumpla una condición; devuelve la vista y cuánto tardó desde `since`.
export const waitForClaim = async (tower: ReturnType<typeof api>, claimId: string, condition: (view: ClaimView) => boolean, message: string, timeout = 30_000) => {
  let view: ClaimView | undefined
  await expect.poll(async () => {
    const response = await tower.call(`/api/v1/operations/claims/${claimId}`)
    view = response.ok ? await response.json() as ClaimView : undefined
    return Boolean(view && condition(view))
  }, { message, timeout, intervals: [250] }).toBe(true)
  return view!
}

// Evidencia mínima: un PNG válido de 1×1.
export const tinyPng = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64')

// Capturas para revisar la interfaz: quedan en e2e/screenshots/ con el número de paso.
mkdirSync('screenshots', { recursive: true })
export const capture = (page: Page, name: string, fullPage = false) => page.screenshot({ path: `screenshots/${name}.png`, fullPage })

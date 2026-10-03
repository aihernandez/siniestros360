import { expect } from '@playwright/test'
import { urls } from './playwright.config'
import { api, cancelOpenClaims, login, users, waitForAvailableAdjuster, waitForClaim } from './tests/support'

// Espera a que el sistema esté listo y lo calienta con un siniestro desechable: tras arrancar, el primer mensaje de
// cada tipo tarda varios segundos más (MassTransit verifica topics y abre enlaces), y eso no es lo que miden las pruebas.
export default async function globalSetup() {
  await expect.poll(async () => (await fetch(`${urls.gateway}/health`).catch(() => null))?.status, { message: `gateway en ${urls.gateway}`, timeout: 300_000, intervals: [2000] }).toBe(200)
  for (const url of [urls.insured, urls.adjuster, urls.tower]) {
    await expect.poll(async () => (await fetch(url).catch(() => null))?.status, { message: `app en ${url}`, timeout: 120_000, intervals: [2000] }).toBe(200)
  }

  const tower = api(await login(users.tower))
  await cancelOpenClaims(tower)
  const unit = await waitForAvailableAdjuster(tower)
  const response = await tower.command('/api/v1/claims', { policyNumber: 'POL-WARMUP-001', vehiclePlate: 'WARM-01', incidentType: 'Collision', latitude: unit.latitude, longitude: unit.longitude, requiresAmbulance: false })
  expect(response.status, 'siniestro de calentamiento').toBe(201)
  const { id } = await response.json() as { id: string }
  await waitForClaim(tower, id, x => x.adjusterId !== null && x.coverageStatus !== 'Pending' && x.slaStage === 'WaitingArrival', 'calentamiento: asignación, póliza y SLA', 120_000)
  expect((await tower.command(`/api/v1/claims/${id}/cancel`)).status).toBe(200)
}

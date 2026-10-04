import { expect, test } from '@playwright/test'
import { api, cancelOpenClaims, demoAdjusterId, login, tinyPng, users, waitForAvailableAdjuster, waitForClaim, type AdjusterView } from './support'

// Recorrido por el gateway contra el sistema completo: Aspire, PostgreSQL/PostGIS y Azure Service Bus.
// Sustituye al recorrido manual de 23 pasos; cada paso es un test.step para leer el reporte como checklist.
test('un siniestro se reporta, se asigna, se atiende y se cierra por el gateway', async () => {
  const tower = api(await login(users.tower))
  const insured = api(await login(users.insured))
  const adjuster = api(await login(users.adjuster))

  const unit = await test.step('preparar: sin siniestros abiertos y Ajustador 01 disponible con GPS', async () => {
    await cancelOpenClaims(tower)
    return await waitForAvailableAdjuster(tower)
  })

  // El siniestro se reporta donde está Ajustador 01 para que sea el más cercano.
  const report = { policyNumber: 'POL-E2E-001', vehiclePlate: 'E2E-001', incidentType: 'Collision', latitude: unit.latitude, longitude: unit.longitude, requiresAmbulance: true }
  const key = crypto.randomUUID()
  const reportedAt = Date.now()
  const claim = await test.step('reportar: 201 con folio inmediato', async () => {
    const response = await insured.command('/api/v1/claims', report, key)
    expect(response.status).toBe(201)
    const body = await response.json() as { id: string; folio: string; coverageStatus: string }
    expect(body.folio).toMatch(/^SIN-\d{4}-/)
    expect(body.coverageStatus).toBe('Pending')
    return body
  })

  await test.step('idempotencia: misma llave y datos responde la misma respuesta marcada como replay', async () => {
    const response = await insured.command('/api/v1/claims', report, key)
    expect(response.status).toBe(201)
    expect(response.headers.get('idempotent-replayed')).toBe('true')
    expect((await response.json() as { id: string }).id).toBe(claim.id)
  })

  await test.step('idempotencia: misma llave con otros datos es 409; sin llave es 400', async () => {
    expect((await insured.command('/api/v1/claims', { ...report, vehiclePlate: 'E2E-002' }, key)).status).toBe(409)
    const missing = await insured.call('/api/v1/claims', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(report) })
    expect(missing.status).toBe(400)
  })

  await test.step('despacho: asigna a Ajustador 01 en menos de 10 s, con póliza vigente y plazo de llegada de 15 min', async () => {
    const view = await waitForClaim(tower, claim.id, x => x.adjusterId === demoAdjusterId && x.slaStage === 'WaitingArrival' && x.coverageStatus !== 'Pending', 'asignación, cobertura y SLA')
    // Antes de separar el GPS en su propia cola, la asignación tardaba 17–28 s detrás de la telemetría.
    expect(Date.now() - reportedAt, 'tiempo hasta la asignación').toBeLessThan(10_000)
    expect(view.adjusterName).toBe('Ajustador 01')
    expect(view.coverageStatus).toBe('Active')
    expect(view.requiresAmbulance).toBe(true)
    const minutes = (new Date(view.slaDueAt!).getTime() - reportedAt) / 60_000
    expect(minutes).toBeGreaterThan(14)
    expect(minutes).toBeLessThanOrEqual(15.5)
  })

  await test.step('estados: iniciar antes de llegar es 409', async () => {
    expect((await adjuster.command(`/api/v1/claims/${claim.id}/service-started`)).status).toBe(409)
  })

  await test.step('ajustador: registra llegada y sube una evidencia', async () => {
    expect((await adjuster.command(`/api/v1/claims/${claim.id}/adjuster-arrived`)).status).toBe(200)
    const form = new FormData()
    form.append('file', new Blob([tinyPng], { type: 'image/png' }), 'evidencia.png')
    const upload = await adjuster.call(`/api/v1/documents/claims/${claim.id}`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: form })
    expect(upload.status).toBe(201)
  })

  await test.step('ajustador: inicia y cierra el servicio', async () => {
    expect((await adjuster.command(`/api/v1/claims/${claim.id}/service-started`)).status).toBe(200)
    expect((await adjuster.command(`/api/v1/claims/${claim.id}/service-completed`)).status).toBe(200)
  })

  await test.step('torre: ve el siniestro cerrado con 1 documento y al ajustador libre', async () => {
    await waitForClaim(tower, claim.id, x => x.status === 'Closed' && x.documentCount === 1, 'cierre con documento')
    await expect.poll(async () => (await tower.get<AdjusterView[]>('/api/v1/operations/adjusters/map')).find(x => x.adjusterId === demoAdjusterId)?.status, { timeout: 30_000 }).toBe('Available')
  })

  await test.step('saga: termina en Closed con la asignación registrada', async () => {
    await expect.poll(async () => (await tower.get<{ status: string }>(`/api/v1/dispatch/claims/${claim.id}`)).status, { timeout: 30_000 }).toBe('Closed')
    const saga = await tower.get<{ attempts: { result: string }[] }>(`/api/v1/dispatch/claims/${claim.id}`)
    expect(saga.attempts.map(x => x.result)).toContain('Assigned')
  })

  await test.step('seguridad: el asegurado no ve las vistas de la torre ni el detalle de la saga', async () => {
    expect((await insured.call('/api/v1/operations/claims')).status).toBe(403)
    expect((await insured.call(`/api/v1/dispatch/claims/${claim.id}`)).status).toBe(403)
  })
})

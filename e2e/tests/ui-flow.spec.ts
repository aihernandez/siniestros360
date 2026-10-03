import { devices, expect, test } from '@playwright/test'
import { urls } from '../playwright.config'
import { api, cancelOpenClaims, capture, login, tinyPng, users, waitForAvailableAdjuster } from './support'

const phone = { ...devices['Pixel 7'], channel: process.env.PW_CHANNEL || undefined, locale: 'es-MX', timezoneId: 'America/Monterrey' }

// El mismo flujo que vive el usuario, en las tres apps a la vez: el asegurado reporta desde el teléfono, la torre lo
// sigue en el mapa y el ajustador lo atiende. Cada paso deja una captura en e2e/screenshots/.
test('asegurado, torre y ajustador atienden un siniestro en vivo', async ({ browser }) => {
  const tower = api(await login(users.tower))
  await cancelOpenClaims(tower)
  const unit = await waitForAvailableAdjuster(tower)

  const towerPage = await (await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'es-MX', timezoneId: 'America/Monterrey' })).newPage()
  const adjusterPage = await (await browser.newContext(phone)).newPage()
  // El GPS del teléfono del asegurado coincide con la posición de Ajustador 01: él es el más cercano.
  const insuredPage = await (await browser.newContext({ ...phone, geolocation: { latitude: unit.latitude!, longitude: unit.longitude! }, permissions: ['geolocation'] })).newPage()
  const errors: string[] = []
  for (const [name, page] of [['torre', towerPage], ['ajustador', adjusterPage], ['asegurado', insuredPage]] as const) {
    page.on('pageerror', error => errors.push(`${name}: ${error.message}`))
    // Las respuestas 4xx/5xx se registran con su URL; el mensaje de consola equivalente no la incluye.
    page.on('console', message => { if (message.type() === 'error' && !message.text().startsWith('Failed to load resource')) errors.push(`${name}: ${message.text()}`) })
    page.on('response', response => { if (response.status() >= 400) errors.push(`${name}: ${response.status()} ${response.request().method()} ${response.url()}`) })
  }

  await test.step('torre: entra y ve a los 5 ajustadores en el mapa', async () => {
    await towerPage.goto(urls.tower)
    await towerPage.getByRole('button', { name: 'Entrar' }).click()
    await expect(towerPage.getByText('En vivo')).toBeVisible()
    await expect(towerPage.locator('.adjuster-row')).toHaveCount(5)
    await capture(towerPage, '01-torre-inicio')
  })

  await test.step('ajustador: entra a operación con el nombre de su unidad', async () => {
    await adjusterPage.goto(urls.adjuster)
    await adjusterPage.getByRole('button', { name: 'Entrar a operación' }).click()
    await expect(adjusterPage.locator('.topbar')).toContainText('Ajustador 01')
    await expect(adjusterPage.getByText('Estás disponible para recibir servicios.')).toBeVisible()
    await capture(adjusterPage, '02-ajustador-disponible')
  })

  const folio = await test.step('asegurado: reporta con ubicación y ambulancia y recibe folio', async () => {
    await insuredPage.goto(urls.insured)
    await insuredPage.getByRole('button', { name: 'Iniciar sesión' }).click()
    await insuredPage.getByLabel('Tipo de siniestro').selectOption('Collision')
    await insuredPage.locator('.location-card button').click()
    await expect(insuredPage.getByText('Ubicación confirmada')).toBeVisible()
    await insuredPage.getByText('Necesito ambulancia').click()
    await capture(insuredPage, '03-asegurado-formulario', true)
    await insuredPage.getByRole('button', { name: 'Solicitar ayuda ahora' }).click()
    await expect(insuredPage.getByText('Estamos contigo.')).toBeVisible()
    const text = await insuredPage.locator('.confirmation strong').first().textContent()
    await capture(insuredPage, '04-asegurado-folio')
    return text!.trim()
  })

  await test.step('asegurado: ve al ajustador asignado y la póliza vigente', async () => {
    await expect(insuredPage.getByText('Ajustador asignado')).toBeVisible({ timeout: 15_000 })
    await expect(insuredPage.getByText('Póliza: vigente')).toBeVisible()
    await expect(insuredPage.getByText(/Llegada estimada/)).toBeVisible()
    await capture(insuredPage, '05-asegurado-asignado')
  })

  await test.step('torre: ve el siniestro con su ajustador, ambulancia y timer de llegada', async () => {
    const row = towerPage.locator('.claim-row', { hasText: folio })
    await expect(row).toContainText('Ajustador 01')
    await expect(row).toContainText('Ambulancia solicitada')
    await expect(row).toContainText(/1[45] min para llegada/)
    await row.click()
    await expect(towerPage.getByRole('tooltip', { name: new RegExp(`^${folio} Ajustador 01`) })).toBeVisible()
    await expect(towerPage.getByRole('tooltip', { name: `Ajustador 01 En ruta a ${folio}` })).toBeVisible()
    await towerPage.waitForTimeout(1000) // termina la animación del mapa
    await capture(towerPage, '06-torre-siniestro-asignado')
  })

  await test.step('ajustador: recibe el aviso y registra llegada', async () => {
    const card = adjusterPage.locator('.service-card', { hasText: folio })
    await expect(card).toBeVisible()
    await expect(card).toContainText(/min para llegar/)
    await capture(adjusterPage, '07-ajustador-asignacion', true)
    await card.getByRole('button', { name: 'Registrar llegada' }).click()
    await expect(card).toContainText('EN SITIO')
  })

  await test.step('ajustador: sube una evidencia', async () => {
    const card = adjusterPage.locator('.service-card', { hasText: folio })
    await card.locator('input[type=file]').setInputFiles({ name: 'evidencia.png', mimeType: 'image/png', buffer: tinyPng })
    await expect(card).toContainText('1 evidencia(s): evidencia.png')
    await capture(adjusterPage, '08-ajustador-evidencia', true)
  })

  await test.step('ajustador: inicia y finaliza el servicio', async () => {
    const card = adjusterPage.locator('.service-card', { hasText: folio })
    await card.getByRole('button', { name: 'Iniciar atención' }).click()
    await expect(card).toContainText('ATENDIENDO')
    await card.getByRole('button', { name: 'Finalizar servicio' }).click()
    await expect(adjusterPage.getByText('Todo al día')).toBeVisible()
    await capture(adjusterPage, '09-ajustador-cerrado', true)
  })

  await test.step('asegurado y torre: ven el servicio concluido', async () => {
    await expect(insuredPage.getByText('Servicio concluido')).toBeVisible()
    await capture(insuredPage, '10-asegurado-concluido')
    await expect(towerPage.locator('.claim-row', { hasText: folio })).toHaveCount(0)
    await towerPage.getByRole('button', { name: 'Restablecer vista' }).click()
    await towerPage.waitForTimeout(1000)
    await capture(towerPage, '11-torre-final')
  })

  await test.step('torre: las alertas se leen en español', async () => {
    await towerPage.getByRole('tab', { name: /Alertas/ }).click()
    await expect(towerPage.locator('.alert-row strong').filter({ hasText: /^(GpsStale|SlaBreached|SlaWarning|NoAdjusterAvailable)/ })).toHaveCount(0)
    await capture(towerPage, '12-torre-alertas')
  })

  expect(errors, 'errores de JavaScript en las apps').toEqual([])
})

// Record a labelled overview of historical synthetic screenshots, never a simulated live test.
import { createRequire } from 'node:module'
import { mkdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(path.join(root, 'tests/e2e/package.json'))
const { chromium } = require('@playwright/test')
const frames = [
  ['Report an incident', 'The customer supplies location and claim details.', 'assets/demo/report.png'],
  ['Receive a reference', 'Assistance begins without waiting for policy coverage.', 'assets/demo/reference.png'],
  ['Coordinate assignment', 'The tower follows the claim, units and arrival deadline.', 'assets/screenshots/control-tower.png'],
  ['Track assistance', 'The customer sees assignment and asynchronous coverage.', 'assets/screenshots/insured-app.png'],
  ['Provide field service', 'The adjuster records arrival against the assigned claim.', 'assets/screenshots/adjuster-app.png'],
  ['Collect evidence', 'Document metadata and object storage have separate responsibilities.', 'assets/demo/evidence.png'],
  ['Complete the journey', 'Terminal events update customer/tower views and release reservations.', 'assets/demo/completed.png'],
]
const output = path.join(root, 'assets/demo/siniestros360-walkthrough.webm')
const cache = path.join(root, 'infrastructure/.local/walkthrough')
await mkdir(cache, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: process.env.PW_CHANNEL || undefined })
try {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 }, recordVideo: { dir: cache, size: { width: 1280, height: 800 } } })
  const page = await context.newPage()
  for (const [index, [title, description, source]] of frames.entries()) {
    const png = await readFile(path.join(root, source))
    if (png.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') throw new Error(`Invalid PNG: ${source}`)
    await page.setContent(`<!doctype html><html lang="en"><meta charset="utf-8"><style>
      *{box-sizing:border-box}body{margin:0;background:#0b1727;color:#eef5fc;font:20px Arial,sans-serif}
      header{height:130px;padding:22px 32px}h1{font-size:29px;margin:8px 0}p{margin:8px 0;color:#b8cde1}
      .notice{font-size:14px;color:#73d6dc;letter-spacing:1px}main{height:610px;text-align:center;background:#14253a;padding:12px}
      img{height:100%;max-width:100%;object-fit:contain}footer{height:60px;padding:18px 32px;font-size:16px;color:#b8cde1}
      </style><header><div class="notice">SINIESTROS360 · HISTORICAL SYNTHETIC SCREENSHOTS · GUIDED OVERVIEW</div><h1>${index + 1}. ${title}</h1><p>${description}</p></header>
      <main><img alt="${title}" src="data:image/png;base64,${png.toString('base64')}"></main>
      <footer>One web console · two React/Capacitor apps · not a new test run or Azure deployment</footer></html>`)
    await page.getByRole('img').evaluate(image => image.decode())
    await page.waitForTimeout(4200)
    console.log(`Recorded ${index + 1}/${frames.length}: ${title}`)
  }
  const video = page.video()
  await context.close()
  await video.saveAs(output)
  await video.delete()
  console.log(`Saved ${path.relative(root, output)}`)
} finally {
  await browser.close()
}

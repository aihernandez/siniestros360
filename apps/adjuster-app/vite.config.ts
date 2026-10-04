import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const gatewayUrl = process.env.SINIESTROS360_GATEWAY_URL ?? 'http://127.0.0.1:5009'

export default defineConfig({ plugins: [react()], server: { proxy: { '/api': gatewayUrl, '/identity': gatewayUrl, '/hubs': { target: gatewayUrl, ws: true } } } })

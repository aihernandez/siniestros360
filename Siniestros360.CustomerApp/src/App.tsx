import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useEffect, useState } from 'react'
import { intentKey, settleIntent } from './intentKey'

type Position = { latitude: number; longitude: number }
type Claim = { id: string; folio: string; coverageStatus: string; status: string; assignedAdjusterId: string | null }
type ClaimView = { claimId: string; folio: string; status: string; coverageStatus: string; adjusterId: string | null; estimatedArrivalAt: string | null; slaStage: string | null; slaDueAt: string | null }
type LoginResponse = { accessToken: string; expiresAt: string }
const api = import.meta.env.VITE_GATEWAY_URL ?? ''
const statusLabel: Record<string, string> = { AssignmentPending: 'Buscando ajustador', Assigned: 'Ajustador en camino', AdjusterArrived: 'Ajustador en el lugar', InProgress: 'En atención', Closed: 'Servicio concluido', Cancelled: 'Cancelado' }
const Icon = ({ d }: { d: string }) => <svg className="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d={d} /></svg>
const pin = 'M12 21s6-5.2 6-11A6 6 0 0 0 6 10c0 5.8 6 11 6 11Zm0-8.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z'
const wait = (milliseconds: number) => new Promise(resolve => window.setTimeout(resolve, milliseconds))

export function App() {
  const [token, setToken] = useState(() => localStorage.getItem('s360.insured.token') ?? '')
  const [email, setEmail] = useState('insured.demo@demo.com')
  const [password, setPassword] = useState('Demo!2026')
  const [position, setPosition] = useState<Position | null>(null)
  const [policyNumber, setPolicyNumber] = useState('POL-DEMO-001')
  const [vehiclePlate, setVehiclePlate] = useState('ABC-123')
  const [incidentType, setIncidentType] = useState('Collision')
  const [ambulance, setAmbulance] = useState(false)
  const [claim, setClaim] = useState<Claim | null>(null)
  const [live, setLive] = useState<ClaimView | null>(null)
  const [message, setMessage] = useState('')

  // Seguimiento en vivo del siniestro propio. La vista de Operations se proyecta 1–2 s después del alta,
  // así que la suscripción se reintenta y mientras tanto se muestra la respuesta del registro.
  useEffect(() => {
    if (!token || !claim) return
    let active = true
    const connection = new HubConnectionBuilder().withUrl(`${api}/hubs/operations`, { accessTokenFactory: () => token }).configureLogging(LogLevel.Warning).withAutomaticReconnect().build()
    connection.on('claimUpdated', (view: ClaimView) => { if (view.claimId === claim.id) setLive(view) })
    const subscribe = async () => {
      for (let attempt = 0; attempt < 3 && active; attempt++) {
        try { await connection.invoke('SubscribeClaim', claim.id); return } catch { await wait(2000) }
      }
    }
    connection.onreconnected(() => void subscribe())
    void connection.start().then(subscribe).catch(() => undefined)
    return () => { active = false; void connection.stop() }
  }, [token, claim])

  const login = async () => {
    const response = await fetch(`${api}/api/v1/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password }) })
    if (!response.ok) return setMessage('No fue posible iniciar sesión.')
    const session = await response.json() as LoginResponse
    localStorage.setItem('s360.insured.token', session.accessToken)
    setToken(session.accessToken); setMessage('')
  }
  const locate = () => navigator.geolocation.getCurrentPosition(x => { setPosition({ latitude: x.coords.latitude, longitude: x.coords.longitude }); setMessage('Ubicación actualizada.') }, () => setMessage('No pudimos obtener tu ubicación. Revisa los permisos.'), { enableHighAccuracy: true, timeout: 10000 })
  const submit = async () => {
    if (!position || !policyNumber || !vehiclePlate) return setMessage('Completa los datos y confirma tu ubicación.')
    setMessage('Enviando solicitud…')
    const body = JSON.stringify({ policyNumber, vehiclePlate, incidentType, latitude: position.latitude, longitude: position.longitude, requiresAmbulance: ambulance })
    const intent = `report:${body}`
    const response = await fetch(`${api}/api/v1/claims`, { method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${token}`, 'Idempotency-Key': intentKey(intent) }, body }).catch(() => null)
    if (!response) return setMessage('Sin conexión. Vuelve a intentarlo: tu solicitud no se duplicará.')
    await settleIntent(intent, response)
    if (response.status === 401) { localStorage.removeItem('s360.insured.token'); setToken(''); return setMessage('Tu sesión expiró. Inicia sesión de nuevo.') }
    if (!response.ok) { const problem = await response.json().catch(() => ({})) as { title?: string }; return setMessage(problem.title ?? 'No pudimos registrar el siniestro. Intenta de nuevo.') }
    setLive(null); setClaim(await response.json()); setMessage('')
  }

  if (!token) return <main className="mobile-shell"><Header /><section className="form-panel"><p className="overline">ACCESO DEL ASEGURADO</p><h1>Bienvenido</h1><label>Correo<input value={email} onChange={e => setEmail(e.target.value)} /></label><label>Contraseña<input type="password" value={password} onChange={e => setPassword(e.target.value)} /></label>{message && <p className="feedback">{message}</p>}<button className="primary-button" onClick={() => void login()}>Iniciar sesión</button><small>Demo: insured.demo@demo.com / Demo!2026</small></section></main>
  if (claim) {
    const status = live?.status ?? claim.status
    const assigned = Boolean(live?.adjusterId ?? claim.assignedAdjusterId)
    const arrival = live?.slaStage === 'WaitingArrival' ? live.slaDueAt : live?.estimatedArrivalAt
    return <main className="mobile-shell"><Header /><section className="confirmation"><div className="success">✓</div><p className="overline">SOLICITUD RECIBIDA</p><h1>Estamos contigo.</h1><p>Tu folio es <strong>{claim.folio}</strong>. La atención ya fue notificada.</p><div className="status-card"><i /><div><strong>{assigned ? 'Ajustador asignado' : 'Buscando ajustador disponible'}</strong><small>{assigned && arrival && status === 'Assigned' ? `Llegada estimada: ${new Date(arrival).toLocaleTimeString()}` : assigned ? statusLabel[status] ?? status : 'Te avisaremos al confirmar.'}</small><small>Cobertura: {live?.coverageStatus ?? claim.coverageStatus}</small></div></div><button className="primary-button" onClick={() => { setClaim(null); setLive(null) }}>Reportar otro siniestro</button></section></main>
  }
  return <main className="mobile-shell"><Header /><section className="incident-intro"><p className="overline">ATENCIÓN 24/7</p><h1>Reporta tu siniestro con calma.</h1><p>Comparte lo esencial; nosotros coordinamos la ayuda.</p></section><section className="form-panel"><div className="section-heading"><span>01</span><div><h2>Identifica tu vehículo</h2><p>La validación de póliza ocurre en segundo plano.</p></div></div><label>Póliza<input value={policyNumber} onChange={e => setPolicyNumber(e.target.value)} placeholder="Número de póliza" /></label><label>Placas<input value={vehiclePlate} onChange={e => setVehiclePlate(e.target.value)} placeholder="Placas o identificador" /></label><label>Tipo de siniestro<input value={incidentType} onChange={e => setIncidentType(e.target.value)} /></label><div className="location-card"><div className="location-icon"><Icon d={pin} /></div><div><strong>{position ? 'Ubicación confirmada' : 'Comparte tu ubicación'}</strong><small>{position ? `${position.latitude.toFixed(5)}, ${position.longitude.toFixed(5)}` : 'Necesaria para enviar ayuda al lugar correcto.'}</small></div><button className="icon-button" onClick={locate}>›</button></div><label className="emergency-option"><input type="checkbox" checked={ambulance} onChange={e => setAmbulance(e.target.checked)} /><span><strong>Necesito ambulancia</strong><small>Notificaremos a la torre para gestionar ayuda.</small></span></label>{message && <p className="feedback">{message}</p>}<button className="primary-button emergency" onClick={() => void submit()}>Solicitar ayuda ahora</button><p className="legal-note">⌁ Tu información se usa sólo para coordinar la atención.</p></section></main>
}
function Header() { return <header className="topbar"><b className="brand-mark">S360</b><span>Asistencia</span><button className="help-button" onClick={() => { localStorage.removeItem('s360.insured.token'); location.reload() }}>Salir</button></header> }

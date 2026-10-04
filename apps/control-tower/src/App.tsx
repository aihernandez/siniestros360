import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import './markers.css'
import './labels.css'
import './map-controls.css'
import { divIcon } from 'leaflet'
import { MapContainer, Marker, Polyline, Popup, TileLayer, Tooltip, useMap } from 'react-leaflet'
import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { intentKey, settleIntent } from './intentKey'

type ClaimView = { claimId: string; folio: string; policyNumber: string; vehiclePlate: string; incidentType: string; latitude: number; longitude: number; requiresAmbulance: boolean; status: string; coverageStatus: string; coverageReason: string | null; adjusterId: string | null; adjusterName: string | null; distanceKm: number | null; assignedAt: string | null; estimatedArrivalAt: string | null; slaStage: string | null; slaDueAt: string | null; escalatedAt: string | null; reportedAt: string; updatedAt: string; documentCount: number }
type AdjusterView = { adjusterId: string; displayName: string; status: string; isAvailable: boolean; activeClaimId: string | null; latitude: number | null; longitude: number | null; speedKmh: number | null; heading: number | null; capturedAt: string | null; gpsStale: boolean }
type AlertView = { id: string; claimId: string | null; adjusterId: string | null; type: string; message: string; raisedAt: string; acknowledgedAt: string | null; acknowledgedBy: string | null }
type LoginResponse = { accessToken: string }
type PanelTab = 'operation' | 'alerts' | 'test'
type MapTarget = [number, number] | null

const api = import.meta.env.VITE_GATEWAY_URL ?? ''
const tokenKey = 's360.tower.token'
const initialMapCenter: [number, number] = [25.6866, -100.3161]
function MapViewport({ target, resetKey }: { target: MapTarget; resetKey: number }) { const map = useMap(); useEffect(() => { if (target) map.flyTo(target, 15, { duration: .65 }) }, [map, target]); useEffect(() => { if (resetKey) map.flyTo(initialMapCenter, 10, { duration: .65 }) }, [map, resetKey]); return null }
const arrivalCountdown = (arrivalDueAt: string, now: number) => {
  const remaining = Math.max(0, Math.ceil((new Date(arrivalDueAt).getTime() - now) / 60000))
  return remaining ? `${remaining} min para llegada` : 'SLA de llegada vencido'
}
// El SLA publica la fecha límite de cada etapa; la torre sólo la muestra mientras se espera la llegada.
const arrivalDueAt = (claim: ClaimView) => claim.slaStage === 'WaitingArrival' ? claim.slaDueAt : null
const markerIcon = (kind: 'claim' | 'adjuster' | 'ambulance') => divIcon({ className: `map-marker marker-${kind}`, html: `<span>${kind === 'claim' ? '✦' : kind === 'ambulance' ? '+' : '➜'}</span>`, iconSize: [36, 36], iconAnchor: [18, 18] })
const claimMarkerIcon = markerIcon('claim')
const ambulanceMarkerIcon = markerIcon('ambulance')
// Los estilos de marcadores y puntos de estado usan el código numérico de la versión original.
const statusCode: Record<string, number> = { Offline: 0, Available: 1, Assigned: 3, EnRoute: 4, Arrived: 5, InService: 6 }
const adjusterStatus: Record<string, string> = { Offline: 'Fuera de línea', Available: 'Disponible', Assigned: 'Asignado', EnRoute: 'En ruta', Arrived: 'En sitio', InService: 'Atendiendo' }
const claimStatus: Record<string, string> = { AssignmentPending: 'Despachando', Assigned: 'Asignado', AdjusterArrived: 'Ajustador llegó', InProgress: 'Atendiendo', Closed: 'Cerrado', Cancelled: 'Cancelado' }
const alertLabel: Record<string, string> = { GpsStale: 'GPS sin actualizar', NoAdjusterAvailable: 'Sin ajustador disponible', SlaWarning: 'Plazo por vencer', SlaBreached: 'Plazo vencido' }
const adjusterMarkerIcons = new Map<number, ReturnType<typeof markerIcon>>()
const getAdjusterMarkerIcon = (status: string) => {
  const code = statusCode[status] ?? 0
  const cached = adjusterMarkerIcons.get(code)
  if (cached) return cached
  const icon = divIcon({ className: `map-marker marker-adjuster marker-adjuster-${code}`, html: '<span>➜</span>', iconSize: [36, 36], iconAnchor: [18, 18] })
  adjusterMarkerIcons.set(code, icon)
  return icon
}
const upsert = <T,>(items: T[], item: T, key: (value: T) => string) => { const id = key(item); return items.some(x => key(x) === id) ? items.map(x => key(x) === id ? item : x) : [item, ...items] }

export function App() {
  const [token, setToken] = useState(() => localStorage.getItem(tokenKey) ?? '')
  const [email, setEmail] = useState('tower.demo@demo.com')
  const [password, setPassword] = useState('Demo!2026')
  const [message, setMessage] = useState('')
  const [adjusters, setAdjusters] = useState<AdjusterView[]>([])
  const [claims, setClaims] = useState<ClaimView[]>([])
  const [alerts, setAlerts] = useState<AlertView[]>([])
  const [connected, setConnected] = useState(false)
  const [activeTab, setActiveTab] = useState<PanelTab>('operation')
  const [clock, setClock] = useState(() => Date.now())
  const [mapTarget, setMapTarget] = useState<MapTarget>(null)
  const [resetMapKey, setResetMapKey] = useState(0)

  const logout = useCallback(() => { localStorage.removeItem(tokenKey); setToken('') }, [])
  const authorizedFetch = useCallback(async (path: string, init: RequestInit = {}) => {
    const response = await fetch(`${api}${path}`, { ...init, headers: { ...init.headers, authorization: `Bearer ${token}` } })
    if (response.status === 401) logout()
    return response
  }, [token, logout])

  useEffect(() => {
    const timer = window.setInterval(() => setClock(Date.now()), 15_000)
    return () => window.clearInterval(timer)
  }, [])
  // Cada cambio de los siniestros renueva el reloj: con el de la carga inicial, un siniestro recién asignado mostraba
  // un minuto de más ("16 min" para un plazo de 15).
  useEffect(() => setClock(Date.now()), [claims])

  useEffect(() => {
    if (!token) return
    let mounted = true
    // Carga inicial y tras reconectar; el resto llega por SignalR como vistas completas que se aplican por upsert.
    const refresh = async () => {
      const responses = await Promise.all(['claims', 'adjusters/map', 'alerts'].map(path => authorizedFetch(`/api/v1/operations/${path}`)))
      if (!mounted) return
      if (responses.some(x => !x.ok)) { setMessage('No se pudieron cargar las proyecciones operativas.'); return }
      const [claimsPayload, adjustersPayload, alertsPayload] = await Promise.all(responses.map(x => x.json()))
      setClaims(claimsPayload); setAdjusters(adjustersPayload); setAlerts(alertsPayload); setMessage('')
    }

    void refresh()
    const connection = new HubConnectionBuilder().withUrl(`${api}/hubs/operations`, { accessTokenFactory: () => token }).configureLogging(LogLevel.Warning).withAutomaticReconnect().build()
    connection.on('claimUpdated', (claim: ClaimView) => setClaims(previous => upsert(previous, claim, x => x.claimId)))
    connection.on('adjusterUpdated', (adjuster: AdjusterView) => setAdjusters(previous => upsert(previous, adjuster, x => x.adjusterId)))
    connection.on('alertRaised', (alert: AlertView) => setAlerts(previous => upsert(previous, alert, x => x.id)))
    connection.on('alertAcknowledged', (alert: AlertView) => setAlerts(previous => upsert(previous, alert, x => x.id)))
    connection.onreconnecting(() => setConnected(false))
    connection.onreconnected(() => { setConnected(true); void refresh() })
    connection.onclose(() => setConnected(false))
    // Recarga al conectar: lo ocurrido entre la carga inicial y la conexión no llegaría por SignalR.
    void connection.start().then(() => { if (mounted) { setConnected(true); void refresh() } }).catch(() => setConnected(false))
    return () => { mounted = false; void connection.stop() }
  }, [token, authorizedFetch])

  const visibleAdjusters = useMemo(() => adjusters.filter(item => item.status !== 'Offline'), [adjusters])
  const activeClaims = useMemo(() => claims.filter(claim => claim.status !== 'Closed' && claim.status !== 'Cancelled'), [claims])
  const availableCount = useMemo(() => visibleAdjusters.filter(adjuster => adjuster.isAvailable).length, [visibleAdjusters])
  const mapAdjusters = useMemo(() => visibleAdjusters.filter(item => item.latitude !== null && item.longitude !== null), [visibleAdjusters])
  const claimsByAdjuster = useMemo(() => new Map(activeClaims.filter(claim => claim.adjusterId).map(claim => [claim.adjusterId!, claim])), [activeClaims])
  const attendedAlerts = useMemo(() => alerts.filter(alert => alert.acknowledgedAt !== null), [alerts])
  const pendingAlerts = useMemo(() => alerts.filter(alert => alert.acknowledgedAt === null), [alerts])
  const adjusterLabel = (claim: ClaimView) => claim.adjusterName ?? claim.adjusterId?.slice(0, 8) ?? ''
  const folios = useMemo(() => new Map(claims.map(claim => [claim.claimId, claim.folio])), [claims])

  const acknowledge = async (alertId: string) => {
    const response = await authorizedFetch(`/api/v1/operations/alerts/${alertId}/acknowledge`, { method: 'POST' })
    if (response.ok) {
      const alert = await response.json() as AlertView
      setAlerts(previous => upsert(previous, alert, x => x.id))
    }
  }

  const login = async () => {
    const response = await fetch(`${api}/api/v1/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password }) })
    if (!response.ok) return setMessage('Credenciales inválidas.')
    const session = await response.json() as LoginResponse
    localStorage.setItem(tokenKey, session.accessToken); setToken(session.accessToken); setMessage('')
  }

  if (!token) return <main className="control-shell"><header className="control-header"><div className="brand"><h1>Siniestros360</h1><span>Torre de control</span></div></header><section className="operations-sidebar" style={{ margin: '4rem auto', maxWidth: 480 }}><form className="tab-content test-form" onSubmit={event => { event.preventDefault(); void login() }}><label>Correo<input value={email} onChange={e => setEmail(e.target.value)} /></label><label>Contraseña<input type="password" value={password} onChange={e => setPassword(e.target.value)} /></label><button className="submit-claim">Entrar</button>{message && <p className="form-help">{message}</p>}<small className="form-help">tower.demo@demo.com / Demo!2026</small></form></section></main>

  return <main className="control-shell">
    <header className="control-header">
      <div className="brand"><h1>Siniestros360</h1><span>Torre de control</span></div>
      <div className="header-metrics" aria-label="Resumen operativo">
        <Metric label="Ajustadores" value={visibleAdjusters.length} />
        <Metric label="Disponibles" value={availableCount} accent="ready" />
        <Metric label="Siniestros" value={activeClaims.length} accent="incident" />
        <Metric label="Alertas" value={pendingAlerts.length} accent={pendingAlerts.length ? 'alert' : 'ready'} />
      </div>
      <span className={connected ? 'connection on' : 'connection'}>{connected ? 'En vivo' : 'Reconectando'}</span>
      <div className="map-actions"><button onClick={logout}>Salir</button></div>
    </header>

    <section className="control-workspace">
      <section className="map-region" aria-label="Mapa operativo del área metropolitana de Monterrey">
        <div className="map-heading"><div><h2>Mapa operativo</h2><p>{message || 'Área Metropolitana de Monterrey · posiciones GPS en tiempo real'}</p></div><div className="map-actions"><button onClick={() => { setMapTarget(null); setResetMapKey(value => value + 1) }}>Restablecer vista</button><span>{mapAdjusters.length} unidades visibles</span></div></div>
        <MapContainer center={initialMapCenter} zoom={10} scrollWheelZoom className="operations-map">
          <MapViewport target={mapTarget} resetKey={resetMapKey} />
          <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
          {activeClaims.map(item => { const due = arrivalDueAt(item); return <Marker key={`claim-${item.claimId}`} position={[item.latitude, item.longitude]} icon={claimMarkerIcon}><Popup><strong>{item.folio}</strong><br />Siniestro activo<br /><small>{item.adjusterId ? `Asignado a ${adjusterLabel(item)}` : 'Buscando ajustador'}</small></Popup>{item.adjusterId ? <Tooltip permanent direction="top" offset={[0, -18]} className="live-map-label"><strong>{item.folio}</strong><span>{adjusterLabel(item)}{due ? ` · ${arrivalCountdown(due, clock)}` : ''}</span></Tooltip> : null}</Marker> })}
          {activeClaims.filter(item => item.requiresAmbulance).map(item => <Marker key={`ambulance-${item.claimId}`} position={[item.latitude + 0.0011, item.longitude + 0.0011]} icon={ambulanceMarkerIcon}><Popup><strong>Ambulancia solicitada</strong><br />{item.folio}</Popup></Marker>)}
          {mapAdjusters.map(item => { const claim = claimsByAdjuster.get(item.adjusterId); return <Marker key={item.adjusterId} position={[item.latitude!, item.longitude!]} icon={getAdjusterMarkerIcon(item.status)}><Popup><strong>{item.displayName}</strong><br /><small>{claim ? `En ruta a ${claim.folio}` : adjusterStatus[item.status] ?? item.status}</small><br /><small>{item.latitude!.toFixed(5)}, {item.longitude!.toFixed(5)}</small></Popup>{claim ? <Tooltip permanent direction="bottom" offset={[0, 18]} className="live-map-label adjuster-label"><strong>{item.displayName}</strong><span>En ruta a {claim.folio}</span></Tooltip> : null}</Marker> })}
          {mapAdjusters.map(item => { const claim = claimsByAdjuster.get(item.adjusterId); return claim ? <Polyline key={`route-${item.adjusterId}`} positions={[[item.latitude!, item.longitude!], [claim.latitude, claim.longitude]]} pathOptions={{ color: '#f5af32', weight: 4, opacity: 0.82, dashArray: '10 8' }} /> : null })}
        </MapContainer>
      </section>

      <aside className="operations-sidebar">
        <div className="sidebar-tabs" role="tablist" aria-label="Panel de torre">
          <button className={activeTab === 'operation' ? 'active' : ''} role="tab" aria-selected={activeTab === 'operation'} onClick={() => setActiveTab('operation')}>Operación</button>
          <button className={activeTab === 'alerts' ? 'active' : ''} role="tab" aria-selected={activeTab === 'alerts'} onClick={() => setActiveTab('alerts')}>Alertas <span>{pendingAlerts.length}</span></button>
          <button className={activeTab === 'test' ? 'active' : ''} role="tab" aria-selected={activeTab === 'test'} onClick={() => setActiveTab('test')}>Pruebas</button>
        </div>
        {activeTab === 'test' ? <TestClaimForm authorizedFetch={authorizedFetch} /> : <>
        {activeTab === 'operation' ? <div className="tab-content">
          <section className="sidebar-section"><div className="section-title"><h2>Siniestros activos</h2><span>{activeClaims.length}</span></div>{activeClaims.length === 0 ? <p className="empty-state">Sin siniestros activos.</p> : activeClaims.map(claim => { const due = arrivalDueAt(claim); return <article className="claim-row selectable-row" key={claim.claimId} onClick={() => setMapTarget([claim.latitude, claim.longitude])}><div><strong>{claim.folio}</strong><p>{claim.adjusterId ? adjusterLabel(claim) : 'Buscando ajustador'}{claim.requiresAmbulance ? ' · Ambulancia solicitada' : ''}</p>{due ? <small className="sla-timer">⏱ {arrivalCountdown(due, clock)}</small> : null}</div><span className="claim-state">{claimStatus[claim.status] ?? claim.status}</span></article> })}</section>
          <section className="sidebar-section adjusters-section"><div className="section-title"><h2>Ajustadores en operación</h2><span>{visibleAdjusters.length}</span></div>{visibleAdjusters.map(adjuster => <article className="adjuster-row selectable-row" key={adjuster.adjusterId} onClick={() => setMapTarget(adjuster.latitude !== null && adjuster.longitude !== null ? [adjuster.latitude, adjuster.longitude] : null)}><span className={`status-dot status-${statusCode[adjuster.status] ?? 0}`} /><div><strong>{adjuster.displayName}</strong><p>{adjuster.capturedAt ? new Date(adjuster.capturedAt).toLocaleTimeString() : 'Sin señal'}</p></div><span>{adjusterStatus[adjuster.status] ?? adjuster.status}</span></article>)}</section>
        </div> : <div className="tab-content alerts-content"><section className="sidebar-section"><div className="section-title"><h2>Alertas pendientes</h2><span>{pendingAlerts.length}</span></div>{pendingAlerts.length === 0 ? <p className="empty-state">No hay alertas pendientes.</p> : pendingAlerts.map(alert => <AlertItem key={alert.id} alert={alert} folio={alert.claimId ? folios.get(alert.claimId) : undefined} onAcknowledge={acknowledge} />)}</section><section className="sidebar-section history-section"><div className="section-title"><h2>Atendidas</h2><span>{attendedAlerts.length}</span></div>{attendedAlerts.length === 0 ? <p className="empty-state">Las alertas atendidas aparecerán aquí.</p> : attendedAlerts.map(alert => <AlertItem key={alert.id} alert={alert} folio={alert.claimId ? folios.get(alert.claimId) : undefined} />)}</section></div>}
        </>}
      </aside>
    </section>
  </main>
}

function Metric({ label, value, accent }: { label: string; value: number; accent?: string }) { return <div className={`header-metric ${accent ?? ''}`}><span>{label}</span><strong>{value}</strong></div> }

function AlertItem({ alert, folio, onAcknowledge }: { alert: AlertView; folio?: string; onAcknowledge?: (alertId: string) => void }) {
  return <article className={alert.acknowledgedAt ? 'alert-row attended' : 'alert-row'}><div><strong>{alertLabel[alert.type] ?? alert.type}{folio ? ` · ${folio}` : ''}</strong><p>{alert.message}</p><small>{new Date(alert.raisedAt).toLocaleTimeString()}</small></div>{onAcknowledge ? <button onClick={() => void onAcknowledge(alert.id)}>Atender</button> : <span className="attended-label">Atendida</span>}</article>
}

function TestClaimForm({ authorizedFetch }: { authorizedFetch: (path: string, init?: RequestInit) => Promise<Response> }) {
  const [policyNumber, setPolicyNumber] = useState('POL-PRUEBA-001')
  const [vehiclePlate, setVehiclePlate] = useState('AUTO-001')
  const [latitude, setLatitude] = useState('25.6866')
  const [longitude, setLongitude] = useState('-100.3161')
  const [requiresAmbulance, setRequiresAmbulance] = useState(false)
  const [status, setStatus] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setSubmitting(true)
    setStatus(null)
    const body = JSON.stringify({ policyNumber, vehiclePlate, incidentType: 'Collision', latitude: Number(latitude), longitude: Number(longitude), requiresAmbulance })
    const intent = `test-claim:${body}`
    const response = await authorizedFetch('/api/v1/claims', {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'Idempotency-Key': intentKey(intent) },
      body,
    }).catch(() => null)
    setSubmitting(false)
    if (!response) { setStatus('Sin conexión. Reintenta: el siniestro no se duplicará.'); return }
    await settleIntent(intent, response)
    if (!response.ok) { const problem = await response.json().catch(() => ({})) as { title?: string }; setStatus(problem.title ?? 'No se pudo crear el siniestro. Revisa los datos de ubicación.'); return }
    const claim = await response.json() as { folio: string }
    setStatus(`Siniestro ${claim.folio} creado y enviado a despacho.`)
  }

  return <form className="tab-content test-form" onSubmit={event => void submit(event)}>
    <section className="sidebar-section"><div className="section-title"><h2>Crear siniestro de prueba</h2></div><p className="form-help">Crea una solicitud real de prueba para validar el despacho automático.</p>
      <label>Póliza<input value={policyNumber} onChange={event => setPolicyNumber(event.target.value)} required minLength={3} /></label>
      <label>Vehículo<input value={vehiclePlate} onChange={event => setVehiclePlate(event.target.value)} required minLength={3} maxLength={20} /></label>
      <div className="coordinate-fields"><label>Latitud<input type="number" step="any" min="-90" max="90" value={latitude} onChange={event => setLatitude(event.target.value)} required /></label><label>Longitud<input type="number" step="any" min="-180" max="180" value={longitude} onChange={event => setLongitude(event.target.value)} required /></label></div>
      <label className="checkbox-label"><input type="checkbox" checked={requiresAmbulance} onChange={event => setRequiresAmbulance(event.target.checked)} /> Solicitar ambulancia</label>
      <button className="submit-claim" disabled={submitting}>{submitting ? 'Enviando…' : 'Crear y despachar'}</button>
      {status ? <p className="form-status">{status}</p> : null}
    </section>
  </form>
}

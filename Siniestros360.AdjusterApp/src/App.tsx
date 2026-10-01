import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useCallback, useEffect, useRef, useState } from 'react'
import { intentKey, settleIntent } from './intentKey'

type Claim = { id: string; folio: string; policyNumber: string; vehiclePlate: string; status: string; assignedAdjusterId: string | null }
type ClaimView = { claimId: string; folio: string; status: string; adjusterId: string | null; slaStage: string | null; slaDueAt: string | null }
type LoginResponse = { accessToken: string }
type Profile = { adjusterId?: string }
const api = import.meta.env.VITE_GATEWAY_URL ?? ''
const statusLabel: Record<string, string> = { Assigned: 'ASIGNADO', AdjusterArrived: 'EN SITIO', InProgress: 'ATENDIENDO' }
const remaining = (due: string, now = Date.now()) => `${Math.max(0, Math.ceil((new Date(due).getTime() - now) / 60000))} min para llegar`
// Plazo de llegada publicado por el SLA; sólo aplica mientras el siniestro espera al ajustador.
const arrivalDue = (view: ClaimView) => view.slaStage === 'WaitingArrival' ? view.slaDueAt : null

export function App() {
  const [token, setToken] = useState(() => localStorage.getItem('s360.adjuster.token') ?? '')
  const [adjusterId, setAdjusterId] = useState(() => localStorage.getItem('s360.adjuster.id') ?? '')
  const [email, setEmail] = useState('adjuster.demo@demo.com')
  const [password, setPassword] = useState('Demo!2026')
  const [items, setItems] = useState<Claim[]>([])
  const [deadlines, setDeadlines] = useState<Record<string, string>>({})
  const [notice, setNotice] = useState<{ title?: string; text: string } | null>(null)
  const [clock, setClock] = useState(Date.now())
  const known = useRef(new Set<string>())

  const logout = useCallback(() => { localStorage.removeItem('s360.adjuster.token'); localStorage.removeItem('s360.adjuster.id'); setToken(''); setAdjusterId('') }, [])
  const refresh = useCallback(async (accessToken = token) => {
    if (!accessToken) return
    const response = await fetch(`${api}/api/v1/claims`, { headers: { authorization: `Bearer ${accessToken}` } })
    if (response.status === 401) return logout()
    if (!response.ok) return
    const claims = await response.json() as Claim[]
    claims.forEach(x => known.current.add(x.id))
    setItems(claims)
    // La fecha límite del SLA vive en la vista de Operations; Claims no la conoce.
    const views = await Promise.all(claims.filter(x => x.status === 'Assigned').map(async x => {
      const view = await fetch(`${api}/api/v1/operations/claims/${x.id}`, { headers: { authorization: `Bearer ${accessToken}` } }).catch(() => null)
      return view?.ok ? await view.json() as ClaimView : null
    }))
    setDeadlines(previous => { const next = { ...previous }; for (const view of views) { const due = view && arrivalDue(view); if (view && due) next[view.claimId] = due } return next })
  }, [token, logout])

  useEffect(() => {
    if (!token) return
    void refresh()
    const timer = window.setInterval(() => setClock(Date.now()), 60_000)
    const connection = new HubConnectionBuilder().withUrl(`${api}/hubs/operations`, { accessTokenFactory: () => token }).configureLogging(LogLevel.Warning).withAutomaticReconnect().build()
    connection.on('claimUpdated', (view: ClaimView) => {
      const due = arrivalDue(view)
      if (due) setDeadlines(previous => ({ ...previous, [view.claimId]: due }))
      if (view.adjusterId === adjusterId && view.status === 'Assigned' && !known.current.has(view.claimId)) {
        known.current.add(view.claimId)
        setNotice({ title: 'Nueva asignación', text: due ? `Nuevo servicio ${view.folio}. Tienes ${remaining(due)}.` : `Nuevo servicio ${view.folio}.` })
      }
      void refresh()
    })
    connection.onreconnected(() => void refresh())
    void connection.start().catch(() => setNotice({ text: 'Reconectando tiempo real…' }))
    return () => { window.clearInterval(timer); void connection.stop() }
  }, [token, adjusterId, refresh])

  const login = async () => {
    const response = await fetch(`${api}/api/v1/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password }) })
    if (!response.ok) return setNotice({ text: 'No fue posible iniciar sesión. Revisa tu correo y contraseña.' })
    const session = await response.json() as LoginResponse
    const profileResponse = await fetch(`${api}/api/v1/auth/me`, { headers: { authorization: `Bearer ${session.accessToken}` } })
    if (!profileResponse.ok) return setNotice({ text: 'La sesión no pudo verificarse.' })
    const profile = await profileResponse.json() as Profile
    if (!profile.adjusterId) return setNotice({ text: 'Esta cuenta no está asociada a un ajustador.' })
    localStorage.setItem('s360.adjuster.token', session.accessToken); localStorage.setItem('s360.adjuster.id', profile.adjusterId)
    setNotice(null); setAdjusterId(profile.adjusterId); setToken(session.accessToken)
  }
  const transition = async (id: string, action: 'adjuster-arrived' | 'service-started' | 'service-completed') => {
    const intent = `${action}:${id}`
    const response = await fetch(`${api}/api/v1/claims/${id}/${action}`, { method: 'POST', headers: { authorization: `Bearer ${token}`, 'Idempotency-Key': intentKey(intent) } }).catch(() => null)
    if (!response) return setNotice({ text: 'Sin conexión. Vuelve a intentarlo: el registro no se duplicará.' })
    await settleIntent(intent, response)
    if (response.status === 401) return logout()
    const problem = response.ok ? null : await response.json().catch(() => ({})) as { title?: string; detail?: string }
    setNotice({ text: response.ok ? 'Estado del servicio actualizado.' : problem?.detail ?? problem?.title ?? 'No fue posible actualizar el servicio.' }); await refresh()
  }

  if (!token) return <main className="mobile-shell adjuster"><header className="topbar"><b className="brand-mark">S360</b><span>Operación en campo</span></header><section className="form-panel"><p className="overline">ACCESO SEGURO</p><h1>Inicia tu jornada</h1><p>Recibirás en vivo los servicios asignados a tu cuenta.</p><label>Correo<input value={email} onChange={e => setEmail(e.target.value)} /></label><label>Contraseña<input type="password" value={password} onChange={e => setPassword(e.target.value)} /></label>{notice && <p className="feedback">{notice.text}</p>}<button className="primary-button" onClick={() => void login()}>Entrar a operación</button><small>Demo: adjuster.demo@demo.com / Demo!2026</small></section></main>
  const active = items.filter(x => x.status !== 'Closed' && x.status !== 'Cancelled')
  return <main className="mobile-shell adjuster"><header className="topbar"><b className="brand-mark">S360</b><span>{adjusterId.slice(0, 8)} · Operación</span><button onClick={logout}>Salir</button></header>{notice && <aside className="assignment-notice" role="alert">{notice.title && <strong>{notice.title}</strong>}<span>{notice.text}</span></aside>}<section className="shift-summary"><div><p className="overline">MI JORNADA</p><h1>Servicios en vivo</h1><p>{active.length ? `${active.length} servicio(s) requieren tu atención.` : 'Estás disponible para recibir servicios.'}</p></div><span className="availability"><i />Conectado</span></section><section className="service-list">{active.map(item => { const [label, action] = item.status === 'Assigned' ? ['Registrar llegada', 'adjuster-arrived'] as const : item.status === 'AdjusterArrived' ? ['Iniciar atención', 'service-started'] as const : ['Finalizar servicio', 'service-completed'] as const; const due = item.status === 'Assigned' ? deadlines[item.id] : undefined; return <article className="service-card" key={item.id}><div className="service-top"><strong>{item.folio}</strong><span>{statusLabel[item.status] ?? item.status}</span></div><p>Póliza {item.policyNumber} · {item.vehiclePlate}</p>{due && <small>⏱ {remaining(due, clock)}</small>}<button className="primary-button" onClick={() => void transition(item.id, action)}>{label}</button></article> })}{!active.length && <div className="empty-state"><h2>Todo al día</h2><p>Cuando recibas una asignación aparecerá aquí.</p></div>}</section></main>
}

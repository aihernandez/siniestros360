// Idempotency-Key por intención, no por envío: la llave nace con la intención (acción + datos) y se reutiliza en
// cualquier reintento de esa misma intención —falla de red, 5xx o 409 "in progress"—, así el servidor nunca la
// ejecuta dos veces. Se descarta sólo con una respuesta definitiva (2xx, o 4xx distinto de 409 en curso).
// Cambiar los datos cambia la intención y por lo tanto la llave.
const keys = new Map<string, string>()

export const intentKey = (intent: string) => {
  let key = keys.get(intent)
  if (!key) {
    key = crypto.randomUUID()
    keys.set(intent, key)
  }
  return key
}

// Se llama con la respuesta recibida; un error de red (sin respuesta) conserva la llave para el reintento.
export const settleIntent = async (intent: string, response: Response) => {
  if (response.status >= 500) return
  if (response.status === 409) {
    const problem = await response.clone().json().catch(() => ({})) as { title?: string }
    if (problem.title?.toLowerCase().includes('in progress')) return
  }
  keys.delete(intent)
}

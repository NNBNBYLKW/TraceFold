import './style.css'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || 'http://127.0.0.1:8000'

const app = document.querySelector<HTMLDivElement>('#app')

if (!app) {
  throw new Error('#app not found')
}

app.innerHTML = `
  <main style="padding: 24px; font-family: sans-serif;">
    <h1>Step 1 Web Ready</h1>
    <p>API: <span id="api-base-url">${apiBaseUrl}</span></p>
    <p>Health: <span id="health-status">Checking...</span></p>
  </main>
`

const healthStatus = document.querySelector<HTMLSpanElement>('#health-status')

async function checkHealth() {
  if (!healthStatus) return

  try {
    const res = await fetch(`${apiBaseUrl}/health`)
    if (!res.ok) {
      throw new Error(`HTTP ${res.status}`)
    }

    await res.text()
    healthStatus.textContent = 'OK'
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Unknown error'
    healthStatus.textContent = `Failed: ${message}`
  }
}

checkHealth()
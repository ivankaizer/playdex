// Idempotent Cloudflare setup for the landing, run by .github/workflows/landing.yml. Checks before it writes.
//   bun landing/scripts/cloudflare.mjs project    the Pages project exists (production branch: main)
// Domains are set up once, outside CI: playdex.dev is a custom domain of the project, and www.playdex.dev
// points at the Caddy edge, which 301s to the apex. The app is dash.playdex.dev (Dokploy behind the edge).
// Env: CLOUDFLARE_API_TOKEN (Account · Cloudflare Pages: Edit), CLOUDFLARE_ACCOUNT_ID.
const PROJECT = 'playdex-landing'

const { CLOUDFLARE_API_TOKEN: token, CLOUDFLARE_ACCOUNT_ID: account } = process.env
if (!token || !account) throw new Error('CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID must be set')

async function cf(method, path, body, { allow404 = false } = {}) {
  const res = await fetch(`https://api.cloudflare.com/client/v4${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
  })
  if (allow404 && res.status === 404) return null
  const json = await res.json().catch(() => ({}))
  if (!res.ok || json.success === false) throw new Error(`${method} ${path}: ${res.status} ${JSON.stringify(json.errors ?? json)}`)
  return json.result
}

async function project() {
  const base = `/accounts/${account}/pages/projects`
  if (await cf('GET', `${base}/${PROJECT}`, null, { allow404: true })) return console.log(`project ${PROJECT}: exists`)
  await cf('POST', base, { name: PROJECT, production_branch: 'main' })
  console.log(`project ${PROJECT}: created`)
}

const step = { project }[process.argv[2]]
if (!step) throw new Error('usage: cloudflare.mjs project')
await step()

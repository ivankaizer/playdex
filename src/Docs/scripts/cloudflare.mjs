// Idempotent Cloudflare setup for the docs, run by .github/workflows/docs.yml. Checks before it writes.
//   bun scripts/cloudflare.mjs project    the Pages project exists (production branch: main)
//   bun scripts/cloudflare.mjs domain     docs.playdex.dev is a custom domain of the project
// The domain step runs once, outside CI. Pages validates the domain through a proxied DNS record
// docs → playdex-docs.pages.dev in the playdex.dev zone.
// Env: CLOUDFLARE_API_TOKEN (Account · Cloudflare Pages: Edit), CLOUDFLARE_ACCOUNT_ID.
const PROJECT = 'playdex-docs'
const DOMAIN = 'docs.playdex.dev'

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

const base = `/accounts/${account}/pages/projects`

async function project() {
  if (await cf('GET', `${base}/${PROJECT}`, null, { allow404: true })) return console.log(`project ${PROJECT}: exists`)
  await cf('POST', base, { name: PROJECT, production_branch: 'main' })
  console.log(`project ${PROJECT}: created`)
}

async function domain() {
  const existing = await cf('GET', `${base}/${PROJECT}/domains/${DOMAIN}`, null, { allow404: true })
  if (existing) return console.log(`domain ${DOMAIN}: ${existing.status}`)
  const created = await cf('POST', `${base}/${PROJECT}/domains`, { name: DOMAIN })
  console.log(`domain ${DOMAIN}: added (${created.status})`)
}

const step = { project, domain }[process.argv[2]]
if (!step) throw new Error('usage: cloudflare.mjs project|domain')
await step()

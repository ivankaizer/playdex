// The public download feed for the Windows apps: download.playdex.dev, a Cloudflare R2 bucket with a custom domain.
// The GitHub repository is private, so installs, updates and the landing's download button read this instead.
//
//   bun tools/downloads/publish.mjs setup                  once, outside CI: the bucket and download.playdex.dev
//   bun tools/downloads/publish.mjs release <tag> <dir>    from release-windows.yaml, after the release is built
//
// Layout (what ReleaseFeed in src/Playdex.Core/AppUpdate.cs reads):
//   windows/<tag>/<asset>               every release asset, immutable
//   windows/releases/<tag>.json         the release in GitHub's release JSON shape, assets pointing at the files above
//   windows/releases/latest.json        the same for the highest released version
//   windows/Playdex-Setup.exe           the latest setup at a stable URL (the landing links here)
// Files go up first and latest.json last, so a client never sees a release whose files aren't there yet.
//
// Env: CLOUDFLARE_API_TOKEN (Account · Workers R2 Storage: Edit; for setup also Zone · Zone: Read on playdex.dev),
// CLOUDFLARE_ACCOUNT_ID.
import { existsSync, statSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

const BUCKET = 'playdex-downloads'
const DOMAIN = 'download.playdex.dev'
const ZONE = 'playdex.dev'
const BASE = `https://${DOMAIN}/windows`
const WRANGLER = 'wrangler@4.135.0'
const IMMUTABLE = 'public, max-age=31536000, immutable'
const SHORT = 'public, max-age=300'

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

async function bucket() {
  const base = `/accounts/${account}/r2/buckets`
  if (await cf('GET', `${base}/${BUCKET}`, null, { allow404: true })) return console.log(`bucket ${BUCKET}: exists`)
  await cf('POST', base, { name: BUCKET })
  console.log(`bucket ${BUCKET}: created`)
}

async function setup() {
  await bucket()
  const path = `/accounts/${account}/r2/buckets/${BUCKET}/domains/custom`
  const { domains = [] } = (await cf('GET', path)) ?? {}
  if (domains.some((d) => d.domain === DOMAIN)) return console.log(`domain ${DOMAIN}: attached`)
  const [zone] = await cf('GET', `/zones?name=${ZONE}`)
  if (!zone) throw new Error(`zone ${ZONE} not found`)
  await cf('POST', path, { domain: DOMAIN, zoneId: zone.id, enabled: true, minTLS: '1.2' })
  console.log(`domain ${DOMAIN}: attached (the certificate can take a few minutes)`)
}

const contentTypes = {
  '.exe': 'application/vnd.microsoft.portable-executable',
  '.zip': 'application/zip',
  '.sha256': 'text/plain; charset=utf-8',
  '.msix': 'application/msix',
  '.cer': 'application/pkix-cert',
  '.json': 'application/json',
}
const contentType = (name) => contentTypes[name.slice(name.lastIndexOf('.'))] ?? 'application/octet-stream'

function put(key, file, cacheControl) {
  const args = ['x', WRANGLER, 'r2', 'object', 'put', `${BUCKET}/${key}`, '--remote', `--file=${file}`,
    `--content-type=${contentType(key)}`, `--cache-control=${cacheControl}`]
  const run = Bun.spawnSync(['bun', ...args], { stdout: 'inherit', stderr: 'inherit' })
  if (run.exitCode !== 0) throw new Error(`upload of ${key} failed`)
}

const parseTag = (tag) => {
  const match = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(tag ?? '')
  return match ? match.slice(1).map(Number) : null
}
const newer = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2]

async function release(tag, dir) {
  const version = parseTag(tag)
  if (!version || !dir) throw new Error('usage: publish.mjs release v<major>.<minor>.<patch> <artifacts dir>')
  const number = tag.slice(1)
  const required = [`playdex-windows-${tag}.zip`, `playdex-windows-${tag}.zip.sha256`, 'Playdex-Setup.exe',
    `playdex-wrapper-windows-${tag}.zip`, `playdex-wrapper-windows-${tag}.zip.sha256`]
  const optional = [`playdex-launcher-v${number}.msix`, 'playdex-launcher.cer']
  for (const name of required) if (!existsSync(join(dir, name))) throw new Error(`${dir} has no ${name}`)
  const files = [...required, ...optional.filter((name) => existsSync(join(dir, name)))]

  await bucket()
  for (const name of files) put(`windows/${tag}/${name}`, join(dir, name), IMMUTABLE)

  const manifest = join(tmpdir(), `playdex-release-${tag}.json`)
  writeFileSync(manifest, JSON.stringify({
    tag_name: tag,
    draft: false,
    prerelease: false,
    assets: files.map((name) => ({
      name,
      browser_download_url: `${BASE}/${tag}/${name}`,
      size: statSync(join(dir, name)).size,
    })),
  }, null, 2))
  put(`windows/releases/${tag}.json`, manifest, SHORT)

  // A re-run of an older tag must not move "latest" backwards.
  const current = await fetch(`${BASE}/releases/latest.json`).then((r) => (r.ok ? r.json() : null)).catch(() => null)
  const currentVersion = parseTag(current?.tag_name)
  if (currentVersion && newer(currentVersion, version) > 0) {
    return console.log(`latest stays ${current.tag_name}; published ${tag} at ${BASE}/releases/${tag}.json`)
  }
  put('windows/Playdex-Setup.exe', join(dir, 'Playdex-Setup.exe'), SHORT)
  put('windows/releases/latest.json', manifest, SHORT)
  console.log(`published ${tag}: ${BASE}/Playdex-Setup.exe, ${BASE}/releases/latest.json`)
}

const [command, ...args] = process.argv.slice(2)
const step = { setup, release }[command]
if (!step) throw new Error('usage: publish.mjs setup | release <tag> <dir>')
await step(...args)

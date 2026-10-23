// Fails when a built page has inline code its Content-Security-Policy doesn't allow, or when the policy
// still lists a hash that no longer matches anything. The CSP allows inline <script> and <style> blocks
// only by sha256 hash, so any edit to the theme script or the boot-loader style in index.html must update
// the hash in the policy, or the browser blocks it. Run after the build: `bun run check:csp`.
//
// Each target pairs a built page with the file its policy is served from. A page that wasn't built is
// skipped (landing.yml builds only the landing), but at least one must exist.
import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));

const targets = [
  { page: "dist/index.html", policyFile: "security-headers.conf" },
  { page: "landing/dist/index.html", policyFile: "landing/public/_headers" },
];

/** The CSP from an nginx snippet (`add_header Content-Security-Policy "..."`) or a Cloudflare `_headers` file. */
function readPolicy(file) {
  const text = readFileSync(root + file, "utf8");
  const match =
    /add_header\s+Content-Security-Policy(?:-Report-Only)?\s+"([^"]+)"/.exec(text) ??
    /^\s*Content-Security-Policy(?:-Report-Only)?:\s*(.+)$/m.exec(text);
  if (!match) throw new Error(`${file}: no Content-Security-Policy found`);
  const directives = new Map();
  for (const part of match[1].split(";")) {
    const [name, ...sources] = part.trim().split(/\s+/);
    if (name) directives.set(name.toLowerCase(), sources);
  }
  return directives;
}

const sha256 = (text) => `'sha256-${createHash("sha256").update(text, "utf8").digest("base64")}'`;

function inlineBlocks(html, tag) {
  const pattern = new RegExp(`<${tag}\\b([^>]*)>([\\s\\S]*?)</${tag}>`, "gi");
  return [...html.matchAll(pattern)]
    .filter(([, attributes]) => !/\bsrc\s*=/i.test(attributes))
    .map(([, , body]) => body)
    .filter((body) => body.trim() !== "");
}

const failures = [];
const built = targets.filter(({ page }) => existsSync(root + page));
if (built.length === 0) failures.push(`none of ${targets.map(({ page }) => page).join(", ")} exists: build first.`);

for (const { page, policyFile } of built) {
  const html = readFileSync(root + page, "utf8");
  const policy = readPolicy(policyFile);

  // Inline event handlers and style attributes can't be allowed by a hash without 'unsafe-hashes'.
  if (/<[a-z][^>]*\son[a-z]+\s*=/i.test(html)) failures.push(`${page}: inline event handler attribute`);
  if (/<[a-z][^>]*\sstyle\s*=/i.test(html)) failures.push(`${page}: inline style attribute`);

  for (const [tag, directive] of [
    ["script", "script-src"],
    ["style", "style-src"],
  ]) {
    const sources = policy.get(directive) ?? policy.get("default-src") ?? [];
    const expected = inlineBlocks(html, tag).map(sha256);
    const listed = sources.filter((source) => source.startsWith("'sha256-"));
    const missing = expected.filter((hash) => !sources.includes(hash));
    const stale = listed.filter((hash) => !expected.includes(hash));
    for (const hash of missing) failures.push(`${policyFile}: ${directive} lacks ${hash} for an inline <${tag}> in ${page}`);
    for (const hash of stale) failures.push(`${policyFile}: ${directive} lists ${hash}, which matches no inline <${tag}> in ${page}`);
  }
}

if (failures.length > 0) {
  console.error("CSP check failed:\n" + failures.map((failure) => `  - ${failure}`).join("\n"));
  process.exit(1);
}
console.log(`CSP check passed for ${built.map(({ page }) => page).join(", ")}.`);

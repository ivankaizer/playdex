import * as fs from "fs";
import * as path from "path";

const __dirname = path.dirname(new URL(import.meta.url).pathname);
const ROOT = path.resolve(__dirname, "..");
const OUTPUT_DIR = path.join(ROOT, "bruno-collection");

const routes = [
  // ===== AUTH =====
  { method: "POST", path: "/api/login", resource: "auth", name: "Login", auth: false, body: { email: "user@example.com", password: "password123" }, description: "Authenticate user and receive JWT tokens. On success, the token is saved to authToken env variable.", postScript: 'if (res.body.token) {\n  bru.setEnvVar("authToken", res.body.token);\n}\nif (res.body.refreshToken) {\n  bru.setEnvVar("refreshToken", res.body.refreshToken);\n}' },
  { method: "POST", path: "/api/refresh", resource: "auth", name: "Refresh Token", auth: false, body: { refreshToken: "{{refreshToken}}" }, description: "Refresh an expired JWT access token using a valid refresh token." },
  { method: "GET", path: "/api/me", resource: "auth", name: "Get Current User", auth: true, description: "Retrieve the currently authenticated user's profile." },

  // ===== GAMES =====
  { method: "GET", path: "/api/games/search", resource: "games", name: "Search Games", auth: true, query: [{ name: "query", value: "zelda" }, { name: "limit", value: "10" }], description: "Search for games by title." },
  { method: "GET", path: "/api/games/{{gameId}}", resource: "games", name: "Get Game", auth: true, description: "Retrieve a single game by its ID." },
  { method: "GET", path: "/api/games/connections", resource: "games", name: "List Connections", auth: true, description: "List all game connections for the current user." },
  { method: "POST", path: "/api/games/connections", resource: "games", name: "Create Connection", auth: true, body: { gameId: "string", connectedGameId: "string" }, description: "Create a connection between two games." },
  { method: "DELETE", path: "/api/games/connections", resource: "games", name: "Delete Connection", auth: true, body: { gameId: "string", connectedGameId: "string" }, description: "Remove a connection between two games." },
  { method: "GET", path: "/api/games/ignored", resource: "games", name: "List Ignored Games", auth: true, query: [{ name: "gameId", value: "string?" }], description: "List ignored games, optionally filtered by gameId." },
  { method: "POST", path: "/api/games/ignored", resource: "games", name: "Ignore Game", auth: true, body: { gameId: "string" }, description: "Add a game to the ignored list." },
  { method: "DELETE", path: "/api/games/ignored", resource: "games", name: "Unignore Game", auth: true, body: { gameId: "string" }, description: "Remove a game from the ignored list." },
  { method: "GET", path: "/api/games/rating", resource: "games", name: "Get Ratings", auth: true, query: [{ name: "gameId", value: "string?" }], description: "Get ratings for games, optionally filtered by gameId." },
  { method: "POST", path: "/api/games/rating", resource: "games", name: "Rate Game", auth: true, body: { gameId: "string", rating: 5 }, description: "Set or update a rating for a game. Rating is an integer." },
  { method: "DELETE", path: "/api/games/rating", resource: "games", name: "Delete Rating", auth: true, body: { gameId: "string" }, description: "Remove a rating for a game." },

  // ===== ACCOUNTS =====
  { method: "GET", path: "/api/accounts", resource: "accounts", name: "List Accounts", auth: true, description: "List all connected gaming accounts for the current user." },
  { method: "GET", path: "/api/accounts/{{accountId}}/sessions", resource: "accounts", name: "List Account Sessions", auth: true, query: [{ name: "limit", value: "20" }, { name: "offset", value: "0" }, { name: "sort", value: "startedAt" }, { name: "direction", value: "desc" }, { name: "title", value: "string?" }, { name: "gameId", value: "string?" }, { name: "from", value: "string?" }, { name: "to", value: "string?" }], description: "List sessions for a specific account with pagination, sorting, and filtering." },
  { method: "GET", path: "/api/accounts/{{accountId}}/sessions/today", resource: "accounts", name: "Get Today Sessions", auth: true, description: "Get today's sessions for a specific account." },
  { method: "GET", path: "/api/accounts/{{accountId}}/sessions/current", resource: "accounts", name: "Get Current Session", auth: true, description: "Get the currently active session for a specific account." },
  { method: "GET", path: "/api/accounts/{{accountId}}/stats", resource: "accounts", name: "Get Account Stats", auth: true, description: "Get playtime statistics for a specific account." },

  // ===== SESSIONS =====
  { method: "GET", path: "/api/sessions", resource: "sessions", name: "List Sessions", auth: true, query: [{ name: "limit", value: "20" }, { name: "offset", value: "0" }, { name: "sort", value: "startedAt" }, { name: "direction", value: "desc" }, { name: "title", value: "string?" }, { name: "gameId", value: "string?" }, { name: "from", value: "string?" }, { name: "to", value: "string?" }], description: "List all sessions for the current user with pagination, sorting, and filtering." },
  { method: "GET", path: "/api/sessions/today", resource: "sessions", name: "Get Today Sessions", auth: true, description: "Get today's sessions for the current user." },
  { method: "GET", path: "/api/sessions/current", resource: "sessions", name: "Get Current Session", auth: true, description: "Get the currently active session for the current user." },
  { method: "DELETE", path: "/api/sessions/{{sessionId}}", resource: "sessions", name: "Delete Session", auth: true, description: "Delete a session by its ID." },

  // ===== STATS =====
  { method: "GET", path: "/api/stats", resource: "stats", name: "Get Stats", auth: true, query: [{ name: "from", value: "2024-01-01" }, { name: "to", value: "2024-12-31" }], description: "Get aggregate playtime statistics for a date range." },

  // ===== ADMIN =====
  { method: "GET", path: "/api/admin/sessions/recent", resource: "admin", name: "Recent Sessions", auth: true, description: "Admin: Get recent sessions across all users." },
  { method: "GET", path: "/api/admin/sessions", resource: "admin", name: "Search Sessions", auth: true, query: [{ name: "query", value: "string?" }, { name: "limit", value: "20" }, { name: "offset", value: "0" }], description: "Admin: Search sessions by query with pagination." },
  { method: "PUT", path: "/api/admin/sessions/move", resource: "admin", name: "Move Sessions", auth: true, body: { sessionIds: [1, 2], targetCredentialId: 1 }, description: "Admin: Move sessions to a different credential/account." },
  { method: "GET", path: "/api/admin/providers/platforms", resource: "admin", name: "Platform Statuses", auth: true, description: "Admin: Get platform statuses for all providers." },
  { method: "PUT", path: "/api/admin/providers/platforms/status", resource: "admin", name: "Update Platform Status", auth: true, body: { platform: "steam", enabled: true }, description: "Admin: Enable or disable a provider platform." },
  { method: "GET", path: "/api/admin/credentials", resource: "admin", name: "List Credentials", auth: true, description: "Admin: List all provider credentials." },
  { method: "PUT", path: "/api/admin/credentials/{{credentialId}}", resource: "admin", name: "Update Credential", auth: true, body: { apiKey: "string?", user: "string?" }, description: "Admin: Update a credential's API key or linked user." },
  { method: "GET", path: "/api/admin/games/search", resource: "admin", name: "Search Games", auth: true, query: [{ name: "query", value: "zelda" }, { name: "limit", value: "10" }], description: "Admin: Search games across the database." },
  { method: "PUT", path: "/api/admin/games", resource: "admin", name: "Update Game", auth: true, body: { gameId: "string", title: "string?", imageUrl: "string?", lockTitle: false, lockImage: false }, description: "Admin: Update a game by gameId in request body." },
  { method: "PUT", path: "/api/admin/games/{{gameId}}", resource: "admin", name: "Update Game by ID", auth: true, body: { title: "string?", imageUrl: "string?", lockTitle: false, lockImage: false }, description: "Admin: Update a game by its ID in the URL path." },

  // ===== PLAYNITE =====
  { method: "POST", path: "/api/playnite/sessions", resource: "playnite", name: "Create Session", auth: true, authType: "apikey", body: { gameTitle: "Elden Ring", gameId: "12345", gameProvider: "steam", durationInSeconds: 3600 }, description: "Create a completed play session via the Playnite extension. Uses API key authentication." },
  { method: "POST", path: "/api/playnite/sessions/start", resource: "playnite", name: "Start Session", auth: true, authType: "apikey", body: { gameTitle: "Elden Ring", gameId: "12345", gameProvider: "steam" }, description: "Start a new play session via the Playnite extension. Uses API key authentication." },
  { method: "POST", path: "/api/playnite/sessions/ping", resource: "playnite", name: "Ping Session", auth: true, authType: "apikey", body: { gameTitle: "Elden Ring", gameId: "12345", gameProvider: "steam" }, description: "Ping an active session to keep it alive via the Playnite extension. Uses API key authentication." },
  { method: "POST", path: "/api/playnite/sessions/end", resource: "playnite", name: "End Session", auth: true, authType: "apikey", description: "End the currently active session via the Playnite extension. Uses API key authentication." },

  // ===== METRICS =====
  { method: "GET", path: "/api/metrics", resource: "metrics", name: "Get Metrics", auth: true, description: "Get user-specific metrics and statistics." },

  // ===== MANUAL =====
  { method: "GET", path: "/api/manual/inputs", resource: "manual", name: "List Inputs", auth: true, description: "List all manual tracking inputs for the current user." },
  { method: "POST", path: "/api/manual/sessions/start", resource: "manual", name: "Start Manual Session", auth: true, body: { title: "Elden Ring" }, description: "Start a manually tracked play session." },
  { method: "POST", path: "/api/manual/sessions/stop", resource: "manual", name: "Stop Manual Session", auth: true, description: "Stop the currently active manually tracked session." },
  { method: "GET", path: "/api/manual/search", resource: "manual", name: "Search Mappings", auth: true, query: [{ name: "query", value: "string" }], description: "Search for game mappings to match manual entries to games." },
  { method: "POST", path: "/api/manual/entries", resource: "manual", name: "Create Manual Entry", auth: true, body: { date: "2024-01-15", durationMinutes: 60, gameId: "string?", igdbId: "string?", title: "string?", platformKey: "string?", capsuleUrl: "string?" }, description: "Create a manual play session entry." },
  { method: "POST", path: "/api/manual/inputs/{{inputId}}/map", resource: "manual", name: "Map Input", auth: true, body: { gameId: "string?", igdbId: "string?", title: "string?", platformKey: "string?", platformId: 0, capsuleUrl: "string?" }, description: "Map a manual input to a game." },

  // ===== PROVIDERS =====
  { method: "GET", path: "/api/providers", resource: "providers", name: "List Providers", auth: true, description: "List all gaming platform providers and their connection status." },
  { method: "PUT", path: "/api/providers/status", resource: "providers", name: "Update Provider Status", auth: true, body: { providerId: 1, status: "connected" }, description: "Update the connection status of a provider." },
  { method: "POST", path: "/api/playnite/key", resource: "providers", name: "Get Playnite Key", auth: true, description: "Generate or retrieve the Playnite API key for the current user." },

  // ===== RECAP =====
  { method: "GET", path: "/api/recap/years", resource: "recap", name: "Get Recap Years", auth: true, description: "List the years for which recap data is available." },
  { method: "GET", path: "/api/recap", resource: "recap", name: "Get Recap", auth: true, query: [{ name: "from", value: "2024-01-01" }, { name: "to", value: "2024-12-31" }], description: "Get yearly recap/wrapped data for a date range." },

  // ===== SETTINGS =====
  { method: "GET", path: "/api/settings", resource: "settings", name: "Get Settings", auth: true, description: "Get the current user's settings." },
  { method: "PUT", path: "/api/settings", resource: "settings", name: "Update Settings", auth: true, body: { ignoreShortSessionsMinutes: 5 }, description: "Update the current user's settings." },

  // ===== SYSTEM =====
  { method: "GET", path: "/api", resource: "system", name: "Health Check", auth: false, description: "Health check ping endpoint. Returns 'Ok'." },
];

// ===== GENERATION =====

function capitalize(str) {
  return str.charAt(0).toUpperCase() + str.slice(1);
}

function generateFileName(route) {
  return route.name.toLowerCase().replace(/\s+/g, "-");
}

function generateBruFile(route, seq) {
  const lines = [];
  const method = route.method.toLowerCase();

  lines.push("meta {");
  lines.push(`  name: ${route.name}`);
  lines.push("  type: http");
  lines.push(`  seq: ${seq}`);
  lines.push("}");
  lines.push("");

  const urlPath = route.path;

  lines.push(`${method} {`);
  lines.push(`  url: {{baseUrl}}${urlPath}`);

  if (["post", "put", "patch"].includes(method) && route.body) {
    lines.push("  body: json");
  } else {
    lines.push("  body: none");
  }

  const authType = route.authType || "bearer";
  if (route.auth) {
    lines.push(`  auth: ${authType}`);
  } else {
    lines.push("  auth: none");
  }

  lines.push("}");
  lines.push("");

  if (route.auth && authType === "bearer") {
    lines.push("auth:bearer {");
    lines.push("  token: {{authToken}}");
    lines.push("}");
    lines.push("");
  } else if (route.auth && authType === "apikey") {
    lines.push("auth:apikey {");
    lines.push("  key: {{playniteApiKey}}");
    lines.push("  value: {{playniteApiKey}}");
    lines.push("  in: header");
    lines.push("}");
    lines.push("");
  }

  if (route.query?.length) {
    lines.push("query {");
    for (const param of route.query) {
      lines.push(`  ${param.name}: ${param.value}`);
    }
    lines.push("}");
    lines.push("");
  }

  lines.push("headers {");
  lines.push("  Accept: application/json");
  if (["post", "put", "patch"].includes(method)) {
    lines.push("  Content-Type: application/json");
  }
  lines.push("}");
  lines.push("");

  if (["post", "put", "patch"].includes(method) && route.body) {
    lines.push("body:json {");
    const bodyJson = JSON.stringify(route.body, null, 2);
    for (const line of bodyJson.split("\n")) {
      lines.push(`  ${line}`);
    }
    lines.push("}");
    lines.push("");
  }

  if (route.description) {
    lines.push("docs {");
    lines.push(`  ${route.description}`);
    lines.push("}");
    lines.push("");
  }

  if (route.postScript) {
    lines.push("script:post-response {");
    for (const line of route.postScript.split("\n")) {
      lines.push(`  ${line}`);
    }
    lines.push("}");
    lines.push("");
  }

  return lines.join("\n");
}

function generateEnvironments(outputDir) {
  const envsDir = path.join(outputDir, "environments");
  if (!fs.existsSync(envsDir)) fs.mkdirSync(envsDir, { recursive: true });

  const environments = [
    { name: "Development", baseUrl: "http://localhost:5246/api" },
    { name: "Staging", baseUrl: "https://staging-api.playdex.app/api" },
    { name: "Production", baseUrl: "https://api.playdex.app/api" },
  ];

  for (const env of environments) {
    const content = `vars {
  baseUrl: ${env.baseUrl}
  authToken:
  refreshToken:
  accountId: 1
  sessionId: 1
  gameId: 1
  credentialId: 1
  inputId: 1
  playniteApiKey:
}

vars:secret [
  authToken,
  refreshToken,
  playniteApiKey
]
`;
    fs.writeFileSync(path.join(envsDir, `${env.name}.bru`), content);
  }
}

function generate() {
  if (!fs.existsSync(OUTPUT_DIR)) fs.mkdirSync(OUTPUT_DIR, { recursive: true });

  const manifest = { version: "1", name: "Playdex API", type: "collection", ignore: ["node_modules", ".git"] };
  fs.writeFileSync(path.join(OUTPUT_DIR, "bruno.json"), JSON.stringify(manifest, null, 2) + "\n");

  generateEnvironments(OUTPUT_DIR);

  const grouped = {};
  for (const route of routes) {
    if (!grouped[route.resource]) grouped[route.resource] = [];
    grouped[route.resource].push(route);
  }

  for (const [resource, resourceRoutes] of Object.entries(grouped)) {
    const folderPath = path.join(OUTPUT_DIR, resource);
    if (!fs.existsSync(folderPath)) fs.mkdirSync(folderPath, { recursive: true });

    fs.writeFileSync(path.join(folderPath, "folder.bru"), `meta {\n  name: ${capitalize(resource)}\n}\n`);

    let seq = 1;
    for (const route of resourceRoutes) {
      const fileName = generateFileName(route);
      const content = generateBruFile(route, seq++);
      fs.writeFileSync(path.join(folderPath, `${fileName}.bru`), content);
    }
  }

  console.log(`Generated Bruno collection in ${OUTPUT_DIR}`);
}

generate();

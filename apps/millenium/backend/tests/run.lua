local backend_dir = arg[0]:gsub("[/\\]tests[/\\]run%.lua$", "")
package.path = backend_dir .. "/?.lua;" .. package.path

local failures = 0

local function assert_equal(actual, expected, label)
  if actual ~= expected then
    failures = failures + 1
    print("FAIL " .. label .. ": expected " .. tostring(expected) .. ", got " .. tostring(actual))
  end
end

local function assert_truthy(value, label)
  if not value then
    failures = failures + 1
    print("FAIL " .. label .. ": expected truthy value")
  end
end

local function encode_value(value)
  if type(value) == "table" then
    local parts = {}
    for key, item in pairs(value) do
      parts[#parts + 1] = '"' .. key .. '":' .. encode_value(item)
    end
    return "{" .. table.concat(parts, ",") .. "}"
  end

  if type(value) == "string" then
    return '"' .. value:gsub("\\", "\\\\"):gsub('"', '\\"') .. '"'
  end

  if type(value) == "boolean" or type(value) == "number" then
    return tostring(value)
  end

  return "null"
end

local decode_queue = {}

PLAYDEX_JSON = {
  encode = encode_value,
  decode = function()
    local value = table.remove(decode_queue, 1)
    if value == "ERROR" then
      error("invalid json")
    end
    return value
  end,
}

local requests = {}
PLAYDEX_HTTP = {
  post = function(url, body, options)
    requests[#requests + 1] = { url = url, body = body, options = options }
    return { status = 200 }
  end,
}

local function reset_modules()
  package.loaded.playdex_json = nil
  package.loaded.logger = nil
  package.loaded.game_identity = nil
  package.loaded.settings = nil
  package.loaded.http_client = nil
  package.loaded.playdex = nil
  requests = {}
  decode_queue = {}
end

local function test_game_identity()
  reset_modules()
  local identity = require("game_identity")

  local parsed = identity.parse_game_name("Hades [1145360]", "")
  assert_equal(parsed.gameTitle, "Hades", "parsed title")
  assert_equal(parsed.gameId, "1145360", "parsed id")
  assert_equal(parsed.gameProvider, "steam", "parsed provider")

  local configured = identity.parse_game_name("Counter-Strike 2 [730]", " 123 ")
  assert_equal(configured.gameTitle, "Counter-Strike 2", "configured title")
  assert_equal(configured.gameId, "123", "configured id")

  local missing = identity.parse_game_name("No Id", "")
  assert_equal(missing, nil, "missing id skips")
end

local function test_settings()
  reset_modules()
  PLAYDEX_SETTINGS_PATH = backend_dir .. "/tests/.tmp-settings.json"
  os.remove(PLAYDEX_SETTINGS_PATH)

  decode_queue[#decode_queue + 1] = {
    api_key = "global",
    games = {
      ["Hades"] = { gameId = "1145360", api_key = "game-key" },
    },
  }

  local settings = require("settings")
  local file = io.open(PLAYDEX_SETTINGS_PATH, "w")
  file:write("{}")
  file:close()

  assert_truthy(settings.load(), "settings load")
  assert_equal(settings.get_api_key(), "global", "global api key")
  assert_equal(settings.get_api_key("Hades"), "game-key", "game api key")
  assert_equal(settings.get_game_id("Hades"), "1145360", "game id")

  assert_truthy(settings.set_game_api_key("Hades", "  "), "clear game key")
  assert_equal(settings.get_api_key("Hades"), "global", "empty game key falls back")

  assert_truthy(settings.set_game_id("Hades", " 730 "), "trim game id")
  assert_equal(settings.get_game_id("Hades"), "730", "trimmed game id")

  os.remove(PLAYDEX_SETTINGS_PATH)
end

local function test_corrupt_settings()
  reset_modules()
  PLAYDEX_SETTINGS_PATH = backend_dir .. "/tests/.tmp-settings.json"
  os.remove(PLAYDEX_SETTINGS_PATH)
  decode_queue[#decode_queue + 1] = "ERROR"

  local file = io.open(PLAYDEX_SETTINGS_PATH, "w")
  file:write("{")
  file:close()

  local settings = require("settings")
  assert_equal(settings.load(), false, "corrupt settings returns false")
  assert_equal(settings.get_api_key(), "", "corrupt settings uses empty api key")

  os.remove(PLAYDEX_SETTINGS_PATH)
end

local function test_playdex_requests()
  reset_modules()
  local settings = require("settings")
  settings._replace_for_test({
    api_key = "global",
    games = {
      ["Hades"] = { gameId = "1145360", api_key = "" },
      ["Celeste"] = { gameId = "", api_key = "" },
    },
  })

  local playdex = require("playdex")
  assert_truthy(playdex.ping_session("Hades"), "ping sends request")
  assert_equal(#requests, 1, "one ping request")
  assert_equal(requests[1].url, "https://playdex.ejwan.dev/api/playnite/sessions/ping", "ping url")
  assert_equal(requests[1].options.headers.Authorization, "Bearer global", "auth header")

  assert_truthy(playdex.end_session("Hades"), "end sends request")
  assert_equal(#requests, 2, "two requests")
  assert_equal(requests[2].url, "https://playdex.ejwan.dev/api/playnite/sessions/end", "end url")

  assert_equal(playdex.ping_session("Celeste"), false, "missing id skips ping")
end

test_game_identity()
test_settings()
test_corrupt_settings()
test_playdex_requests()

if failures > 0 then
  os.exit(1)
end

print("Lua backend tests passed")

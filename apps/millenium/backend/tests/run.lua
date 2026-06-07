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
  package.loaded.playdex_logger = nil
  package.loaded.logger = nil
  package.loaded.game_identity = nil
  package.loaded.settings = nil
  package.loaded.http_client = nil
  package.loaded.playdex = nil
  package.loaded.rpc = nil
  package.loaded.main = nil
  package.loaded.millennium = nil
  package.loaded.fs = nil
  requests = {}
  decode_queue = {}
  PLAYDEX_SETTINGS_PATH = nil
  PLUGIN_BASE_DIR = nil
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

  local quoted = identity.parse_game_name("Counter-Strike 2 [730]", '"123"')
  assert_equal(quoted.gameId, "123", "quoted configured id")

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

  assert_truthy(settings.set_game_id("Hades", '"123"'), "unwrap quoted game id")
  assert_equal(settings.get_game_id("Hades"), "123", "unwrapped game id")

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

local function test_rpc_callable_payload_shapes()
  reset_modules()
  local settings = require("settings")
  settings._replace_for_test({
    api_key = "global",
    games = {},
  })

  local rpc = require("rpc")
  assert_truthy(rpc.SetGameId("Hades", "1145360"), "positional payload saves game id")
  assert_equal(settings.get_game_id("Hades"), "1145360", "positional payload game id")

  assert_truthy(rpc.OnGameApiKeyFieldChange("Hades", "game-key"), "positional payload saves key")
  assert_equal(settings.get_api_key("Hades"), "game-key", "positional payload game api key")

  assert_equal(rpc.GetGameId("Hades"), "1145360", "positional payload reads game id")

  assert_truthy(rpc.SetGameId({ { app_name = "Hades", id = "1145360" } }), "array payload saves game id")
  assert_equal(settings.get_game_id("Hades"), "1145360", "array payload game id")

  assert_truthy(rpc.OnGameApiKeyFieldChange({ args = { { app_name = "Hades", key = "game-key" } } }), "args payload saves key")
  assert_equal(settings.get_api_key("Hades"), "game-key", "args payload game api key")

  assert_equal(rpc.GetGameId({ params = { { app_name = "Hades" } } }), "1145360", "params payload reads game id")
end

local function test_logger_uses_millennium_logger()
  reset_modules()
  local messages = {}

  package.preload.logger = function()
    return {
      info = function(_, message)
        messages[#messages + 1] = message
      end,
    }
  end

  local logger = require("playdex_logger")
  logger.info("hello")
  assert_equal(messages[1], "[Playdex] hello", "millennium logger receives message")

  package.preload.logger = nil
end

local function test_settings_path_uses_module_location()
  reset_modules()
  local settings = require("settings")
  local app_data = os.getenv("APPDATA") or os.getenv("LOCALAPPDATA")
  local expected = app_data and (app_data .. "\\playdex\\millennium\\playdex\\settings.json") or (backend_dir .. "\\settings.json")

  assert_equal(settings._settings_path_for_test(), expected, "settings path uses writable location")
end

local function test_main_load_signals_ready()
  reset_modules()
  local ready_called = false

  package.preload.millennium = function()
    return {
      ready = function()
        ready_called = true
      end,
    }
  end

  PLAYDEX_SETTINGS_PATH = backend_dir .. "/tests/.tmp-settings.json"
  os.remove(PLAYDEX_SETTINGS_PATH)

  local plugin = require("main")
  assert_equal(type(plugin.on_load), "function", "main exposes on_load")
  plugin:on_load()
  assert_truthy(ready_called, "on_load signals millennium ready")

  package.preload.millennium = nil
  os.remove(PLAYDEX_SETTINGS_PATH)
end

test_game_identity()
test_settings()
test_corrupt_settings()
test_playdex_requests()
test_rpc_callable_payload_shapes()
test_logger_uses_millennium_logger()
test_settings_path_uses_module_location()
test_main_load_signals_ready()

if failures > 0 then
  os.exit(1)
end

print("Lua backend tests passed")

local json = require("playdex_json")
local logger = require("logger")
local identity = require("game_identity")

local settings = {}

local M = {}

local function base_dir()
  return rawget(_G, "PLUGIN_BASE_DIR") or "."
end

local function path_join(left, right)
  local sep = package.config:sub(1, 1)
  if left:sub(-1) == "/" or left:sub(-1) == "\\" then
    return left .. right
  end
  return left .. sep .. right
end

local function settings_path()
  return rawget(_G, "PLAYDEX_SETTINGS_PATH") or path_join(base_dir(), "settings.json")
end

local function normalize()
  if type(settings) ~= "table" then
    settings = {}
  end

  if type(settings.games) ~= "table" then
    settings.games = {}
  end
end

local function read_all(path)
  local file = io.open(path, "r")
  if not file then
    return nil
  end

  local content = file:read("*a")
  file:close()
  return content
end

local function write_all(path, content)
  local file, err = io.open(path, "w")
  if not file then
    return false, err
  end

  file:write(content)
  file:close()
  return true, nil
end

function M.load()
  local path = settings_path()
  local content = read_all(path)
  if not content or content == "" then
    settings = {}
    normalize()
    return true
  end

  local ok, decoded = pcall(json.decode, content)
  if not ok or type(decoded) ~= "table" then
    logger.warn("settings.json is corrupted; using empty settings")
    settings = {}
    normalize()
    return false
  end

  settings = decoded
  normalize()
  return true
end

function M.save()
  normalize()

  local path = settings_path()
  local temp_path = path .. ".tmp"
  local ok, encoded = pcall(json.encode, settings)
  if not ok then
    logger.error("failed to encode settings: " .. tostring(encoded))
    return false
  end

  local wrote, write_err = write_all(temp_path, encoded)
  if not wrote then
    logger.error("failed to write settings: " .. tostring(write_err))
    return false
  end

  os.remove(path)
  local renamed, rename_err = os.rename(temp_path, path)
  if not renamed then
    logger.error("failed to replace settings: " .. tostring(rename_err))
    os.remove(temp_path)
    return false
  end

  return true
end

function M.set_api_key(key)
  settings.api_key = identity.trim(key)
  return M.save()
end

function M.get_api_key(app_name)
  normalize()

  if app_name then
    local game = settings.games[app_name]
    if type(game) == "table" and identity.trim(game.api_key) ~= "" then
      return game.api_key
    end
  end

  return settings.api_key or ""
end

function M.set_game_api_key(app_name, key)
  normalize()
  local name = identity.trim(app_name)
  if name == "" then
    return false
  end

  settings.games[name] = settings.games[name] or {}
  settings.games[name].api_key = identity.trim(key)
  return M.save()
end

function M.set_game_id(app_name, game_id)
  normalize()
  local name = identity.trim(app_name)
  if name == "" then
    return false
  end

  settings.games[name] = settings.games[name] or {}
  settings.games[name].gameId = identity.trim(game_id)
  return M.save()
end

function M.get_game_id(app_name)
  normalize()
  local game = settings.games[app_name]
  if type(game) ~= "table" then
    return ""
  end

  return game.gameId or ""
end

function M.resolve_game_identity(app_name)
  normalize()
  local game = settings.games[app_name]
  local configured_game_id = type(game) == "table" and game.gameId or ""
  return identity.parse_game_name(app_name, configured_game_id)
end

function M._replace_for_test(next_settings)
  settings = next_settings or {}
  normalize()
end

function M._get_for_test()
  normalize()
  return settings
end

return M

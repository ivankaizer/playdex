local json = require("playdex_json")
local logger = require("playdex_logger")
local identity = require("game_identity")

local settings = {}

local M = {}

local function get_fs()
  local ok, fs = pcall(require, "fs")
  if ok and type(fs) == "table" then
    return fs
  end

  return nil
end

local function base_dir()
  local configured = rawget(_G, "PLUGIN_BASE_DIR")
  if type(configured) == "string" and configured ~= "" then
    return configured
  end

  local source = debug.getinfo(1, "S").source
  if type(source) == "string" and source:sub(1, 1) == "@" then
    local file_path = source:sub(2)
    local fs = get_fs()
    if fs and fs.parent_path then
      return fs.parent_path(fs.parent_path(file_path))
    end

    local backend_dir = file_path:match("^(.*)[/\\][^/\\]+$")
    if backend_dir then
      return backend_dir:match("^(.*)[/\\][^/\\]+$") or backend_dir
    end
  end

  return "."
end

local function path_join(left, right)
  local fs = get_fs()
  if fs and fs.join then
    return fs.join(left, right)
  end

  local sep = package.config:sub(1, 1)
  if left:sub(-1) == "/" or left:sub(-1) == "\\" then
    return left .. right
  end
  return left .. sep .. right
end

local function plugin_name()
  return rawget(_G, "PLAYDEX_PLUGIN_NAME") or "playdex"
end

local function app_data_dir()
  local app_data = os.getenv("APPDATA") or os.getenv("LOCALAPPDATA")
  if type(app_data) == "string" and app_data ~= "" then
    return path_join(path_join(path_join(app_data, "playdex"), "millennium"), plugin_name())
  end

  return nil
end

local function settings_path()
  return rawget(_G, "PLAYDEX_SETTINGS_PATH") or path_join(app_data_dir() or base_dir(), "settings.json")
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

local function ensure_directory(path)
  local probe = io.open(path_join(path, ".playdex-write-test"), "w")
  if probe then
    probe:close()
    os.remove(path_join(path, ".playdex-write-test"))
    return true, nil
  end

  local fs = get_fs()
  if fs and fs.create_directories then
    local ok, err = fs.create_directories(path)
    if not ok and err then
      return false, err
    end
    return true, nil
  end

  local command
  if package.config:sub(1, 1) == "\\" then
    command = 'mkdir "' .. path .. '" >NUL 2>NUL'
  else
    command = 'mkdir -p "' .. path .. '"'
  end

  local ok = os.execute(command)
  if ok == true or ok == 0 then
    return true, nil
  end

  local after = io.open(path_join(path, ".playdex-write-test"), "w")
  if after then
    after:close()
    os.remove(path_join(path, ".playdex-write-test"))
    return true, nil
  end

  return false, "failed to create directory " .. path
end

local function write_all(path, content)
  local parent = path:match("^(.*)[/\\][^/\\]+$")
  if parent then
    local ok, err = ensure_directory(parent)
    if not ok and err then
      return false, err
    end
  end

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
  logger.info("loading settings from " .. path)
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
  logger.info("saving settings to " .. path)
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

  local fs = get_fs()
  if fs and fs.remove then
    fs.remove(path)
  else
    os.remove(path)
  end

  local renamed, rename_err
  if fs and fs.rename then
    renamed, rename_err = fs.rename(temp_path, path, false)
  else
    renamed, rename_err = os.rename(temp_path, path)
  end
  if not renamed then
    logger.error("failed to replace settings: " .. tostring(rename_err))
    if fs and fs.remove then
      fs.remove(temp_path)
    else
      os.remove(temp_path)
    end
    return false
  end

  logger.info("settings saved")
  return true
end

function M.set_api_key(key)
  settings.api_key = identity.trim(key)
  local saved = M.save()
  if not saved then
    logger.error("failed to save global api key")
  end
  return saved
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
  local saved = M.save()
  if not saved then
    logger.error("failed to save game api key for " .. name)
  end
  return saved
end

function M.set_game_id(app_name, game_id)
  normalize()
  local name = identity.trim(app_name)
  if name == "" then
    return false
  end

  settings.games[name] = settings.games[name] or {}
  settings.games[name].gameId = identity.trim(game_id)
  local saved = M.save()
  if not saved then
    logger.error("failed to save game id for " .. name)
  end
  return saved
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

function M._settings_path_for_test()
  return settings_path()
end

return M

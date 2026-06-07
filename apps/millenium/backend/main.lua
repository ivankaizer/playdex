local function infer_base_dir()
  local configured = rawget(_G, "PLUGIN_BASE_DIR")
  if type(configured) == "string" and configured ~= "" then
    return configured
  end

  local source = debug.getinfo(1, "S").source
  if type(source) == "string" and source:sub(1, 1) == "@" then
    local file_path = source:sub(2)
    local backend_dir = file_path:match("^(.*)[/\\][^/\\]+$")
    if backend_dir then
      return backend_dir:match("^(.*)[/\\][^/\\]+$") or backend_dir
    end
  end

  return "."
end

local base_dir = infer_base_dir()
package.path = base_dir .. "/backend/?.lua;" .. base_dir .. "\\backend\\?.lua;" .. package.path

local logger = require("playdex_logger")
local playdex = require("playdex")

RPC = require("rpc")

local Plugin = {}

local function get_millennium()
  local ok, millennium = pcall(require, "millennium")
  if ok then
    return millennium
  end

  return rawget(_G, "millennium")
end

function Plugin:on_frontend_loaded()
  logger.info("Frontend loaded")
end

function Plugin:on_load()
  PLAYDEX_PLUGIN_NAME = rawget(_G, "PLUGIN_NAME") or "playdex"
  playdex.load_settings()

  local millennium = get_millennium()
  if millennium and millennium.ready then
    millennium.ready()
  end

  logger.info("Backend loaded")
end

function Plugin:on_unload()
  logger.info("Backend unloaded")
end

return Plugin

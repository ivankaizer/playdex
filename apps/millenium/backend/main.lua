local base_dir = rawget(_G, "PLUGIN_BASE_DIR") or "."
package.path = base_dir .. "/backend/?.lua;" .. base_dir .. "\\backend\\?.lua;" .. package.path

local logger = require("logger")
local playdex = require("playdex")

RPC = require("rpc")

local Plugin = {}

function Plugin:_front_end_loaded()
  logger.info("Frontend loaded")
end

function Plugin:_load()
  playdex.load_settings()

  local millennium = rawget(_G, "millennium")
  if millennium and millennium.ready then
    millennium.ready()
  end

  logger.info("Backend loaded")
end

function Plugin:_unload()
  logger.info("Backend unloaded")
end

return Plugin

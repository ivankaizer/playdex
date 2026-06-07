local logger = require("logger")
local settings = require("settings")
local http_client = require("http_client")

local M = {}

function M.load_settings()
  settings.load()
end

function M.ping_session(app_name)
  local game = settings.resolve_game_identity(app_name)
  local api_key = settings.get_api_key(app_name)

  if not game then
    logger.warn("skipping ping event for " .. tostring(app_name) .. ": missing game id")
    return false
  end

  return http_client.post_session_event("ping", api_key, game)
end

function M.end_session(app_name)
  local game = settings.resolve_game_identity(app_name)
  local api_key = settings.get_api_key(app_name)

  if not game then
    logger.warn("skipping end event for " .. tostring(app_name) .. ": missing game id")
    return false
  end

  return http_client.post_session_event("end", api_key, game)
end

function M.set_api_key(key)
  return settings.set_api_key(key)
end

function M.set_game_api_key(app_name, key)
  return settings.set_game_api_key(app_name, key)
end

function M.get_api_key()
  return settings.get_api_key()
end

function M.get_game_api_key(app_name)
  return settings.get_api_key(app_name)
end

function M.set_game_id(app_name, game_id)
  return settings.set_game_id(app_name, game_id)
end

function M.get_game_id(app_name)
  return settings.get_game_id(app_name)
end

return M

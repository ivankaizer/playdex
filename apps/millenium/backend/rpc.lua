local json = require("playdex_json")
local logger = require("logger")
local playdex = require("playdex")

local RPC = {}

local function decode(payload)
  if type(payload) ~= "string" or payload == "" then
    return {}
  end

  local ok, decoded = pcall(json.decode, payload)
  if not ok or type(decoded) ~= "table" then
    logger.error("invalid RPC payload")
    return nil
  end

  return decoded
end

local function encode(value)
  if value == nil then
    return nil
  end

  return json.encode(value)
end

local function require_field(data, name)
  local value = data and data[name]
  if type(value) ~= "string" then
    logger.error("RPC missing field: " .. name)
    return nil
  end
  return value
end

function RPC.OnNonSteamAppHeartbeat(payload)
  local data = decode(payload)
  local app_name = require_field(data, "app_name")
  if not app_name then
    return nil
  end

  return encode(playdex.ping_session(app_name))
end

function RPC.OnNonSteamAppQuit(payload)
  local data = decode(payload)
  local app_name = require_field(data, "app_name")
  if not app_name then
    return nil
  end

  return encode(playdex.end_session(app_name))
end

function RPC.OnApiKeyFieldChange(payload)
  local data = decode(payload)
  local key = require_field(data, "key")
  if not key then
    return nil
  end

  return encode(playdex.set_api_key(key))
end

function RPC.OnGameApiKeyFieldChange(payload)
  local data = decode(payload)
  local app_name = require_field(data, "app_name")
  local key = require_field(data, "key")
  if not app_name or not key then
    return nil
  end

  return encode(playdex.set_game_api_key(app_name, key))
end

function RPC.GetApiKey()
  return encode(playdex.get_api_key() or "")
end

function RPC.GetGameApiKey(payload)
  local data = decode(payload)
  local app_name = require_field(data, "app_name")
  if not app_name then
    return encode("")
  end

  return encode(playdex.get_game_api_key(app_name) or "")
end

function RPC.SetGameId(payload)
  local data = decode(payload)
  local app_name = require_field(data, "app_name")
  local id = require_field(data, "id")
  if not app_name or not id then
    return nil
  end

  return encode(playdex.set_game_id(app_name, id))
end

function RPC.GetGameId(payload)
  local data = decode(payload)
  local app_name = require_field(data, "app_name")
  if not app_name then
    return encode("")
  end

  return encode(playdex.get_game_id(app_name) or "")
end

return RPC

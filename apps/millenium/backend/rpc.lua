local json = require("playdex_json")
local logger = require("playdex_logger")
local playdex = require("playdex")

local RPC = {}

local function keys_of(value)
  if type(value) ~= "table" then
    return ""
  end

  local keys = {}
  for key in pairs(value) do
    keys[#keys + 1] = tostring(key)
  end
  return table.concat(keys, ",")
end

local function first_value(value)
  if type(value) ~= "table" then
    return nil
  end

  if value[1] ~= nil then
    return value[1]
  end

  if value.args and type(value.args) == "table" then
    return first_value(value.args)
  end

  if value.params and type(value.params) == "table" then
    return first_value(value.params)
  end

  return nil
end

local function decode(payload)
  if type(payload) == "table" then
    logger.info("RPC payload table keys: " .. keys_of(payload))

    if type(payload.payload) == "string" then
      payload = payload.payload
    elseif payload.app_name or payload.key or payload.id then
      return payload
    else
      local first = first_value(payload)
      if type(first) == "table" then
        logger.info("RPC payload using first nested table keys: " .. keys_of(first))
        return first
      end

      if type(first) == "string" then
        payload = first
      else
        return payload
      end
    end
  end

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
  return value
end

local function require_field(data, name)
  local value = data and data[name]
  if type(value) ~= "string" then
    logger.error("RPC missing field: " .. name)
    return nil
  end
  return value
end

local function payload_or_string_field(payload, field_name)
  if type(payload) == "string" then
    return payload
  end

  local data = decode(payload)
  return require_field(data, field_name)
end

function RPC.OnNonSteamAppHeartbeat(payload)
  logger.info("RPC heartbeat")
  local app_name = payload_or_string_field(payload, "app_name")
  if not app_name then
    return nil
  end

  return encode(playdex.ping_session(app_name))
end

function RPC.OnNonSteamAppQuit(payload)
  logger.info("RPC quit")
  local app_name = payload_or_string_field(payload, "app_name")
  if not app_name then
    return nil
  end

  return encode(playdex.end_session(app_name))
end

function RPC.OnApiKeyFieldChange(payload)
  logger.info("RPC save global api key")
  local key = payload_or_string_field(payload, "key")
  if not key then
    return nil
  end

  return encode(playdex.set_api_key(key))
end

function RPC.OnGameApiKeyFieldChange(payload, key)
  logger.info("RPC save game api key")
  local app_name
  if type(payload) == "string" then
    app_name = payload
  else
    local data = decode(payload)
    app_name = require_field(data, "app_name")
    key = key or require_field(data, "key")
  end
  if not app_name or not key then
    return nil
  end

  logger.info("RPC save game api key for " .. app_name)
  local saved = playdex.set_game_api_key(app_name, key)
  logger.info("RPC save game api key result for " .. app_name .. ": " .. tostring(saved))
  return encode(saved)
end

function RPC.GetApiKey()
  return encode(playdex.get_api_key() or "")
end

function RPC.GetGameApiKey(payload)
  logger.info("RPC get game api key")
  local app_name = payload_or_string_field(payload, "app_name")
  if not app_name then
    return encode("")
  end

  return encode(playdex.get_game_api_key(app_name) or "")
end

function RPC.SetGameId(payload, id)
  logger.info("RPC save game id")
  local app_name
  if type(payload) == "string" then
    app_name = payload
  else
    local data = decode(payload)
    app_name = require_field(data, "app_name")
    id = id or require_field(data, "id")
  end
  if not app_name or not id then
    return nil
  end

  logger.info("RPC save game id for " .. app_name)
  local saved = playdex.set_game_id(app_name, id)
  logger.info("RPC save game id result for " .. app_name .. ": " .. tostring(saved))
  return encode(saved)
end

function RPC.GetGameId(payload)
  logger.info("RPC get game id")
  local app_name = payload_or_string_field(payload, "app_name")
  if not app_name then
    return encode("")
  end

  return encode(playdex.get_game_id(app_name) or "")
end

function OnNonSteamAppHeartbeat(payload)
  return RPC.OnNonSteamAppHeartbeat(payload)
end

function OnNonSteamAppQuit(payload)
  return RPC.OnNonSteamAppQuit(payload)
end

function OnApiKeyFieldChange(payload)
  return RPC.OnApiKeyFieldChange(payload)
end

function OnGameApiKeyFieldChange(app_name, key)
  return RPC.OnGameApiKeyFieldChange(app_name, key)
end

function GetApiKey()
  return RPC.GetApiKey()
end

function GetGameApiKey(payload)
  return RPC.GetGameApiKey(payload)
end

function SetGameId(app_name, id)
  return RPC.SetGameId(app_name, id)
end

function GetGameId(payload)
  return RPC.GetGameId(payload)
end

return RPC

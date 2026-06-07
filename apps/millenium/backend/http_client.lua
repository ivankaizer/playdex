local json = require("playdex_json")
local logger = require("logger")

local M = {}

local HOST = "playdex.ejwan.dev"

local function endpoint(event_type)
  return "https://" .. HOST .. "/api/playnite/sessions/" .. event_type
end

local function post(url, body, options)
  local injected = rawget(_G, "PLAYDEX_HTTP")
  if injected and injected.post then
    return injected.post(url, body, options)
  end

  local http = require("http")
  return http.post(url, body, options)
end

function M.post_session_event(event_type, api_key, game)
  if not api_key or api_key == "" then
    logger.warn("skipping " .. event_type .. " event: missing api key")
    return false
  end

  if not game then
    logger.warn("skipping " .. event_type .. " event: missing game identity")
    return false
  end

  local body = json.encode(game)
  local ok, response = pcall(post, endpoint(event_type), body, {
    headers = {
      Authorization = "Bearer " .. api_key,
      ["Content-Type"] = "application/json",
      Accept = "application/json",
    },
    timeout = 10,
  })

  if not ok then
    logger.error(event_type .. " request failed: " .. tostring(response))
    return false
  end

  if type(response) == "table" and response.status and response.status >= 400 then
    logger.error(event_type .. " request returned status " .. tostring(response.status))
    return false
  end

  return true
end

return M

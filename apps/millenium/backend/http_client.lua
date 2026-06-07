local json = require("playdex_json")
local logger = require("playdex_logger")

local M = {}

local HOST = "playdex.ejwan.dev"

local function endpoint(event_type)
  return "https://" .. HOST .. "/api/playnite/sessions/" .. event_type
end

local function log_request(event_type, url, body)
  logger.info(event_type .. " request url: " .. url)
  logger.info(event_type .. " request payload: " .. body)
end

local function auth_fingerprint(api_key)
  local key = tostring(api_key or "")
  if #key <= 8 then
    return "length=" .. tostring(#key)
  end

  return "length=" .. tostring(#key) .. " prefix=" .. key:sub(1, 4) .. " suffix=" .. key:sub(-4)
end

local function log_response(event_type, response, err)
  if not response then
    logger.error(event_type .. " request returned no response: " .. tostring(err))
    return
  end

  logger.info(event_type .. " response status: " .. tostring(response.status))
  if response.body and response.body ~= "" then
    logger.info(event_type .. " response body: " .. tostring(response.body))
  end
end

local function post(url, body, options)
  local injected = rawget(_G, "PLAYDEX_HTTP")
  if injected and injected.post then
    return injected.post(url, body, options)
  end

  local http = require("http")
  options.method = "POST"
  options.data = body
  return http.request(url, options)
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
  local url = endpoint(event_type)
  log_request(event_type, url, body)
  logger.info(event_type .. " auth fingerprint: " .. auth_fingerprint(api_key))

  local ok, response, err = pcall(post, url, body, {
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

  log_response(event_type, response, err)
  if not response then
    return false
  end

  if type(response) == "table" and response.status and response.status >= 400 then
    logger.error(
      event_type
        .. " request returned status "
        .. tostring(response.status)
        .. " for "
        .. url
        .. " with payload "
        .. body
    )
    return false
  end

  return true
end

return M

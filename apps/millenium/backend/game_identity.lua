local identity = {}

local function trim(value)
  if type(value) ~= "string" then
    return ""
  end

  local trimmed = value:match("^%s*(.-)%s*$")
  local quoted = trimmed:match('^"(.*)"$')
  if quoted then
    return quoted:gsub('\\"', '"'):gsub("\\\\", "\\")
  end

  return trimmed
end

local function clean_title(app_name)
  local title = trim(app_name)
  local cleaned = title:match("^(.-)%s*%[%d+%]%s*$")
  return trim(cleaned or title)
end

function identity.parse_game_name(app_name, configured_game_id)
  local title = clean_title(app_name)
  local game_id = trim(configured_game_id)

  if game_id == "" then
    game_id = trim((app_name or ""):match("%[(%d+)%]%s*$"))
  end

  if title == "" or game_id == "" then
    return nil
  end

  return {
    gameTitle = title,
    gameId = game_id,
    gameProvider = "steam",
  }
end

identity.clean_title = clean_title
identity.trim = trim

return identity

local logger = {}

local function write(level, message)
  local millennium = rawget(_G, "millennium")
  if millennium and millennium[level] then
    millennium[level](message)
    return
  end

  print("[Playdex] " .. level .. ": " .. message)
end

function logger.info(message)
  write("info", message)
end

function logger.warn(message)
  write("warn", message)
end

function logger.error(message)
  write("error", message)
end

return logger

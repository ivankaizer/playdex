local M = {}

local function get_logger()
  local ok, logger = pcall(require, "logger")
  if ok and type(logger) == "table" then
    return logger
  end

  return nil
end

local function write(level, message)
  local logger = get_logger()
  if logger and type(logger[level]) == "function" then
    logger[level](logger, "[Playdex] " .. tostring(message))
    return
  end

  print("[Playdex] " .. level .. ": " .. tostring(message))
end

function M.info(message)
  write("info", message)
end

function M.warn(message)
  write("warn", message)
end

function M.error(message)
  write("error", message)
end

return M

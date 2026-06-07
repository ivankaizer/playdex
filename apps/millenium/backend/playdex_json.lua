local test_json = rawget(_G, "PLAYDEX_JSON")
if test_json then
  return test_json
end

local ok, json = pcall(require, "json")
if ok then
  return json
end

ok, json = pcall(require, "cjson")
if ok then
  return json
end

error("Playdex could not load a JSON module")

from functools import wraps
import json
from operator import itemgetter
from typing import Callable

from helpers import json_replacer, json_reviver
import playdex


def rpcmethod(func: Callable):
  """
  Decorator for RPC methods, handling JSON serialisation and deserialisation
  """
  args_names = func.__code__.co_varnames[:func.__code__.co_argcount]

  @wraps(func)
  def wrapper_func(payload: str):
    data = json.loads(payload, object_hook=json_reviver)

    if not args_names:
      ret = func()
    elif len(args_names) == 1:
      ret = func(data[args_names[0]])
    else:
      args = itemgetter(*args_names)(data)
      ret = func(*args)

    if ret is not None:
      return json.dumps(ret, default=json_replacer)

  return wrapper_func


class RPC:
  @staticmethod
  @rpcmethod
  def OnNonSteamAppLaunch(app_name: str):
    playdex.start_session(app_name)

  @staticmethod
  @rpcmethod
  def OnNonSteamAppHeartbeat(app_name: str):
    playdex.ping_session(app_name)

  @staticmethod
  @rpcmethod
  def OnNonSteamAppQuit(app_name: str):
    playdex.stop_session(app_name)

  # ----------------------------------------------------------------
  # API keys (global + per-game override)
  # ----------------------------------------------------------------

  @staticmethod
  @rpcmethod
  def OnApiKeyFieldChange(key: str):
    # sets global api_key
    playdex.set_api_key(key)

  @staticmethod
  @rpcmethod
  def OnGameApiKeyFieldChange(app_name: str, key: str):
    # sets per-game api_key override
    playdex.set_api_key(key, app_name)

  @staticmethod
  @rpcmethod
  def GetApiKey():
    # returns global api_key
    return playdex.get_api_key()

  @staticmethod
  @rpcmethod
  def GetGameApiKey(app_name: str):
    # returns per-game override if set, otherwise global
    return playdex.get_api_key(app_name)

  # ----------------------------------------------------------------
  # Game IDs
  # ----------------------------------------------------------------

  @staticmethod
  @rpcmethod
  def SetGameId(app_name: str, id: str):
    playdex.set_game_id(app_name, id)

  @staticmethod
  @rpcmethod
  def GetGameId(app_name: str):
    return playdex.get_game_id(app_name)


rpc = RPC()

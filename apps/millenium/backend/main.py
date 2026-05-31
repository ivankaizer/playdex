import Millennium # type: ignore

from rpc import *
import playdex

class Plugin:
  def _front_end_loaded(self):
    print("Frontend loaded")
    pass
  
  def _load(self):
    playdex.load_settings()
    Millennium.ready()
    print("Backend loaded")
    pass

  def _unload(self):
    pass

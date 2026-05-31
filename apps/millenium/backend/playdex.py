from pathlib import Path
from datetime import datetime, timezone, timedelta
import json
from typing import TypedDict, Optional, Dict, Any
import tempfile
import re
import requests

from helpers import json_replacer, json_reviver
from __main__ import PLUGIN_BASE_DIR

# ------------------------------------------------------------------

SETTINGS_PATH = Path(PLUGIN_BASE_DIR) / "settings.json"
HOST = "playdex.ejwan.dev"


def endpoint(type: str):
    return f"https://{HOST}/api/playnite/sessions/{type}"


# ------------------------------------------------------------------
# Types
# ------------------------------------------------------------------

class GameSettings(TypedDict, total=False):
    gameId: int
    api_key: str


class Settings(TypedDict, total=False):
    api_key: str
    games: Dict[str, GameSettings]


_settings: Settings = {}


# ------------------------------------------------------------------
# Parsing
# ------------------------------------------------------------------

def parse_game_name(raw: str) -> dict:
    """
    Parse a game name like:
      'Absolum [1904480]'
    or:
      'Absolum'

    Returns:
      { "name": str, "id": int | None }
    """

    game_id = get_game_id(raw)
    if game_id:
        return {
            "name": raw,
            "id": int(game_id)
        }

    pattern = r'^\s*(?P<name>.+?)(?:\s*\[(?P<id>\d+)\])?\s*$'
    match = re.match(pattern, raw)

    if not match:
        return {"name": raw.strip(), "id": None}

    return {
        "name": match.group("name").strip(),
        "id": int(match.group("id")) if match.group("id") else None
    }


# ------------------------------------------------------------------
# HTTP
# ------------------------------------------------------------------

def send_game_payload(
    url: str,
    game: Optional[Dict[str, Any]] = None,
    timeout: int = 10
) -> requests.Response:
    """
    Sends a JSON POST request with game data.

    Payload format:
    {
        "gameTitle": str,
        "gameId": int | None,
        "gameProvider": "steam" | "unknown"
    }
    """

    payload: Dict[str, Any] = {}

    if game is not None and game.get("id") is None:
        return

    if game is not None:
        payload = {
            "gameTitle": game.get("name"),
            "gameId": game.get("id"),
            "gameProvider": "steam" if game.get("id") is not None else "unknown",
        }

    bearer_token = get_api_key(game.get("name") if game else None)

    headers = {
        "Authorization": f"Bearer {bearer_token}",
        "Content-Type": "application/json",
        "Accept": "application/json",
    }

    response = requests.post(
        url,
        json=payload,
        headers=headers,
        timeout=timeout,
    )

    response.raise_for_status()
    return response


# ------------------------------------------------------------------
# Settings
# ------------------------------------------------------------------

def load_settings():
    global _settings

    if SETTINGS_PATH.exists():
        try:
            with open(SETTINGS_PATH, "r") as f:
                _settings = json.load(f, object_hook=json_reviver)
        except json.JSONDecodeError:
            print("Warning: settings.json is corrupted")
            _settings = {}


def save_settings() -> None:
    with tempfile.NamedTemporaryFile(
        mode="w",
        dir=str(SETTINGS_PATH.parent),
        delete=False
    ) as tf:
        json.dump(_settings, tf, indent=2, default=json_replacer)
        temp_path = Path(tf.name)

    temp_path.replace(SETTINGS_PATH)


# ------------------------------------------------------------------
# Game settings
# ------------------------------------------------------------------

def set_game_id(app_name: str, game_id: str) -> None:
    if "games" not in _settings:
        _settings["games"] = {}

    game = _settings["games"].setdefault(app_name, {})
    game["gameId"] = game_id.strip()

    save_settings()


def get_game_id(app_name: str) -> str:
    game = _settings.get("games", {}).get(app_name)
    if game is None:
        return ""

    return game.get("gameId", "")


def set_game_api_key(app_name: str, key: str) -> None:
    if "games" not in _settings:
        _settings["games"] = {}

    game = _settings["games"].setdefault(app_name, {})
    game["api_key"] = key.strip()

    save_settings()


# ------------------------------------------------------------------
# API key logic (override + fallback)
# ------------------------------------------------------------------

def set_api_key(key: str, app_name: str | None = None):
    if app_name:
        set_game_api_key(app_name, key)
        return

    _settings["api_key"] = key.strip()
    save_settings()


def get_api_key(app_name: str | None = None) -> str | None:
    if app_name:
        game = _settings.get("games", {}).get(app_name)
        if game and game.get("api_key"):
            return game["api_key"]

    return _settings.get("api_key")


# ------------------------------------------------------------------
# Session events
# ------------------------------------------------------------------

def start_session(app_name: str):
    print(f"Non-steam app {app_name} launched, starting session...")
    send_game_payload(endpoint("start"), parse_game_name(app_name))


def ping_session(app_name: str):
    send_game_payload(endpoint("ping"), parse_game_name(app_name))


def stop_session(app_name: str):
    print(f"Non-steam app {app_name} stopped, ending session...")
    send_game_payload(endpoint("end"))

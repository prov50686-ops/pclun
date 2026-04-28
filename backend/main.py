"""Tiny online-counter backend for the PcLun launcher.

Endpoints:
    POST /heartbeat  body={"username": str}
        -> {"online": int, "total": int}
        Counts unique usernames seen in the last 5 minutes.
    GET /online
        -> {"online": int, "total": int}
"""
from __future__ import annotations

import os
import time
from collections import OrderedDict
from threading import Lock

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

ONLINE_WINDOW_SECONDS = 300  # 5 минут
DATA_FILE = os.environ.get("PCLUN_DATA_FILE", "/data/total.txt")

app = FastAPI(title="PcLun Online", version="0.1.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

_lock = Lock()
_recent: "OrderedDict[str, float]" = OrderedDict()
_total_lifetime = 0


def _load_total() -> int:
    try:
        with open(DATA_FILE, "r", encoding="utf-8") as fh:
            return int(fh.read().strip() or "0")
    except (OSError, ValueError):
        return 0


def _save_total(value: int) -> None:
    try:
        os.makedirs(os.path.dirname(DATA_FILE), exist_ok=True)
        with open(DATA_FILE, "w", encoding="utf-8") as fh:
            fh.write(str(value))
    except OSError:
        # ephemeral filesystem on hobby plan — that's fine
        pass


_total_lifetime = _load_total()


class Heartbeat(BaseModel):
    username: str


def _prune(now: float) -> None:
    cutoff = now - ONLINE_WINDOW_SECONDS
    while _recent and next(iter(_recent.values())) < cutoff:
        _recent.popitem(last=False)


@app.post("/heartbeat")
def heartbeat(beat: Heartbeat) -> dict:
    global _total_lifetime
    name = (beat.username or "Гость").strip()[:32]
    now = time.time()
    with _lock:
        _prune(now)
        is_new = name not in _recent
        _recent[name] = now
        _recent.move_to_end(name)
        if is_new:
            _total_lifetime += 1
            _save_total(_total_lifetime)
        return {"online": len(_recent), "total": _total_lifetime}


@app.get("/online")
def online() -> dict:
    now = time.time()
    with _lock:
        _prune(now)
        return {"online": len(_recent), "total": _total_lifetime}


@app.get("/")
def root() -> dict:
    return {"service": "pclun-online", "ok": True}

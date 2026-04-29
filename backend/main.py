"""Backend for the PcLun launcher.

Endpoints:
    POST /heartbeat  body={"username": str}
        -> {"online": int, "total": int}
        Counts unique usernames seen in the last 5 minutes (rate-limited per IP).
    GET /online
        -> {"online": int, "total": int}
    GET /stats
        -> {"online": int, "total": int, "today": int, "peak": int}
    GET /news
        -> {"items": [{"title", "body", "date", "tag"}]}
    GET /servers/featured
        -> {"items": [{"name", "address", "tag"}]}
    GET /leaderboard
        -> {"items": [{"name", "sessions", "last_seen"}]}
"""
from __future__ import annotations

import json
import os
import sqlite3
import time
from collections import OrderedDict, defaultdict
from contextlib import contextmanager
from threading import Lock

from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

ONLINE_WINDOW_SECONDS = 300  # 5 минут
DATA_DIR = os.environ.get("PCLUN_DATA_DIR", "/data")
DB_PATH = os.environ.get("PCLUN_DB", os.path.join(DATA_DIR, "pclun.db"))
LEGACY_TOTAL_FILE = os.environ.get("PCLUN_DATA_FILE", os.path.join(DATA_DIR, "total.txt"))

# Rate limit: max 30 heartbeats / minute / IP. Clients ping ~1/min, so this is
# extremely lenient for real users but blocks `for i in {1..1e6}; do curl …`.
RATE_LIMIT_WINDOW = 60.0
RATE_LIMIT_MAX = 30

app = FastAPI(title="PcLun Online", version="0.2.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

_lock = Lock()
_recent: "OrderedDict[str, float]" = OrderedDict()
_rate_buckets: "dict[str, list[float]]" = defaultdict(list)
_peak_today = 0
_peak_today_ymd = ""


def _db_connect() -> sqlite3.Connection:
    os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
    conn = sqlite3.connect(DB_PATH, check_same_thread=False, timeout=5.0)
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA synchronous=NORMAL")
    return conn


@contextmanager
def _db():
    conn = _db_connect()
    try:
        yield conn
        conn.commit()
    finally:
        conn.close()


def _init_db() -> None:
    with _db() as c:
        c.executescript(
            """
            CREATE TABLE IF NOT EXISTS players (
                name TEXT PRIMARY KEY,
                first_seen REAL NOT NULL,
                last_seen REAL NOT NULL,
                sessions INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_last_seen ON players(last_seen);
            CREATE TABLE IF NOT EXISTS counters (
                key TEXT PRIMARY KEY,
                value INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS daily_stats (
                ymd TEXT PRIMARY KEY,
                unique_players INTEGER NOT NULL DEFAULT 0,
                peak_online INTEGER NOT NULL DEFAULT 0
            );
            """
        )
        # Migrate legacy total.txt counter once.
        row = c.execute("SELECT value FROM counters WHERE key='total'").fetchone()
        if row is None:
            legacy = 0
            try:
                with open(LEGACY_TOTAL_FILE, "r", encoding="utf-8") as fh:
                    legacy = int(fh.read().strip() or "0")
            except (OSError, ValueError):
                legacy = 0
            c.execute(
                "INSERT OR IGNORE INTO counters(key, value) VALUES('total', ?)",
                (legacy,),
            )


_init_db()


class Heartbeat(BaseModel):
    username: str = Field(min_length=1, max_length=32)


def _prune(now: float) -> None:
    cutoff = now - ONLINE_WINDOW_SECONDS
    while _recent and next(iter(_recent.values())) < cutoff:
        _recent.popitem(last=False)


def _rate_limited(ip: str, now: float) -> bool:
    bucket = _rate_buckets[ip]
    cutoff = now - RATE_LIMIT_WINDOW
    while bucket and bucket[0] < cutoff:
        bucket.pop(0)
    if len(bucket) >= RATE_LIMIT_MAX:
        return True
    bucket.append(now)
    return False


def _today_ymd(now: float) -> str:
    return time.strftime("%Y-%m-%d", time.gmtime(now))


def _bump_player(name: str, now: float) -> int:
    """Insert or update player record. Returns total lifetime players."""
    with _db() as c:
        cur = c.execute(
            """
            INSERT INTO players(name, first_seen, last_seen, sessions)
            VALUES(?, ?, ?, 1)
            ON CONFLICT(name) DO UPDATE SET
                last_seen = excluded.last_seen,
                sessions = sessions + CASE
                    WHEN excluded.last_seen - players.last_seen > ? THEN 1 ELSE 0 END
            """,
            (name, now, now, ONLINE_WINDOW_SECONDS),
        )
        # Update total counter to # of distinct rows in players (cheap, table is tiny).
        c.execute(
            "INSERT INTO counters(key, value) VALUES('total', (SELECT COUNT(*) FROM players)) "
            "ON CONFLICT(key) DO UPDATE SET value=(SELECT COUNT(*) FROM players)"
        )
        # Daily stats.
        ymd = _today_ymd(now)
        c.execute(
            "INSERT INTO daily_stats(ymd, unique_players, peak_online) "
            "VALUES(?, 1, 0) ON CONFLICT(ymd) DO NOTHING",
            (ymd,),
        )
        # Peak online for today.
        c.execute(
            "UPDATE daily_stats SET peak_online = MAX(peak_online, ?) WHERE ymd = ?",
            (len(_recent), ymd),
        )
        row = c.execute("SELECT value FROM counters WHERE key='total'").fetchone()
        return int(row[0]) if row else 0


@app.post("/heartbeat")
def heartbeat(beat: Heartbeat, request: Request) -> dict:
    ip = (request.client.host if request.client else "?") or "?"
    now = time.time()
    name = beat.username.strip()[:32] or "Гость"

    with _lock:
        if _rate_limited(ip, now):
            raise HTTPException(status_code=429, detail="rate limit")
        _prune(now)
        _recent[name] = now
        _recent.move_to_end(name)
        total = _bump_player(name, now)
        return {"online": len(_recent), "total": total}


@app.get("/online")
def online() -> dict:
    now = time.time()
    with _lock:
        _prune(now)
        with _db() as c:
            row = c.execute("SELECT value FROM counters WHERE key='total'").fetchone()
            total = int(row[0]) if row else 0
        return {"online": len(_recent), "total": total}


@app.get("/stats")
def stats() -> dict:
    now = time.time()
    ymd = _today_ymd(now)
    # Start of UTC day.
    day_start = time.mktime(time.strptime(ymd, "%Y-%m-%d")) - time.timezone
    with _lock:
        _prune(now)
        with _db() as c:
            row = c.execute("SELECT value FROM counters WHERE key='total'").fetchone()
            total = int(row[0]) if row else 0
            today_row = c.execute(
                "SELECT COUNT(*) FROM players WHERE last_seen >= ?", (day_start,)
            ).fetchone()
            peak_row = c.execute(
                "SELECT peak_online FROM daily_stats WHERE ymd=?", (ymd,)
            ).fetchone()
        return {
            "online": len(_recent),
            "total": total,
            "today": int(today_row[0]) if today_row else 0,
            "peak": int(peak_row[0]) if peak_row else len(_recent),
        }


# ---------- /news ----------

# News are kept in-memory; can be edited by setting PCLUN_NEWS env (JSON array).
_DEFAULT_NEWS = [
    {
        "title": "PcLun 0.3 — много нового",
        "body": (
            "Авто-обновление, менеджер модов и шейдеров, бэкапы миров, "
            "анализатор краш-логов, Discord Rich Presence, скин-вьювер."
        ),
        "date": "2026-04-29",
        "tag": "release",
    },
    {
        "title": "Профиль Potato 🥔",
        "body": "Самый агрессивный профиль для совсем слабых ПК — render distance 2, всё анимированное выкл.",
        "date": "2026-03-15",
        "tag": "tip",
    },
    {
        "title": "OptiFine HD U G8",
        "body": "Лаунчер автоматически ставит OptiFine с optifine.net, fallback на mirror.",
        "date": "2026-02-20",
        "tag": "info",
    },
]


def _load_news() -> list[dict]:
    raw = os.environ.get("PCLUN_NEWS")
    if raw:
        try:
            data = json.loads(raw)
            if isinstance(data, list):
                return data
        except json.JSONDecodeError:
            pass
    return _DEFAULT_NEWS


@app.get("/news")
def news() -> dict:
    return {"items": _load_news()}


# ---------- /servers/featured ----------

_DEFAULT_SERVERS = [
    {"name": "Hypixel", "address": "play.hypixel.net", "tag": "minigames"},
    {"name": "CubeCraft", "address": "play.cubecraft.net", "tag": "minigames"},
    {"name": "Mineplex", "address": "hub.mineplex.com", "tag": "minigames"},
    {"name": "PurplePrison", "address": "play.purpleprison.net", "tag": "prison"},
    {"name": "Pika Network", "address": "play.pika-network.net", "tag": "mixed"},
    {"name": "Complex Gaming", "address": "mc.complex-gaming.net", "tag": "pixelmon"},
    {"name": "CraftRise", "address": "play.craftrise.com.tr", "tag": "tr"},
]


def _load_servers() -> list[dict]:
    raw = os.environ.get("PCLUN_SERVERS")
    if raw:
        try:
            data = json.loads(raw)
            if isinstance(data, list):
                return data
        except json.JSONDecodeError:
            pass
    return _DEFAULT_SERVERS


@app.get("/servers/featured")
def featured_servers() -> dict:
    return {"items": _load_servers()}


# ---------- /leaderboard ----------


@app.get("/leaderboard")
def leaderboard(limit: int = 20) -> dict:
    limit = max(1, min(100, limit))
    with _lock, _db() as c:
        rows = c.execute(
            "SELECT name, sessions, last_seen FROM players "
            "ORDER BY sessions DESC, last_seen DESC LIMIT ?",
            (limit,),
        ).fetchall()
    return {
        "items": [
            {"name": r[0], "sessions": int(r[1]), "last_seen": float(r[2])}
            for r in rows
        ]
    }


@app.get("/")
def root() -> dict:
    return {"service": "pclun-online", "version": "0.2.0", "ok": True}

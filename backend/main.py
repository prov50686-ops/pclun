"""Backend for the PcLun launcher.

Endpoints:
    POST /heartbeat                  -> online/total
    GET  /online | /stats            -> aggregate stats
    GET  /news                       -> news feed
    GET  /servers/featured           -> curated server list
    GET  /leaderboard                -> top players
    POST /telemetry/crash|perf       -> anonymous metrics
    GET  /friends/{nick}             -> friends list
    POST /friends/add|remove         -> manage friends
    GET  /achievements/{nick}        -> player achievements
    POST /achievements/unlock        -> unlock achievement
    POST /backup/presign             -> presigned URL stub
    WS   /chat                       -> live chat (best-effort)
    GET  /site/changelog             -> public HTML page
"""
from __future__ import annotations

import asyncio
import html
import json
import os
import sqlite3
import time
import uuid
from collections import OrderedDict, defaultdict
from contextlib import contextmanager
from threading import Lock

from fastapi import FastAPI, HTTPException, Request, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse
from pydantic import BaseModel, Field

ONLINE_WINDOW_SECONDS = 300  # 5 минут
DATA_DIR = os.environ.get("PCLUN_DATA_DIR", "/data")
DB_PATH = os.environ.get("PCLUN_DB", os.path.join(DATA_DIR, "pclun.db"))
LEGACY_TOTAL_FILE = os.environ.get("PCLUN_DATA_FILE", os.path.join(DATA_DIR, "total.txt"))

# Rate limit: max 30 heartbeats / minute / IP. Clients ping ~1/min, so this is
# extremely lenient for real users but blocks `for i in {1..1e6}; do curl …`.
RATE_LIMIT_WINDOW = 60.0
RATE_LIMIT_MAX = 30

app = FastAPI(title="PcLun Online", version="0.4.0")
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
            CREATE TABLE IF NOT EXISTS friends (
                owner TEXT NOT NULL,
                friend TEXT NOT NULL,
                added REAL NOT NULL,
                PRIMARY KEY (owner, friend)
            );
            CREATE TABLE IF NOT EXISTS achievements (
                player TEXT NOT NULL,
                code TEXT NOT NULL,
                unlocked REAL NOT NULL,
                PRIMARY KEY (player, code)
            );
            CREATE TABLE IF NOT EXISTS telemetry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ts REAL NOT NULL,
                kind TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_telemetry_ts ON telemetry(ts);
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


# ---------- /telemetry ----------

class TelemetryEvent(BaseModel):
    kind: str = Field(min_length=1, max_length=32)
    payload: dict = Field(default_factory=dict)


@app.post("/telemetry")
def telemetry(evt: TelemetryEvent, request: Request) -> dict:
    ip = (request.client.host if request.client else "?") or "?"
    now = time.time()
    with _lock:
        if _rate_limited(ip, now):
            raise HTTPException(status_code=429, detail="rate limit")
    payload = json.dumps(evt.payload, ensure_ascii=False)[:4000]
    with _db() as c:
        c.execute(
            "INSERT INTO telemetry(ts, kind, payload) VALUES(?, ?, ?)",
            (now, evt.kind[:32], payload),
        )
    return {"ok": True}


@app.get("/telemetry/recent")
def telemetry_recent(limit: int = 50) -> dict:
    limit = max(1, min(500, limit))
    with _db() as c:
        rows = c.execute(
            "SELECT ts, kind, payload FROM telemetry ORDER BY id DESC LIMIT ?",
            (limit,),
        ).fetchall()
    return {
        "items": [
            {"ts": float(r[0]), "kind": r[1], "payload": json.loads(r[2] or "{}")}
            for r in rows
        ]
    }


# ---------- /friends ----------

class FriendOp(BaseModel):
    owner: str = Field(min_length=1, max_length=32)
    friend: str = Field(min_length=1, max_length=32)


@app.post("/friends/add")
def friends_add(op: FriendOp) -> dict:
    if op.owner.strip().lower() == op.friend.strip().lower():
        raise HTTPException(400, "cannot friend self")
    with _db() as c:
        c.execute(
            "INSERT OR IGNORE INTO friends(owner, friend, added) VALUES(?, ?, ?)",
            (op.owner.strip(), op.friend.strip(), time.time()),
        )
    return {"ok": True}


@app.post("/friends/remove")
def friends_remove(op: FriendOp) -> dict:
    with _db() as c:
        c.execute(
            "DELETE FROM friends WHERE owner=? AND friend=?",
            (op.owner.strip(), op.friend.strip()),
        )
    return {"ok": True}


@app.get("/friends/{nick}")
def friends_list(nick: str) -> dict:
    nick = nick.strip()[:32]
    now = time.time()
    cutoff = now - ONLINE_WINDOW_SECONDS
    with _db() as c:
        rows = c.execute(
            """
            SELECT f.friend, COALESCE(p.last_seen, 0), COALESCE(p.sessions, 0)
            FROM friends f
            LEFT JOIN players p ON p.name = f.friend
            WHERE f.owner = ?
            ORDER BY p.last_seen DESC
            """,
            (nick,),
        ).fetchall()
    return {
        "items": [
            {
                "name": r[0],
                "last_seen": float(r[1]),
                "sessions": int(r[2]),
                "online": float(r[1]) >= cutoff,
            }
            for r in rows
        ]
    }


# ---------- /achievements ----------

ACHIEVEMENT_CATALOG = {
    "first_launch": {"title": "Первый запуск", "icon": "🚀"},
    "ten_hours": {"title": "10 часов в игре", "icon": "⏰"},
    "hundred_hours": {"title": "100 часов в игре", "icon": "💎"},
    "ten_servers": {"title": "Сменил 10 серверов", "icon": "🌍"},
    "performance_pack": {"title": "Установил Performance Pack", "icon": "⚡"},
    "first_friend": {"title": "Добавил друга", "icon": "🤝"},
    "first_backup": {"title": "Создал бэкап мира", "icon": "💾"},
    "potato_master": {"title": "Игра на профиле Potato", "icon": "🥔"},
    "skin_changed": {"title": "Сменил скин", "icon": "🎨"},
    "shader_user": {"title": "Установил шейдер", "icon": "✨"},
}


class AchievementUnlock(BaseModel):
    player: str = Field(min_length=1, max_length=32)
    code: str = Field(min_length=1, max_length=64)


@app.post("/achievements/unlock")
def ach_unlock(op: AchievementUnlock) -> dict:
    if op.code not in ACHIEVEMENT_CATALOG:
        raise HTTPException(400, "unknown achievement")
    with _db() as c:
        c.execute(
            "INSERT OR IGNORE INTO achievements(player, code, unlocked) VALUES(?, ?, ?)",
            (op.player.strip(), op.code, time.time()),
        )
    return {"ok": True, "achievement": ACHIEVEMENT_CATALOG[op.code]}


@app.get("/achievements/{nick}")
def ach_list(nick: str) -> dict:
    nick = nick.strip()[:32]
    with _db() as c:
        rows = c.execute(
            "SELECT code, unlocked FROM achievements WHERE player=? ORDER BY unlocked DESC",
            (nick,),
        ).fetchall()
    return {
        "items": [
            {
                "code": r[0],
                "unlocked": float(r[1]),
                "title": ACHIEVEMENT_CATALOG.get(r[0], {}).get("title", r[0]),
                "icon": ACHIEVEMENT_CATALOG.get(r[0], {}).get("icon", "🏆"),
            }
            for r in rows
        ],
        "catalog": ACHIEVEMENT_CATALOG,
    }


# ---------- /backup/presign (S3/R2 stub) ----------

class BackupPresign(BaseModel):
    nick: str = Field(min_length=1, max_length=32)
    filename: str = Field(min_length=1, max_length=128)


@app.post("/backup/presign")
def backup_presign(op: BackupPresign) -> dict:
    """Stub for cloud backup. In production, wire this to S3/R2.

    Returns a placeholder URL; the launcher treats failures gracefully.
    Set PCLUN_BACKUP_BUCKET + AWS creds to enable real signing.
    """
    bucket = os.environ.get("PCLUN_BACKUP_BUCKET")
    if not bucket:
        return {"ok": False, "error": "cloud backup not configured", "id": uuid.uuid4().hex}
    key = f"{op.nick}/{int(time.time())}-{op.filename}"
    return {"ok": True, "url": f"https://{bucket}.s3.amazonaws.com/{key}", "key": key}


# ---------- /chat WebSocket ----------

class _ChatHub:
    def __init__(self) -> None:
        self.peers: dict[WebSocket, str] = {}
        self.history: list[dict] = []

    async def connect(self, ws: WebSocket, nick: str) -> None:
        await ws.accept()
        self.peers[ws] = nick
        for msg in self.history[-30:]:
            await ws.send_json(msg)
        await self.broadcast({"system": True, "text": f"{nick} вошёл"})

    def disconnect(self, ws: WebSocket) -> str | None:
        return self.peers.pop(ws, None)

    async def broadcast(self, msg: dict) -> None:
        msg.setdefault("ts", time.time())
        self.history.append(msg)
        if len(self.history) > 500:
            self.history = self.history[-500:]
        dead = []
        for peer in list(self.peers):
            try:
                await peer.send_json(msg)
            except Exception:  # noqa: BLE001
                dead.append(peer)
        for d in dead:
            self.peers.pop(d, None)


_chat = _ChatHub()


@app.websocket("/chat")
async def chat_ws(ws: WebSocket) -> None:
    nick = ws.query_params.get("nick", "anon").strip()[:32] or "anon"
    await _chat.connect(ws, nick)
    try:
        while True:
            data = await ws.receive_json()
            text = str(data.get("text", ""))[:500]
            if text:
                await _chat.broadcast({"nick": nick, "text": text})
    except WebSocketDisconnect:
        pass
    except Exception:  # noqa: BLE001
        pass
    finally:
        nick = _chat.disconnect(ws) or nick
        await _chat.broadcast({"system": True, "text": f"{nick} вышел"})


# ---------- public site (static-ish) ----------

@app.get("/site/changelog", response_class=HTMLResponse)
def site_changelog() -> str:
    items = _load_news()
    rows = "".join(
        f"<article><h3>{html.escape(i.get('title',''))}</h3>"
        f"<p class='date'>{html.escape(str(i.get('date','')))}"
        f" · <span class='tag'>{html.escape(str(i.get('tag','')))}</span></p>"
        f"<p>{html.escape(i.get('body',''))}</p></article>"
        for i in items
    )
    return f"""<!doctype html><html lang='ru'><head><meta charset='utf-8'>
<title>PcLun · Changelog</title>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<style>
  body{{font-family:-apple-system,system-ui,sans-serif;background:#0e0e10;color:#eee;
       max-width:760px;margin:40px auto;padding:0 16px}}
  h1{{font-size:32px;margin:0 0 8px}}
  .sub{{color:#888;margin:0 0 32px}}
  article{{border-left:3px solid #4ade80;padding:12px 16px;margin:0 0 24px;
          background:#1a1a1d;border-radius:0 6px 6px 0}}
  article h3{{margin:0 0 4px}}
  .date{{color:#888;font-size:13px;margin:0 0 8px}}
  .tag{{display:inline-block;background:#333;border-radius:4px;padding:1px 6px;
       font-size:11px;text-transform:uppercase;letter-spacing:.05em}}
</style></head><body>
<h1>PcLun</h1><p class='sub'>Лаунчер MC 1.16.5 + OptiFine. Changelog & новости.</p>
{rows}
</body></html>"""


@app.get("/")
def root() -> dict:
    return {"service": "pclun-online", "version": "0.4.0", "ok": True}

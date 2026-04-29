# pclun-online

Минимальный FastAPI-бэкенд счётчика онлайна для лаунчера PcLun.

## Запуск локально

```bash
cd backend
pip install -e .
uvicorn main:app --host 0.0.0.0 --port 8000
```

## Эндпоинты

- `POST /heartbeat` `{username}` → `{online, total}` — пинг (rate limit: 30/мин/IP).
- `GET  /online` → `{online, total}`.
- `GET  /stats` → `{online, total, today, peak}` — сегодняшняя уникальная активность и пик.
- `GET  /news` → лента новостей (берётся из `PCLUN_NEWS` env, JSON-массив, или из дефолта).
- `GET  /servers/featured` → рекомендуемые серверы (env `PCLUN_SERVERS` или дефолт).
- `GET  /leaderboard?limit=20` → топ ников по числу сессий.

## Persistence

Данные хранятся в SQLite по пути `PCLUN_DB` (по умолчанию `/data/pclun.db`).
На Fly.io подмонтируйте Volume в `/data` (см. `fly volumes create pclun_data`).
При первом запуске старый `total.txt` мигрируется в SQLite автоматически.

## Деплой

Поддерживается Fly.io / Railway / Render. На бесплатном плане Fly.io используется `Dockerfile`.

```bash
fly volumes create pclun_data --region waw --size 1
fly deploy
```

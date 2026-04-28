# pclun-online

Минимальный FastAPI-бэкенд счётчика онлайна для лаунчера PcLun.

## Запуск локально

```bash
cd backend
pip install -e .
uvicorn main:app --host 0.0.0.0 --port 8000
```

## Деплой

Поддерживается Fly.io / Railway / Render. На бесплатном плане Fly.io используется `Dockerfile`.

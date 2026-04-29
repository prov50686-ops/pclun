# PcLun public site

Минимальный статический сайт под Cloudflare Pages. Деплой:

```bash
npx wrangler pages deploy packaging/cloudflare-pages --project-name pclun
```

Содержимое:
- `index.html` — лендинг с кнопкой скачать и встроенным live-changelog (iframe в бэкенд `/site/changelog`).

# Iteration 1 Scope

Delivered baseline skeleton:

- Monorepo structure: `backend/`, `admin/`, `launcher/`, `deploy/`, `docs/`
- Backend API (.NET 8 + EF Core + PostgreSQL)
  - `POST /api/admin/setup`
  - `POST /api/admin/login`
  - `GET /api/admin/setup/status`
  - `GET /api/public/bootstrap`
- Branding layer via `backend/BivLauncher.Api/branding.json`
- Admin web stub (React + Vite + TypeScript): setup/login/dashboard screens
- Docker compose stack: api + admin + postgres (+ optional minio profile)
- Installer script: `deploy/installer.sh`

## Next in iteration 2

- CRUD for profiles and servers in admin API/UI
- icon upload to S3 + expose icon URLs in bootstrap

# Iteration 2: Profiles/Servers CRUD + Icon Upload

Delivered:

- Admin API CRUD for profiles:
  - `GET /api/admin/profiles`
  - `GET /api/admin/profiles/{id}`
  - `POST /api/admin/profiles`
  - `PUT /api/admin/profiles/{id}`
  - `DELETE /api/admin/profiles/{id}`
- Admin API CRUD for servers:
  - `GET /api/admin/servers`
  - `GET /api/admin/servers/{id}`
  - `POST /api/admin/servers`
  - `PUT /api/admin/servers/{id}`
  - `DELETE /api/admin/servers/{id}`
- JWT protection on both controllers (`admin` role).
- Admin UI dashboard updated with profile/server management forms and lists.
- Admin upload endpoint:
  - `POST /api/admin/upload?category=profiles|servers|assets|runtimes|news&entityId=...`
- S3/MinIO storage layer (`AWSSDK.S3`) with bucket auto-create support.
- Public asset endpoint:
  - `GET /api/public/assets/{**key}`
- Bootstrap now includes icon URLs:
  - `profiles[].iconUrl`
  - `profiles[].servers[].iconUrl`

Next:

- icon preview widgets in admin UI
- runtime/assets upload flows (beyond icons)

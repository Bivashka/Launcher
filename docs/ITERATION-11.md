# Iteration 11: Hardware/User Bans + Admin Ban Panel

Delivered:

- Added backend bans domain model and migration:
  - `HardwareBan` entity (`accountId` or `hwidHash`, reason, created/expires timestamps)
- Added admin ban API:
  - `GET /api/admin/bans`
  - `POST /api/admin/bans/hwid`
  - `POST /api/admin/bans/account/{user}`
  - `DELETE /api/admin/bans/{id}`
- Public auth login now blocks:
  - active HWID bans
  - active account bans
- Admin React dashboard now includes ban management:
  - create HWID ban
  - create account ban by username/externalId
  - list/delete bans

Migration:

- `AddHardwareBans`

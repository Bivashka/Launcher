# Iteration 63: Audit Time-Range Filters

Delivered:

- Extended admin audit logs API with time-range filters:
  - `fromUtc` (inclusive)
  - `toUtc` (inclusive)
- Added validation for invalid range:
  - returns `400` when `fromUtc > toUtc`
- Admin UI audit panel now supports time-range filtering:
  - `From` and `To` datetime-local inputs
  - values are converted to UTC ISO before API request
- Updated API examples with date-range query for audit logs.

Verification:

- `dotnet build backend/BivLauncher.sln`
- `npm run build` (admin)

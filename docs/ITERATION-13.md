# Iteration 13: Per-Profile Route Selection (RU proxy / Main DE)

Delivered:

- Added route config fields for each server profile in backend:
  - main route jar path (`mainJarPath`)
  - RU proxy address/port (`ruProxyAddress`, `ruProxyPort`)
  - RU route jar path (`ruJarPath`)
- Added migration `AddServerRouteConfig`.
- Extended admin server CRUD models and API payloads with route fields.
- Extended admin React server form with route configuration inputs.
- Extended public bootstrap server payload with route config.
- Launcher now supports manual per-profile route selection:
  - `RU server (via proxy)` or `Main server (DE host)`
  - selection is saved locally per profile (`ProfileRouteSelections`)
- Route selection affects:
  - target minecraft jar (`mainJarPath` / `ruJarPath`)
  - target connect address+port (`address:port` / `ruProxyAddress:ruProxyPort`)
- Added route selector to launcher runtime UI and localized labels (RU/EN/UK/KK).

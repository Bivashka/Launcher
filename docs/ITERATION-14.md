# Iteration 14: Auth Provider Settings (Admin + Runtime Usage)

Delivered:

- Added persistent auth provider settings in backend DB (`AuthProviderConfig`):
  - `loginUrl`
  - `timeoutSeconds`
  - `allowDevFallback`
- Added admin API for auth provider settings:
  - `GET /api/admin/settings/auth-provider`
  - `PUT /api/admin/settings/auth-provider`
- Updated `ExternalAuthService` to resolve effective auth settings from DB first,
  then fallback to env/config defaults.
- Switched `IExternalAuthService` registration to scoped and added `IHttpClientFactory`.
- Added admin dashboard UI section for editing auth provider settings.
- Added migration `AddAuthProviderConfig`.

# Iteration 7: Admin UI for Skins/Capes

Delivered:

- Admin dashboard now includes a `Skins / Capes` block.
- Supports upload by `username` or `externalId`:
  - `POST /api/admin/skins/{user}/upload`
  - `POST /api/admin/capes/{user}/upload`
- Upload status is shown in existing notice/error area.

Validation behavior:

- requires selected file
- requires non-empty user identifier
- shows backend validation messages (account not found, bad extension, etc.)

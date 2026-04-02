#!/usr/bin/env bash
set -euo pipefail

log() {
  printf '[ru-mirror-sync] %s\n' "$*"
}

fail() {
  log "ERROR: $*"
  exit 1
}

require_command() {
  local command_name="$1"
  command -v "$command_name" >/dev/null 2>&1 || fail "Required command is missing: $command_name"
}

contains_value() {
  local needle="$1"
  shift || true
  local item
  for item in "$@"; do
    if [ "$item" = "$needle" ]; then
      return 0
    fi
  done

  return 1
}

discover_storage_root() {
  local volume_name
  for volume_name in launcher_storage_data launcher_storage; do
    if docker volume inspect "$volume_name" --format '{{ .Mountpoint }}' >/dev/null 2>&1; then
      docker volume inspect "$volume_name" --format '{{ .Mountpoint }}'
      return 0
    fi
  done

  return 1
}

sync_directory_if_exists() {
  local source_directory="$1"
  local target_directory="$2"

  if [ ! -d "$source_directory" ]; then
    log "Skipping missing directory: $source_directory"
    return 0
  fi

  log "Syncing ${source_directory#$STORAGE_ROOT/} -> ${target_directory#$RU_MIRROR_PATH/}"
  "${SSH_COMMAND[@]}" "mkdir -p \"$target_directory\""
  rsync -az --delete \
    --chown=www-data:www-data \
    --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r \
    -e "$RSYNC_SSH_COMMAND" \
    "$source_directory/" \
    "$RU_MIRROR_TARGET:$target_directory/"
}

prune_remote_client_builds() {
  local profile_slug="$1"
  shift || true
  local active_build_ids=("$@")
  local remote_profile_root="$RU_MIRROR_PATH/clients/$profile_slug"
  local remote_build_ids

  remote_build_ids="$("${SSH_COMMAND[@]}" "if [ -d \"$remote_profile_root\" ]; then find \"$remote_profile_root\" -mindepth 1 -maxdepth 1 -type d -printf '%f\n'; fi" || true)"
  while IFS= read -r remote_build_id; do
    [ -n "$remote_build_id" ] || continue
    if contains_value "$remote_build_id" "${active_build_ids[@]}"; then
      continue
    fi

    log "Pruning stale remote build: clients/$profile_slug/$remote_build_id"
    "${SSH_COMMAND[@]}" "rm -rf \"$remote_profile_root/$remote_build_id\""
  done <<< "$remote_build_ids"
}

require_command curl
require_command jq
require_command rsync
require_command ssh

LOCAL_API_BASE="${LOCAL_API_BASE:-http://127.0.0.1:8080}"
RU_MIRROR_HOST="${RU_MIRROR_HOST:-}"
RU_MIRROR_USER="${RU_MIRROR_USER:-root}"
RU_MIRROR_PATH="${RU_MIRROR_PATH:-/srv/launcher-assets}"
RU_MIRROR_SSH_KEY="${RU_MIRROR_SSH_KEY:-/root/.ssh/launcher_ru_mirror}"
PRUNE_CLIENT_BUILDS="${PRUNE_CLIENT_BUILDS:-1}"

if [ -z "$RU_MIRROR_HOST" ]; then
  fail "RU_MIRROR_HOST is required."
fi

if [ ! -f "$RU_MIRROR_SSH_KEY" ]; then
  fail "SSH key does not exist: $RU_MIRROR_SSH_KEY"
fi

STORAGE_ROOT="${STORAGE_ROOT:-$(discover_storage_root || true)}"
STORAGE_ROOT="${STORAGE_ROOT%/}"
[ -n "$STORAGE_ROOT" ] || fail "STORAGE_ROOT is empty and auto-discovery failed."
[ -d "$STORAGE_ROOT" ] || fail "STORAGE_ROOT directory does not exist: $STORAGE_ROOT"

RU_MIRROR_TARGET="${RU_MIRROR_USER}@${RU_MIRROR_HOST}"
RSYNC_SSH_COMMAND="ssh -i $RU_MIRROR_SSH_KEY -o StrictHostKeyChecking=no"
SSH_COMMAND=(ssh -i "$RU_MIRROR_SSH_KEY" -o StrictHostKeyChecking=no "$RU_MIRROR_TARGET")

BOOTSTRAP_JSON="$(mktemp)"
trap 'rm -f "$BOOTSTRAP_JSON"' EXIT

log "Fetching bootstrap from $LOCAL_API_BASE"
curl -fsSL "$LOCAL_API_BASE/api/public/bootstrap" -o "$BOOTSTRAP_JSON"

mapfile -t profile_slugs < <(jq -r '.profiles[]?.slug // empty' "$BOOTSTRAP_JSON" | sort -u)
mapfile -t profile_build_pairs < <(
  jq -r '.profiles[]? as $profile | ($profile.servers // [])[]? | select(.buildId != null and .buildId != "") | "\($profile.slug)\t\(.buildId)"' "$BOOTSTRAP_JSON" |
  sort -u
)

log "Ensuring remote mirror root exists: $RU_MIRROR_PATH"
"${SSH_COMMAND[@]}" "mkdir -p \"$RU_MIRROR_PATH\""

sync_directory_if_exists "$STORAGE_ROOT/branding" "$RU_MIRROR_PATH/branding"
sync_directory_if_exists "$STORAGE_ROOT/uploads" "$RU_MIRROR_PATH/uploads"
sync_directory_if_exists "$STORAGE_ROOT/icons" "$RU_MIRROR_PATH/icons"
sync_directory_if_exists "$STORAGE_ROOT/launcher-updates" "$RU_MIRROR_PATH/launcher-updates"

if [ "${#profile_slugs[@]}" -eq 0 ]; then
  log "No enabled profiles found in bootstrap."
else
  log "Profiles from bootstrap: ${profile_slugs[*]}"
fi

for profile_slug in "${profile_slugs[@]}"; do
  [ -n "$profile_slug" ] || continue
  sync_directory_if_exists "$STORAGE_ROOT/manifests/$profile_slug" "$RU_MIRROR_PATH/manifests/$profile_slug"

  mapfile -t active_build_ids < <(
    printf '%s\n' "${profile_build_pairs[@]}" |
    awk -F '\t' -v profile_slug="$profile_slug" '$1 == profile_slug { print $2 }' |
    sort -u
  )

  if [ "${#active_build_ids[@]}" -eq 0 ]; then
    log "No active build IDs found for profile '$profile_slug'."
    continue
  fi

  for build_id in "${active_build_ids[@]}"; do
    [ -n "$build_id" ] || continue
    sync_directory_if_exists "$STORAGE_ROOT/clients/$profile_slug/$build_id" "$RU_MIRROR_PATH/clients/$profile_slug/$build_id"
  done

  if [ "$PRUNE_CLIENT_BUILDS" = "1" ] || [ "$PRUNE_CLIENT_BUILDS" = "true" ] || [ "$PRUNE_CLIENT_BUILDS" = "yes" ]; then
    prune_remote_client_builds "$profile_slug" "${active_build_ids[@]}"
  fi
done

log "RU mirror sync completed successfully."

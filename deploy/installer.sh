#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

ENV_FILE=".env"
NON_INTERACTIVE="${BLP_INSTALLER_NON_INTERACTIVE:-0}"
USE_MINIO_OVERRIDE=""
HOST_OVERRIDE=""
API_PORT_OVERRIDE=""
ADMIN_PORT_OVERRIDE=""
SSL_OVERRIDE=""
SKIP_HEALTH_CHECK="0"
SKIP_PORT_CHECK="0"
SKIP_COMPOSE_CONFIG_CHECK="0"
STRICT_CHECK="0"
DRY_RUN="0"
NO_PUBLIC_IP="0"
ENABLE_ENV_BACKUP="1"
ENV_ROLLBACK_ON_FAIL="0"
HEALTH_RETRIES_OVERRIDE=""
HEALTH_TIMEOUT_OVERRIDE=""
HEALTH_DELAY_OVERRIDE=""
OUTPUT_JSON_PATH=""
ADMIN_SETUP_MODE="prompt"
ADMIN_USERNAME_OVERRIDE=""
ADMIN_PASSWORD_OVERRIDE=""
ENV_CHANGES_JSON=""
GENERATED_SECRETS_JSON=""
CHECKS_JSON=""
FAILED_CHECKS_TOTAL=0
HEALTH_CHECKS_RAN="false"
HEALTH_CHECKS_SKIPPED="false"
COMPOSE_UP_COMMAND=""
FINAL_EXIT_CODE=0
FINAL_STATUS="success"
FINAL_ERROR_CODE=""

CHECKS_PASSED_TOTAL=0
CHECKS_FAILED_TOTAL=0
CHECKS_SKIPPED_TOTAL=0

CHECKS_SERVICE_PASSED=0
CHECKS_SERVICE_FAILED=0
CHECKS_SERVICE_SKIPPED=0
CHECKS_HTTP_PASSED=0
CHECKS_HTTP_FAILED=0
CHECKS_HTTP_SKIPPED=0
CHECKS_META_PASSED=0
CHECKS_META_FAILED=0
CHECKS_META_SKIPPED=0

USE_MINIO_PROFILE="false"
API_PORT="0"
ADMIN_PORT="0"
PUBLIC_HOST=""
URL_SCHEME=""
API_PUBLIC_URL=""
ADMIN_PUBLIC_URL=""
PUBLIC_IP=""
POSTGRES_PORT_VALUE=0
MINIO_PORT_VALUE=0
MINIO_CONSOLE_PORT_VALUE=0
ENV_FILE_EXISTED_AT_START="false"
ENV_BACKUP_DONE="0"
ENV_BACKUP_PATH=""
ENV_BACKUP_CREATED="false"
ENV_ROLLBACK_APPLIED="false"
HEALTH_RETRIES=30
HEALTH_TIMEOUT_SECONDS=3
HEALTH_RETRY_DELAY_SECONDS=2
ADMIN_SETUP_ATTEMPTED="false"
ADMIN_SETUP_CREATED="false"
ADMIN_SETUP_USERNAME=""
ADMIN_SETUP_SKIP_REASON=""

print_usage() {
  cat <<'EOF'
Usage: deploy/installer.sh [options]

Options:
  --non-interactive         Do not ask questions, use provided flags/.env defaults.
  --with-minio              Start docker compose with --profile minio.
  --host <host>             Public host/domain for generated URLs.
  --api-port <port>         API port.
  --admin-port <port>       Admin UI port.
  --ssl <true|false>        Use https or http scheme in generated URLs.
  --skip-health-check       Skip post-start HTTP checks.
  --skip-port-check         Skip local port availability pre-check before compose up.
  --skip-compose-config-check
                            Skip `docker compose config -q` preflight validation.
  --strict-check            Exit with non-zero code when any post-start check fails.
  --dry-run                 Print planned changes/commands without writing or starting services.
  --no-env-backup           Disable backup creation for existing env file before modifications.
  --rollback-env-on-fail    Restore env file from backup when installer fails after writes.
  --no-public-ip            Disable external public IP lookup.
  --health-retries <n>      Health-check retries (default: 30).
  --health-timeout <sec>    Health-check request timeout in seconds (default: 3).
  --health-delay <sec>      Delay between health-check retries in seconds (default: 2).
  --env-file <path>         Use custom env file path instead of .env.
  --setup-admin             Try to create first admin account after stack startup.
  --skip-admin-setup        Skip admin setup flow (including interactive prompt).
  --admin-user <username>   Admin username for setup flow.
  --admin-password <pass>   Admin password for setup flow (or use BLP_ADMIN_PASSWORD env var).
  --output-json <path|->    Write final installer report as JSON (use '-' for stdout).
  --help                    Show this help.
EOF
}

normalize_bool() {
  local raw="${1:-}"
  local lowered="${raw,,}"

  case "$lowered" in
    1|true|yes|y|on)
      echo "true"
      ;;
    0|false|no|n|off|"")
      echo "false"
      ;;
    *)
      echo ""
      ;;
  esac
}

parse_args() {
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --non-interactive)
        NON_INTERACTIVE="1"
        shift
        ;;
      --with-minio)
        USE_MINIO_OVERRIDE="true"
        shift
        ;;
      --host)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-host" "Option --host requires a value."
        fi
        HOST_OVERRIDE="$2"
        shift 2
        ;;
      --api-port)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-api-port" "Option --api-port requires a value."
        fi
        API_PORT_OVERRIDE="$2"
        shift 2
        ;;
      --admin-port)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-admin-port" "Option --admin-port requires a value."
        fi
        ADMIN_PORT_OVERRIDE="$2"
        shift 2
        ;;
      --ssl)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-ssl" "Option --ssl requires a value."
        fi
        SSL_OVERRIDE="$2"
        shift 2
        ;;
      --skip-health-check)
        SKIP_HEALTH_CHECK="1"
        shift
        ;;
      --skip-port-check)
        SKIP_PORT_CHECK="1"
        shift
        ;;
      --skip-compose-config-check)
        SKIP_COMPOSE_CONFIG_CHECK="1"
        shift
        ;;
      --strict-check)
        STRICT_CHECK="1"
        shift
        ;;
      --dry-run)
        DRY_RUN="1"
        shift
        ;;
      --no-env-backup)
        ENABLE_ENV_BACKUP="0"
        shift
        ;;
      --rollback-env-on-fail)
        ENV_ROLLBACK_ON_FAIL="1"
        shift
        ;;
      --no-public-ip)
        NO_PUBLIC_IP="1"
        shift
        ;;
      --health-retries)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-health-retries" "Option --health-retries requires a value."
        fi
        HEALTH_RETRIES_OVERRIDE="$2"
        shift 2
        ;;
      --health-timeout)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-health-timeout" "Option --health-timeout requires a value."
        fi
        HEALTH_TIMEOUT_OVERRIDE="$2"
        shift 2
        ;;
      --health-delay)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-health-delay" "Option --health-delay requires a value."
        fi
        HEALTH_DELAY_OVERRIDE="$2"
        shift 2
        ;;
      --env-file)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-env-file" "Option --env-file requires a value."
        fi
        ENV_FILE="$2"
        shift 2
        ;;
      --setup-admin)
        ADMIN_SETUP_MODE="force"
        shift
        ;;
      --skip-admin-setup)
        ADMIN_SETUP_MODE="skip"
        shift
        ;;
      --admin-user)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-admin-user" "Option --admin-user requires a value."
        fi
        ADMIN_USERNAME_OVERRIDE="$2"
        shift 2
        ;;
      --admin-password)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-admin-password" "Option --admin-password requires a value."
        fi
        ADMIN_PASSWORD_OVERRIDE="$2"
        shift 2
        ;;
      --output-json)
        if [ "$#" -lt 2 ]; then
          fail_now 10 "missing-arg-output-json" "Option --output-json requires a value."
        fi
        OUTPUT_JSON_PATH="$2"
        shift 2
        ;;
      --help|-h)
        print_usage
        exit 0
        ;;
      *)
        print_usage
        fail_now 11 "unknown-option" "Unknown option: $1"
        ;;
    esac
  done
}

json_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\n'/\\n}"
  value="${value//$'\r'/\\r}"
  value="${value//$'\t'/\\t}"
  printf '%s' "$value"
}

json_bool() {
  if [ "${1:-false}" = "true" ] || [ "${1:-0}" = "1" ]; then
    printf '%s' "true"
  else
    printf '%s' "false"
  fi
}

append_env_change() {
  local key="$1"
  local value="$2"
  local report_value="$value"
  local item

  case "$key" in
    JWT_SECRET|HWID_HMAC_SALT|S3_SECRET_KEY)
      report_value="<redacted>"
      ;;
  esac

  item="{\"key\":\"$(json_escape "$key")\",\"value\":\"$(json_escape "$report_value")\"}"

  if [ -n "$ENV_CHANGES_JSON" ]; then
    ENV_CHANGES_JSON="${ENV_CHANGES_JSON},${item}"
  else
    ENV_CHANGES_JSON="$item"
  fi
}

append_generated_secret() {
  local key="$1"
  local item
  item="\"$(json_escape "$key")\""

  if [ -n "$GENERATED_SECRETS_JSON" ]; then
    GENERATED_SECRETS_JSON="${GENERATED_SECRETS_JSON},${item}"
  else
    GENERATED_SECRETS_JSON="$item"
  fi
}

append_check_result() {
  local kind="$1"
  local name="$2"
  local target="$3"
  local status="$4"
  local item

  item="{\"kind\":\"$(json_escape "$kind")\",\"name\":\"$(json_escape "$name")\",\"target\":\"$(json_escape "$target")\",\"status\":\"$(json_escape "$status")\"}"
  if [ -n "$CHECKS_JSON" ]; then
    CHECKS_JSON="${CHECKS_JSON},${item}"
  else
    CHECKS_JSON="$item"
  fi

  case "$status" in
    passed*)
      CHECKS_PASSED_TOTAL=$((CHECKS_PASSED_TOTAL + 1))
      ;;
    failed*)
      CHECKS_FAILED_TOTAL=$((CHECKS_FAILED_TOTAL + 1))
      ;;
    skipped*)
      CHECKS_SKIPPED_TOTAL=$((CHECKS_SKIPPED_TOTAL + 1))
      ;;
  esac

  case "$kind" in
    service)
      case "$status" in
        passed*) CHECKS_SERVICE_PASSED=$((CHECKS_SERVICE_PASSED + 1)) ;;
        failed*) CHECKS_SERVICE_FAILED=$((CHECKS_SERVICE_FAILED + 1)) ;;
        skipped*) CHECKS_SERVICE_SKIPPED=$((CHECKS_SERVICE_SKIPPED + 1)) ;;
      esac
      ;;
    http)
      case "$status" in
        passed*) CHECKS_HTTP_PASSED=$((CHECKS_HTTP_PASSED + 1)) ;;
        failed*) CHECKS_HTTP_FAILED=$((CHECKS_HTTP_FAILED + 1)) ;;
        skipped*) CHECKS_HTTP_SKIPPED=$((CHECKS_HTTP_SKIPPED + 1)) ;;
      esac
      ;;
    meta)
      case "$status" in
        passed*) CHECKS_META_PASSED=$((CHECKS_META_PASSED + 1)) ;;
        failed*) CHECKS_META_FAILED=$((CHECKS_META_FAILED + 1)) ;;
        skipped*) CHECKS_META_SKIPPED=$((CHECKS_META_SKIPPED + 1)) ;;
      esac
      ;;
  esac
}

write_json_report() {
  if [ -z "$OUTPUT_JSON_PATH" ]; then
    return
  fi

  local timestamp_utc
  local local_host_url
  local public_ip_url_formatted
  local report
  local checks_json
  local env_changes_json
  local generated_secrets_json

  timestamp_utc="$(date -u +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || true)"
  checks_json="${CHECKS_JSON:-}"
  env_changes_json="${ENV_CHANGES_JSON:-}"
  generated_secrets_json="${GENERATED_SECRETS_JSON:-}"

  if [ -z "$checks_json" ]; then
    checks_json=""
  fi
  if [ -z "$env_changes_json" ]; then
    env_changes_json=""
  fi
  if [ -z "$generated_secrets_json" ]; then
    generated_secrets_json=""
  fi

  local_host_url="$(json_escape "${PUBLIC_HOST}")"
  public_ip_url_formatted=""
  if [ -n "${PUBLIC_IP:-}" ]; then
    public_ip_url_formatted="$(json_escape "$PUBLIC_IP")"
  fi

  report="$(cat <<EOF
{
  "timestampUtc": "$(json_escape "$timestamp_utc")",
  "result": {
    "status": "$(json_escape "$FINAL_STATUS")",
    "exitCode": ${FINAL_EXIT_CODE},
    "errorCode": "$(json_escape "$FINAL_ERROR_CODE")"
  },
  "dryRun": $(json_bool "$DRY_RUN"),
  "nonInteractive": $(json_bool "$NON_INTERACTIVE"),
  "strictCheck": $(json_bool "$STRICT_CHECK"),
  "skipHealthCheck": $(json_bool "$SKIP_HEALTH_CHECK"),
  "skipPortCheck": $(json_bool "$SKIP_PORT_CHECK"),
  "skipComposeConfigCheck": $(json_bool "$SKIP_COMPOSE_CONFIG_CHECK"),
  "envBackupEnabled": $(json_bool "$ENABLE_ENV_BACKUP"),
  "envBackupCreated": $(json_bool "$ENV_BACKUP_CREATED"),
  "envRollbackOnFail": $(json_bool "$ENV_ROLLBACK_ON_FAIL"),
  "envRollbackApplied": $(json_bool "$ENV_ROLLBACK_APPLIED"),
  "noPublicIp": $(json_bool "$NO_PUBLIC_IP"),
  "healthChecksRan": $(json_bool "$HEALTH_CHECKS_RAN"),
  "healthChecksSkipped": $(json_bool "$HEALTH_CHECKS_SKIPPED"),
  "failedChecks": ${CHECKS_FAILED_TOTAL},
  "useMinioProfile": $(json_bool "$USE_MINIO_PROFILE"),
  "composeCommand": "$(json_escape "$COMPOSE_UP_COMMAND")",
  "config": {
    "envFile": "$(json_escape "$ENV_FILE")",
    "envBackupPath": "$(json_escape "$ENV_BACKUP_PATH")",
    "envRollbackSource": "$(json_escape "$ENV_BACKUP_PATH")",
    "apiPort": ${API_PORT},
    "adminPort": ${ADMIN_PORT},
    "postgresPort": ${POSTGRES_PORT_VALUE},
    "minioPort": ${MINIO_PORT_VALUE},
    "minioConsolePort": ${MINIO_CONSOLE_PORT_VALUE},
    "publicHost": "${local_host_url}",
    "urlScheme": "$(json_escape "$URL_SCHEME")",
    "apiPublicUrl": "$(json_escape "$API_PUBLIC_URL")",
    "adminPublicUrl": "$(json_escape "$ADMIN_PUBLIC_URL")",
    "detectedPublicIp": "${public_ip_url_formatted}"
  },
  "healthPolicy": {
    "retries": ${HEALTH_RETRIES},
    "timeoutSeconds": ${HEALTH_TIMEOUT_SECONDS},
    "retryDelaySeconds": ${HEALTH_RETRY_DELAY_SECONDS}
  },
  "adminSetup": {
    "mode": "$(json_escape "$ADMIN_SETUP_MODE")",
    "attempted": $(json_bool "$ADMIN_SETUP_ATTEMPTED"),
    "created": $(json_bool "$ADMIN_SETUP_CREATED"),
    "username": "$(json_escape "$ADMIN_SETUP_USERNAME")",
    "skipReason": "$(json_escape "$ADMIN_SETUP_SKIP_REASON")"
  },
  "checkSummary": {
    "total": $((CHECKS_PASSED_TOTAL + CHECKS_FAILED_TOTAL + CHECKS_SKIPPED_TOTAL)),
    "passed": ${CHECKS_PASSED_TOTAL},
    "failed": ${CHECKS_FAILED_TOTAL},
    "skipped": ${CHECKS_SKIPPED_TOTAL},
    "service": {
      "passed": ${CHECKS_SERVICE_PASSED},
      "failed": ${CHECKS_SERVICE_FAILED},
      "skipped": ${CHECKS_SERVICE_SKIPPED}
    },
    "http": {
      "passed": ${CHECKS_HTTP_PASSED},
      "failed": ${CHECKS_HTTP_FAILED},
      "skipped": ${CHECKS_HTTP_SKIPPED}
    },
    "meta": {
      "passed": ${CHECKS_META_PASSED},
      "failed": ${CHECKS_META_FAILED},
      "skipped": ${CHECKS_META_SKIPPED}
    }
  },
  "generatedSecrets": [${generated_secrets_json}],
  "envChanges": [${env_changes_json}],
  "checks": [${checks_json}]
}
EOF
)"

  if [ "$OUTPUT_JSON_PATH" = "-" ]; then
    printf '%s\n' "$report"
    return
  fi

  mkdir -p "$(dirname "$OUTPUT_JSON_PATH")"
  printf '%s\n' "$report" > "$OUTPUT_JSON_PATH"
  echo "Installer JSON report written to: $OUTPUT_JSON_PATH"
}

fail_now() {
  local exit_code="$1"
  local error_code="$2"
  local message="${3:-}"

  FINAL_EXIT_CODE="$exit_code"
  FINAL_STATUS="failed"
  FINAL_ERROR_CODE="$error_code"

  if [ -n "$message" ]; then
    echo "$message"
  fi

  if [ "$ENV_ROLLBACK_ON_FAIL" = "1" ] && [ "$DRY_RUN" != "1" ]; then
    if [ "$ENV_BACKUP_CREATED" = "true" ] && [ -n "$ENV_BACKUP_PATH" ] && [ -f "$ENV_BACKUP_PATH" ]; then
      if cp "$ENV_BACKUP_PATH" "$ENV_FILE"; then
        ENV_ROLLBACK_APPLIED="true"
        append_check_result "meta" "Env rollback on fail" "$ENV_FILE" "passed"
      else
        append_check_result "meta" "Env rollback on fail" "$ENV_FILE" "failed"
      fi
    else
      append_check_result "meta" "Env rollback on fail" "$ENV_FILE" "skipped(no-backup)"
    fi
  elif [ "$ENV_ROLLBACK_ON_FAIL" = "1" ]; then
    append_check_result "meta" "Env rollback on fail" "$ENV_FILE" "skipped(dry-run)"
  fi

  write_json_report
  exit "$exit_code"
}

compose_exec() {
  docker compose --env-file "$ENV_FILE" "$@"
}

parse_args "$@"

if [ -z "$ENV_FILE" ]; then
  fail_now 18 "invalid-env-file" "Env file path cannot be empty."
fi

if [ -f "$ENV_FILE" ]; then
  ENV_FILE_EXISTED_AT_START="true"
fi

if [ "$DRY_RUN" != "1" ]; then
  if ! command -v docker >/dev/null 2>&1; then
    fail_now 20 "docker-not-found" "Docker not found. Please install Docker and retry."
  fi

  if ! docker compose version >/dev/null 2>&1; then
    fail_now 21 "docker-compose-not-found" "Docker Compose plugin not found. Please install docker compose and retry."
  fi
fi

if [ ! -f "$ENV_FILE" ]; then
  if [ "$DRY_RUN" = "1" ]; then
    echo "DRY-RUN: env file '${ENV_FILE}' is missing; would create from .env.example."
  else
    if ! mkdir -p "$(dirname "$ENV_FILE")"; then
      fail_now 19 "env-file-create-failed" "Failed to create directory for env file '${ENV_FILE}'."
    fi
    if ! cp .env.example "$ENV_FILE"; then
      fail_now 19 "env-file-create-failed" "Failed to create env file '${ENV_FILE}' from .env.example."
    fi
    echo "Env file '${ENV_FILE}' created from .env.example. Edit secrets before production use."
  fi
fi

ENV_READ_FILE="$ENV_FILE"
if [ ! -f "$ENV_READ_FILE" ]; then
  ENV_READ_FILE=".env.example"
fi

read_env_value() {
  local key="$1"
  local default_value="$2"
  local value
  value="$(grep -E "^${key}=" "$ENV_READ_FILE" | tail -n 1 | cut -d '=' -f 2- || true)"
  value="${value%$'\r'}"
  if [ -z "$value" ]; then
    echo "$default_value"
  else
    echo "$value"
  fi
}

prepare_env_backup() {
  if [ "$ENV_BACKUP_DONE" = "1" ]; then
    return
  fi

  ENV_BACKUP_DONE="1"

  if [ "$ENABLE_ENV_BACKUP" != "1" ]; then
    append_check_result "meta" "Env backup" "$ENV_FILE" "skipped(flag)"
    return
  fi

  if [ "$ENV_FILE_EXISTED_AT_START" != "true" ]; then
    append_check_result "meta" "Env backup" "$ENV_FILE" "skipped(no-existing-file)"
    return
  fi

  local timestamp
  timestamp="$(date -u +"%Y%m%dT%H%M%SZ" 2>/dev/null || echo "now")"
  ENV_BACKUP_PATH="${ENV_FILE}.bak.${timestamp}"

  if [ "$DRY_RUN" = "1" ]; then
    echo "DRY-RUN: would create env backup '${ENV_BACKUP_PATH}'."
    append_check_result "meta" "Env backup" "$ENV_BACKUP_PATH" "skipped(dry-run)"
    return
  fi

  if cp "$ENV_FILE" "$ENV_BACKUP_PATH"; then
    ENV_BACKUP_CREATED="true"
    append_check_result "meta" "Env backup" "$ENV_BACKUP_PATH" "passed"
    return
  fi

  fail_now 24 "env-backup-failed" "Failed to create env backup file '${ENV_BACKUP_PATH}'."
}

write_env_value_atomic() {
  local target_file="$1"
  local key="$2"
  local value="$3"
  local target_dir
  local tmp_file

  target_dir="$(dirname "$target_file")"
  if ! mkdir -p "$target_dir"; then
    fail_now 26 "env-atomic-write-failed" "Failed to create env target directory '${target_dir}'."
  fi
  if ! tmp_file="$(mktemp "${target_file}.tmp.XXXXXX")"; then
    fail_now 26 "env-atomic-write-failed" "Failed to allocate temp file for env write."
  fi

  if [ -f "$target_file" ]; then
    if ! awk -v key="$key" -v value="$value" '
      BEGIN { updated = 0 }
      index($0, key "=") == 1 { print key "=" value; updated = 1; next }
      { print }
      END { if (!updated) print key "=" value }
    ' "$target_file" > "$tmp_file"; then
      rm -f "$tmp_file"
      fail_now 26 "env-atomic-write-failed" "Failed to build updated env content for '${target_file}'."
    fi
  else
    if ! printf '%s=%s\n' "$key" "$value" > "$tmp_file"; then
      rm -f "$tmp_file"
      fail_now 26 "env-atomic-write-failed" "Failed to initialize env file '${target_file}'."
    fi
  fi

  if ! mv "$tmp_file" "$target_file"; then
    rm -f "$tmp_file"
    fail_now 26 "env-atomic-write-failed" "Failed to atomically replace env file '${target_file}'."
  fi
}

write_env_value() {
  local key="$1"
  local value="$2"

  prepare_env_backup
  append_env_change "$key" "$value"

  if [ "$DRY_RUN" = "1" ]; then
    echo "DRY-RUN: would set ${key}=${value}"
    return
  fi

  write_env_value_atomic "$ENV_FILE" "$key" "$value"
}

generate_secret() {
  local length="${1:-64}"

  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex $((length / 2)) | cut -c1-"$length"
    return
  fi

  if [ -r /dev/urandom ]; then
    tr -dc 'A-Za-z0-9' </dev/urandom | head -c "$length"
    return
  fi

  printf '%s' "$(date +%s)-$$-$(hostname)-$RANDOM" | tr -dc 'A-Za-z0-9' | head -c "$length"
}

ensure_secret_value() {
  local key="$1"
  local current="$2"
  local default_hint="$3"
  local fallback_hint="$4"
  local length="$5"

  if [ -z "$current" ] || [ "$current" = "$default_hint" ] || [ "$current" = "$fallback_hint" ]; then
    local generated
    generated="$(generate_secret "$length")"
    write_env_value "$key" "$generated"
    append_generated_secret "$key"
    echo "$key was empty/default and has been generated."
  fi
}

ask_with_default() {
  local prompt="$1"
  local default_value="$2"
  local answer

  if [ "$NON_INTERACTIVE" = "1" ]; then
    echo "$default_value"
    return
  fi

  read -r -p "${prompt} [${default_value}]: " answer
  if [ -z "$answer" ]; then
    echo "$default_value"
  else
    echo "$answer"
  fi
}

ask_secret() {
  local prompt="$1"
  local answer

  if [ "$NON_INTERACTIVE" = "1" ]; then
    echo ""
    return
  fi

  read -r -s -p "${prompt}: " answer
  echo
  echo "$answer"
}

validate_admin_username() {
  local username="$1"
  if [ -z "$username" ]; then
    fail_now 32 "invalid-admin-username" "Admin username cannot be empty."
  fi

  if [ "${#username}" -lt 3 ] || [ "${#username}" -gt 64 ]; then
    fail_now 32 "invalid-admin-username" "Admin username length must be between 3 and 64 characters."
  fi
}

validate_admin_password() {
  local password="$1"
  if [ -z "$password" ]; then
    fail_now 33 "invalid-admin-password" "Admin password cannot be empty."
  fi

  if [ "${#password}" -lt 8 ] || [ "${#password}" -gt 128 ]; then
    fail_now 33 "invalid-admin-password" "Admin password length must be between 8 and 128 characters."
  fi
}

detect_public_ip() {
  if ! command -v curl >/dev/null 2>&1; then
    echo ""
    return
  fi

  local ip
  ip="$(curl -fsS --max-time 3 ifconfig.me 2>/dev/null || true)"
  if [ -n "$ip" ]; then
    echo "$ip"
    return
  fi

  echo ""
}

wait_for_http() {
  local url="$1"
  local label="$2"
  local attempt=1

  if ! command -v curl >/dev/null 2>&1; then
    echo "curl not found, skipping check: ${label}"
    append_check_result "http" "$label" "$url" "failed"
    return 1
  fi

  while [ "$attempt" -le "$HEALTH_RETRIES" ]; do
    if curl -fsS --max-time "$HEALTH_TIMEOUT_SECONDS" "$url" >/dev/null 2>&1; then
      echo "${label}: OK (${url})"
      append_check_result "http" "$label" "$url" "passed"
      return 0
    fi

    if [ "$attempt" -lt "$HEALTH_RETRIES" ]; then
      sleep "$HEALTH_RETRY_DELAY_SECONDS"
    fi
    attempt=$((attempt + 1))
  done

  echo "${label}: FAILED (${url})"
  append_check_result "http" "$label" "$url" "failed"
  return 1
}

fetch_admin_setup_status() {
  local url="$1"
  local attempt=1
  local response

  if ! command -v curl >/dev/null 2>&1; then
    echo ""
    return 1
  fi

  while [ "$attempt" -le "$HEALTH_RETRIES" ]; do
    response="$(curl -fsS --max-time "$HEALTH_TIMEOUT_SECONDS" "$url" 2>/dev/null || true)"
    if [[ "$response" == *'"needsSetup":true'* ]] || [[ "$response" == *'"needsSetup": true'* ]]; then
      echo "true"
      return 0
    fi
    if [[ "$response" == *'"needsSetup":false'* ]] || [[ "$response" == *'"needsSetup": false'* ]]; then
      echo "false"
      return 0
    fi

    if [ "$attempt" -lt "$HEALTH_RETRIES" ]; then
      sleep "$HEALTH_RETRY_DELAY_SECONDS"
    fi
    attempt=$((attempt + 1))
  done

  echo ""
  return 1
}

create_first_admin() {
  local setup_url="$1"
  local username="$2"
  local password="$3"
  local payload
  local response_file
  local body
  local status

  payload="$(printf '{"username":"%s","password":"%s"}' "$(json_escape "$username")" "$(json_escape "$password")")"
  response_file="$(mktemp "${TMPDIR:-/tmp}/blp-admin-setup.XXXXXX")"

  status="$(curl -sS --max-time "$HEALTH_TIMEOUT_SECONDS" -o "$response_file" -w "%{http_code}" \
    -H "Content-Type: application/json" \
    -X POST \
    --data "$payload" \
    "$setup_url" || true)"
  body="$(cat "$response_file" 2>/dev/null || true)"
  rm -f "$response_file"

  case "$status" in
    200)
      return 0
      ;;
    409)
      return 2
      ;;
    *)
      if [ -n "$body" ]; then
        echo "$body" | tr '\n' ' ' | cut -c1-240
      fi
      return 1
      ;;
  esac
}

maybe_setup_admin() {
  local setup_status_url="http://localhost:${API_PORT}/api/admin/setup/status"
  local setup_create_url="http://localhost:${API_PORT}/api/admin/setup"
  local needs_setup
  local should_setup="false"
  local username
  local password
  local ask_result

  if [ "$DRY_RUN" = "1" ]; then
    ADMIN_SETUP_SKIP_REASON="dry-run"
    append_check_result "meta" "Admin setup" "$setup_create_url" "skipped(dry-run)"
    return
  fi

  if [ "$ADMIN_SETUP_MODE" = "skip" ]; then
    ADMIN_SETUP_SKIP_REASON="flag-skip"
    append_check_result "meta" "Admin setup" "$setup_create_url" "skipped(flag)"
    return
  fi

  needs_setup="$(fetch_admin_setup_status "$setup_status_url" || true)"
  if [ -z "$needs_setup" ]; then
    ADMIN_SETUP_SKIP_REASON="status-unavailable"
    append_check_result "meta" "Admin setup status" "$setup_status_url" "failed"
    if [ "$ADMIN_SETUP_MODE" = "force" ]; then
      fail_now 31 "admin-setup-status-failed" "Failed to resolve admin setup status endpoint."
    fi
    return
  fi

  append_check_result "meta" "Admin setup status" "$setup_status_url" "passed"

  if [ "$needs_setup" != "true" ]; then
    ADMIN_SETUP_SKIP_REASON="already-configured"
    append_check_result "meta" "Admin setup" "$setup_create_url" "skipped(already-configured)"
    return
  fi

  if [ "$ADMIN_SETUP_MODE" = "force" ]; then
    should_setup="true"
  elif [ "$NON_INTERACTIVE" = "1" ]; then
    ADMIN_SETUP_SKIP_REASON="non-interactive-no-force"
    append_check_result "meta" "Admin setup" "$setup_create_url" "skipped(non-interactive)"
    return
  else
    ask_result="$(ask_with_default "Create first admin account now? (true/false)" "true")"
    ask_result="$(normalize_bool "$ask_result")"
    if [ "$ask_result" = "true" ]; then
      should_setup="true"
    else
      ADMIN_SETUP_SKIP_REASON="user-declined"
      append_check_result "meta" "Admin setup" "$setup_create_url" "skipped(user)"
      return
    fi
  fi

  if [ "$should_setup" != "true" ]; then
    ADMIN_SETUP_SKIP_REASON="not-requested"
    append_check_result "meta" "Admin setup" "$setup_create_url" "skipped(no-request)"
    return
  fi

  username="${ADMIN_USERNAME_OVERRIDE:-admin}"
  if [ "$NON_INTERACTIVE" != "1" ] && [ -z "${ADMIN_USERNAME_OVERRIDE}" ]; then
    username="$(ask_with_default "Admin username" "$username")"
  fi
  validate_admin_username "$username"

  password="${ADMIN_PASSWORD_OVERRIDE:-${BLP_ADMIN_PASSWORD:-}}"
  if [ -z "$password" ]; then
    if [ "$NON_INTERACTIVE" = "1" ]; then
      fail_now 33 "missing-admin-password" "Admin password is required in non-interactive setup mode. Use --admin-password or BLP_ADMIN_PASSWORD."
    fi

    while true; do
      password="$(ask_secret "Admin password (8+ chars)")"
      validate_admin_password "$password"
      local confirm_password
      confirm_password="$(ask_secret "Repeat admin password")"
      if [ "$password" = "$confirm_password" ]; then
        break
      fi
      echo "Passwords do not match. Try again."
    done
  else
    validate_admin_password "$password"
  fi

  ADMIN_SETUP_ATTEMPTED="true"
  ADMIN_SETUP_USERNAME="$username"

  local setup_error=""
  local setup_result=0
  set +e
  setup_error="$(create_first_admin "$setup_create_url" "$username" "$password")"
  setup_result=$?
  set -e

  if [ "$setup_result" -eq 0 ]; then
    ADMIN_SETUP_CREATED="true"
    ADMIN_SETUP_SKIP_REASON=""
    append_check_result "meta" "Admin setup" "$username" "passed"
    echo "Admin account created: ${username}"
    return
  fi

  if [ "$setup_result" -eq 2 ]; then
    ADMIN_SETUP_CREATED="false"
    ADMIN_SETUP_SKIP_REASON="already-configured"
    append_check_result "meta" "Admin setup" "$username" "skipped(already-configured)"
    echo "Admin setup skipped: account already configured."
    return
  fi

  ADMIN_SETUP_CREATED="false"
  ADMIN_SETUP_SKIP_REASON="request-failed"
  append_check_result "meta" "Admin setup" "$username" "failed"

  if [ "$ADMIN_SETUP_MODE" = "force" ]; then
    if [ -n "$setup_error" ]; then
      fail_now 31 "admin-setup-failed" "Admin setup failed: ${setup_error}"
    fi
    fail_now 31 "admin-setup-failed" "Admin setup failed."
  fi

  if [ -n "$setup_error" ]; then
    echo "Admin setup failed: ${setup_error}"
  else
    echo "Admin setup failed."
  fi
}

is_port_busy() {
  local port="$1"

  if command -v ss >/dev/null 2>&1; then
    ss -ltn "( sport = :$port )" 2>/dev/null | awk 'NR>1 { exit 0 } END { exit 1 }'
    return $?
  fi

  if command -v lsof >/dev/null 2>&1; then
    lsof -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
    return $?
  fi

  if command -v netstat >/dev/null 2>&1; then
    netstat -an 2>/dev/null | grep -E "[:.]${port}[[:space:]].*LISTEN" >/dev/null 2>&1
    return $?
  fi

  return 2
}

check_port_available() {
  local port="$1"
  local label="$2"

  is_port_busy "$port"
  local status=$?

  if [ "$status" -eq 0 ]; then
    append_check_result "meta" "Port availability: ${label}" "$port" "failed"
    return 1
  fi

  if [ "$status" -eq 2 ]; then
    append_check_result "meta" "Port availability: ${label}" "$port" "skipped(no-tool)"
    return 0
  fi

  append_check_result "meta" "Port availability: ${label}" "$port" "passed"
  return 0
}

format_host_for_url() {
  local host="$1"
  if [[ "$host" == *:* && "$host" != \[*\] ]]; then
    echo "[$host]"
  else
    echo "$host"
  fi
}

check_compose_service_running() {
  local service="$1"
  local label="$2"
  local running

  running="$(compose_exec ps --services --status running 2>/dev/null || true)"
  if printf '%s\n' "$running" | grep -qx "$service"; then
    echo "${label}: RUNNING (${service})"
    append_check_result "service" "$label" "$service" "passed"
    return 0
  fi

  echo "${label}: NOT RUNNING (${service})"
  append_check_result "service" "$label" "$service" "failed"
  return 1
}

validate_port() {
  local value="$1"
  local label="$2"

  if ! [[ "$value" =~ ^[0-9]+$ ]]; then
    fail_now 12 "invalid-port" "${label} must be numeric, got: ${value}"
  fi

  if [ "$value" -lt 1 ] || [ "$value" -gt 65535 ]; then
    fail_now 12 "invalid-port" "${label} must be in range 1..65535, got: ${value}"
  fi
}

validate_positive_int() {
  local value="$1"
  local label="$2"
  local error_code="$3"

  if ! [[ "$value" =~ ^[0-9]+$ ]]; then
    fail_now "$error_code" "invalid-${label}" "${label} must be a positive integer, got: ${value}"
  fi

  if [ "$value" -lt 1 ]; then
    fail_now "$error_code" "invalid-${label}" "${label} must be >= 1, got: ${value}"
  fi
}

DEFAULT_API_PORT="$(read_env_value "API_PORT" "8080")"
DEFAULT_ADMIN_PORT="$(read_env_value "ADMIN_PORT" "5173")"
DEFAULT_HOST="$(read_env_value "INSTALL_PUBLIC_HOST" "localhost")"
DEFAULT_SSL="$(read_env_value "INSTALL_USE_SSL" "false")"
DEFAULT_USE_MINIO="false"

echo "BivLauncher installer"
echo "Env file: ${ENV_FILE}"
API_PORT="${API_PORT_OVERRIDE:-$(ask_with_default "API port" "$DEFAULT_API_PORT")}"
ADMIN_PORT="${ADMIN_PORT_OVERRIDE:-$(ask_with_default "Admin port" "$DEFAULT_ADMIN_PORT")}"
PUBLIC_HOST="${HOST_OVERRIDE:-$(ask_with_default "Public host/domain (empty = localhost)" "$DEFAULT_HOST")}"
SSL_INPUT="${SSL_OVERRIDE:-$(ask_with_default "Use HTTPS for URL config? (true/false)" "$DEFAULT_SSL")}"
MINIO_INPUT="${USE_MINIO_OVERRIDE:-$(ask_with_default "Enable MinIO profile? (true/false)" "$DEFAULT_USE_MINIO")}"

validate_port "$API_PORT" "API port"
validate_port "$ADMIN_PORT" "Admin port"

HEALTH_RETRIES="${HEALTH_RETRIES_OVERRIDE:-$HEALTH_RETRIES}"
HEALTH_TIMEOUT_SECONDS="${HEALTH_TIMEOUT_OVERRIDE:-$HEALTH_TIMEOUT_SECONDS}"
HEALTH_RETRY_DELAY_SECONDS="${HEALTH_DELAY_OVERRIDE:-$HEALTH_RETRY_DELAY_SECONDS}"
validate_positive_int "$HEALTH_RETRIES" "health-retries" 15
validate_positive_int "$HEALTH_TIMEOUT_SECONDS" "health-timeout" 16
validate_positive_int "$HEALTH_RETRY_DELAY_SECONDS" "health-delay" 17

POSTGRES_PORT_VALUE="$(read_env_value "POSTGRES_PORT" "5432")"
MINIO_PORT_VALUE="$(read_env_value "MINIO_PORT" "9000")"
MINIO_CONSOLE_PORT_VALUE="$(read_env_value "MINIO_CONSOLE_PORT" "9001")"
validate_port "$POSTGRES_PORT_VALUE" "Postgres port"
validate_port "$MINIO_PORT_VALUE" "MinIO API port"
validate_port "$MINIO_CONSOLE_PORT_VALUE" "MinIO Console port"

SSL_NORMALIZED="$(normalize_bool "$SSL_INPUT")"
if [ -z "$SSL_NORMALIZED" ]; then
  fail_now 13 "invalid-ssl-bool" "Invalid --ssl/SSL value: '$SSL_INPUT'. Expected true/false."
fi

MINIO_NORMALIZED="$(normalize_bool "$MINIO_INPUT")"
if [ -z "$MINIO_NORMALIZED" ]; then
  fail_now 14 "invalid-minio-bool" "Invalid minio value: '$MINIO_INPUT'. Expected true/false."
fi

if [ "$SSL_NORMALIZED" = "true" ]; then
  URL_SCHEME="https"
else
  URL_SCHEME="http"
fi

if [ "$MINIO_NORMALIZED" = "true" ]; then
  USE_MINIO_PROFILE="true"
else
  USE_MINIO_PROFILE="false"
fi

if [ -z "$PUBLIC_HOST" ]; then
  PUBLIC_HOST="localhost"
fi

URL_HOST="$(format_host_for_url "$PUBLIC_HOST")"
API_PUBLIC_URL="${URL_SCHEME}://${URL_HOST}:${API_PORT}"
ADMIN_PUBLIC_URL="${URL_SCHEME}://${URL_HOST}:${ADMIN_PORT}"

write_env_value "API_PORT" "$API_PORT"
write_env_value "ADMIN_PORT" "$ADMIN_PORT"
write_env_value "PUBLIC_BASE_URL" "$API_PUBLIC_URL"
write_env_value "VITE_API_BASE_URL" "$API_PUBLIC_URL"
write_env_value "ADMIN_ALLOWED_ORIGINS" "$ADMIN_PUBLIC_URL"
write_env_value "INSTALL_PUBLIC_HOST" "$PUBLIC_HOST"
write_env_value "INSTALL_USE_SSL" "$SSL_NORMALIZED"

if [ "$MINIO_NORMALIZED" = "true" ]; then
  write_env_value "S3_USE_S3" "true"
  CURRENT_S3_ENDPOINT="$(read_env_value "S3_ENDPOINT" "")"
  if [ -z "$CURRENT_S3_ENDPOINT" ] || [ "$CURRENT_S3_ENDPOINT" = "http://localhost:9000" ] || [ "$CURRENT_S3_ENDPOINT" = "http://127.0.0.1:9000" ]; then
    write_env_value "S3_ENDPOINT" "http://minio:9000"
  fi
else
  write_env_value "S3_USE_S3" "false"
fi

POSTGRES_DB_VALUE="$(read_env_value "POSTGRES_DB" "bivlauncher")"
POSTGRES_USER_VALUE="$(read_env_value "POSTGRES_USER" "postgres")"
POSTGRES_PASSWORD_VALUE="$(read_env_value "POSTGRES_PASSWORD" "postgres")"
DB_CONN_VALUE="Host=db;Port=5432;Database=${POSTGRES_DB_VALUE};Username=${POSTGRES_USER_VALUE};Password=${POSTGRES_PASSWORD_VALUE}"
write_env_value "DB_CONN" "$DB_CONN_VALUE"

JWT_SECRET_VALUE="$(read_env_value "JWT_SECRET" "")"
HWID_HMAC_VALUE="$(read_env_value "HWID_HMAC_SALT" "")"
ensure_secret_value "JWT_SECRET" "$JWT_SECRET_VALUE" "change_me_to_a_long_random_secret" "change-me" "64"
ensure_secret_value "HWID_HMAC_SALT" "$HWID_HMAC_VALUE" "change_me_hwid_salt" "change-me" "64"

if [ "$DRY_RUN" = "1" ]; then
  append_check_result "meta" "Port pre-check" "local-listen-ports" "skipped(dry-run)"
elif [ "$SKIP_PORT_CHECK" = "1" ]; then
  append_check_result "meta" "Port pre-check" "local-listen-ports" "skipped(flag)"
else
  PORT_CHECK_FAILED=0
  check_port_available "$API_PORT" "API" || PORT_CHECK_FAILED=1
  check_port_available "$ADMIN_PORT" "Admin" || PORT_CHECK_FAILED=1
  check_port_available "$POSTGRES_PORT_VALUE" "Postgres" || PORT_CHECK_FAILED=1
  if [ "$USE_MINIO_PROFILE" = "true" ]; then
    check_port_available "$MINIO_PORT_VALUE" "MinIO API" || PORT_CHECK_FAILED=1
    check_port_available "$MINIO_CONSOLE_PORT_VALUE" "MinIO Console" || PORT_CHECK_FAILED=1
  fi

  if [ "$PORT_CHECK_FAILED" -ne 0 ]; then
    fail_now 22 "port-in-use" "One or more required ports are already in use. Use --skip-port-check to bypass."
  fi
fi

if [ "$DRY_RUN" = "1" ]; then
  append_check_result "meta" "Compose config preflight" "docker compose config -q" "skipped(dry-run)"
elif [ "$SKIP_COMPOSE_CONFIG_CHECK" = "1" ]; then
  append_check_result "meta" "Compose config preflight" "docker compose config -q" "skipped(flag)"
else
  if compose_exec config -q >/dev/null 2>&1; then
    append_check_result "meta" "Compose config preflight" "docker compose config -q" "passed"
  else
    fail_now 23 "compose-config-invalid" "Docker compose config validation failed. Run 'docker compose --env-file \"$ENV_FILE\" config' for details."
  fi
fi

echo "Starting BivLauncher stack..."
if [ "$USE_MINIO_PROFILE" = "true" ]; then
  COMPOSE_UP_COMMAND="docker compose --env-file \"$ENV_FILE\" --profile minio up -d --build"
else
  COMPOSE_UP_COMMAND="docker compose --env-file \"$ENV_FILE\" up -d --build"
fi

if [ "$DRY_RUN" = "1" ]; then
  echo "DRY-RUN: would run: ${COMPOSE_UP_COMMAND}"
elif [ "$USE_MINIO_PROFILE" = "true" ]; then
  if ! compose_exec --profile minio up -d --build; then
    fail_now 25 "compose-up-failed" "Docker compose up failed for profile minio."
  fi
else
  if ! compose_exec up -d --build; then
    fail_now 25 "compose-up-failed" "Docker compose up failed."
  fi
fi

if [ "$DRY_RUN" != "1" ] && [ "$SKIP_HEALTH_CHECK" != "1" ]; then
  HEALTH_CHECKS_RAN="true"
  HEALTH_CHECKS_SKIPPED="false"
  echo "Running health checks..."
  FAILED_CHECKS=0
  API_LOCAL_URL="http://localhost:${API_PORT}/health"
  API_SETUP_URL="http://localhost:${API_PORT}/api/admin/setup/status"
  ADMIN_LOCAL_URL="http://localhost:${ADMIN_PORT}"

  check_compose_service_running "api" "Docker compose service check" || FAILED_CHECKS=$((FAILED_CHECKS + 1))
  check_compose_service_running "admin" "Docker compose service check" || FAILED_CHECKS=$((FAILED_CHECKS + 1))
  if [ "$USE_MINIO_PROFILE" = "true" ]; then
    check_compose_service_running "minio" "Docker compose service check" || FAILED_CHECKS=$((FAILED_CHECKS + 1))
  fi

  wait_for_http "$API_LOCAL_URL" "API health endpoint" || FAILED_CHECKS=$((FAILED_CHECKS + 1))
  wait_for_http "$API_SETUP_URL" "Admin setup-status endpoint" || FAILED_CHECKS=$((FAILED_CHECKS + 1))
  wait_for_http "$ADMIN_LOCAL_URL" "Admin UI endpoint" || FAILED_CHECKS=$((FAILED_CHECKS + 1))

  FAILED_CHECKS_TOTAL="$FAILED_CHECKS"

  if [ "$FAILED_CHECKS" -gt 0 ]; then
    echo "Health checks completed with ${FAILED_CHECKS} failure(s)."
    if [ "$STRICT_CHECK" = "1" ]; then
      fail_now 30 "strict-check-failed" "Strict mode enabled: exiting with error."
    fi
  else
    echo "All post-start checks passed."
  fi
elif [ "$DRY_RUN" = "1" ]; then
  HEALTH_CHECKS_RAN="false"
  HEALTH_CHECKS_SKIPPED="true"
  echo "DRY-RUN: health checks are skipped (no services are started)."
  append_check_result "meta" "Health checks" "post-start" "skipped(dry-run)"
else
  HEALTH_CHECKS_RAN="false"
  HEALTH_CHECKS_SKIPPED="true"
  append_check_result "meta" "Health checks" "post-start" "skipped(flag)"
fi

maybe_setup_admin

if [ "$NO_PUBLIC_IP" = "1" ]; then
  PUBLIC_IP=""
  append_check_result "meta" "Public IP detection" "ifconfig.me" "skipped(flag)"
else
  PUBLIC_IP="$(detect_public_ip)"
  if [ -n "$PUBLIC_IP" ]; then
    append_check_result "meta" "Public IP detection" "ifconfig.me" "passed"
  else
    append_check_result "meta" "Public IP detection" "ifconfig.me" "skipped(unavailable)"
  fi
fi

if [ -n "${PUBLIC_IP}" ] && [ "$PUBLIC_HOST" = "localhost" ]; then
  URL_PUBLIC_IP="$(format_host_for_url "$PUBLIC_IP")"
  echo "Admin URL (public): ${URL_SCHEME}://${URL_PUBLIC_IP}:${ADMIN_PORT}"
  echo "API URL (public):   ${URL_SCHEME}://${URL_PUBLIC_IP}:${API_PORT}"
  echo "Admin URL (local):  ${ADMIN_PUBLIC_URL}"
  echo "API URL (local):    ${API_PUBLIC_URL}"
else
  echo "Admin URL: ${ADMIN_PUBLIC_URL}"
  echo "API URL:   ${API_PUBLIC_URL}"
fi

if [ "$ADMIN_SETUP_CREATED" = "true" ]; then
  echo "Admin setup completed for user: ${ADMIN_SETUP_USERNAME}"
elif [ "$ADMIN_SETUP_ATTEMPTED" = "true" ]; then
  echo "Admin setup attempt finished without account creation."
fi

write_json_report

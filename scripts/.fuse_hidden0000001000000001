#!/usr/bin/env bash
# Спільні змінні та хелпери для скриптів деплою. Підключається через `source`.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export AWS_REGION="${AWS_REGION:-us-west-2}"   # увесь проєкт захардкожений під us-west-2
export AWS_DEFAULT_REGION="$AWS_REGION"
export AWS_PAGER=""

TF_DIR="$REPO_ROOT/infra/terraform"
BE_DIR="$REPO_ROOT/backend/restaurant-backend-app"
FE_DIR="$REPO_ROOT/frontend/restaurant-frontend-app"
SDCT_DIR="$BE_DIR/.syndicate-config-restaurant-backend-app"
VENV_DIR="$BE_DIR/syndicate_venv"
API_NAME="restaurant-backend-api"
API_STAGE="develop"

log()  { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[warn]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2; exit 1; }

account_id() { aws sts get-caller-identity --query Account --output text; }

api_id() {
  aws apigateway get-rest-apis --region "$AWS_REGION" \
    --query "items[?name=='${API_NAME}'].id | [0]" --output text
}

# Друкує базовий URL API (зі стейджем) або повертає 1, якщо API ще не задеплоєно.
api_url() {
  local id; id="$(api_id)"
  [[ -z "$id" || "$id" == "None" ]] && return 1
  echo "https://${id}.execute-api.${AWS_REGION}.amazonaws.com/${API_STAGE}"
}

bucket_exists() { aws s3api head-bucket --bucket "$1" >/dev/null 2>&1; }

# Syndicate потребує Python 3.10–3.11; venv створюється один раз у backend/…/syndicate_venv
ensure_syndicate() {
  if [[ ! -x "$VENV_DIR/bin/syndicate" ]]; then
    local py; py="$(command -v python3.11 || command -v python3.10 || true)"
    [[ -z "$py" ]] && die "Потрібен python3.11 або python3.10 (brew install python@3.11)"
    log "Створюю venv для Syndicate ($py)"
    "$py" -m venv "$VENV_DIR"
    "$VENV_DIR/bin/pip" install --quiet --upgrade pip
    "$VENV_DIR/bin/pip" install --quiet aws-syndicate
  fi
  export PATH="$VENV_DIR/bin:$PATH"
  export SDCT_CONF="$SDCT_DIR"
}

#!/usr/bin/env bash
# Підставляє твій AWS account ID, локальний шлях і email у конфіги Terraform та Syndicate.
# Ідемпотентний: можна запускати повторно (наприклад, після зміни акаунта).
#
# Використання:
#   scripts/configure-account.sh you@example.com            # account ID береться з aws sts
#   scripts/configure-account.sh you@example.com 123456789012
source "$(dirname "$0")/lib.sh"

EMAIL="${1:-}"
[[ -z "$EMAIL" ]] && die "Передай email для алертів: scripts/configure-account.sh you@example.com"
ACC="${2:-$(account_id)}"
[[ "$ACC" =~ ^[0-9]{12}$ ]] || die "Некоректний account ID: '$ACC'"
export ACC BE_DIR

log "Account: $ACC · email: $EMAIL · region: $AWS_REGION"

# --- Terraform ---
perl -pi -e 's/restaurant-terraform-state-\d{12}/restaurant-terraform-state-$ENV{ACC}/' "$TF_DIR/backend.tf"
perl -pi -e 's/(default\s*=\s*")\d{12}(")/$1$ENV{ACC}$2/' "$TF_DIR/variables.tf"
# alert_emails іде в terraform.tfvars — він у .gitignore, email не потрапить у репозиторій
printf 'alert_emails = ["%s"]\n' "$EMAIL" > "$TF_DIR/terraform.tfvars"

# --- Syndicate ---
SY="$SDCT_DIR/syndicate.yml"
AL="$SDCT_DIR/syndicate_aliases.yml"
perl -pi -e 's/^(account_id:\s*")\d{12}(")/$1$ENV{ACC}$2/' "$SY" "$AL"
perl -pi -e 's|^project_path:.*|project_path: "$ENV{BE_DIR}" # локальний абсолютний шлях (CI підставляє свій)|' "$SY"
# в оригіналі тут помилка (зайвий 0 в кінці) — приводимо до імені з README
perl -pi -e 's|^deploy_target_bucket:.*|deploy_target_bucket: "marathon2026-restaurant-artifacts-$ENV{ACC}"|' "$SY"
perl -pi -e 's/(restaurant-backend-dev-(?:images|profile-images|swagger-ui)-)\d{12}/$1$ENV{ACC}/g' "$AL"

log "Готово. Змінені значення:"
grep -n "restaurant-terraform-state-" "$TF_DIR/backend.tf"
grep -n -A0 'default *= *"[0-9]\{12\}"' "$TF_DIR/variables.tf"
cat "$TF_DIR/terraform.tfvars"
grep -nE "^(account_id|project_path|deploy_target_bucket):" "$SY"
grep -nE "^(account_id|imagesBucketName|profileImagesBucketName|swagger_ui_hoster_bucket_name):" "$AL"

if grep -rq "747482682250" "$TF_DIR" "$SDCT_DIR" 2>/dev/null; then
  warn "Десь лишився старий account ID 747482682250:"; grep -rn "747482682250" "$TF_DIR" "$SDCT_DIR"
fi

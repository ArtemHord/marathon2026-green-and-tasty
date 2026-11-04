#!/usr/bin/env bash
# Деплой Green & Tasty у твій AWS-акаунт: state bucket → Terraform → Syndicate (backend) → Angular → S3/CloudFront.
#
# Використання:
#   scripts/deploy.sh all            # усе по порядку (перший деплой)
#   scripts/deploy.sh state          # лише S3-бакет для terraform state
#   scripts/deploy.sh infra          # terraform init / validate / plan / apply
#   scripts/deploy.sh backend        # syndicate build + deploy (або update, якщо API вже існує)
#   scripts/deploy.sh frontend       # npm ci + build з BACKEND_URL + s3 sync + CloudFront invalidation
#   scripts/deploy.sh outputs        # надрукувати URL-и
#   кроки можна комбінувати: scripts/deploy.sh backend frontend
#
# Прапорці:
#   --yes        не питати підтвердження перед terraform apply
#   --redeploy   syndicate deploy замість update, навіть якщо API вже є
source "$(dirname "$0")/lib.sh"

YES=0; REDEPLOY=0; STEPS=()
for a in "$@"; do
  case "$a" in
    --yes) YES=1 ;;
    --redeploy) REDEPLOY=1 ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    *) STEPS+=("$a") ;;
  esac
done
[[ ${#STEPS[@]} -eq 0 ]] && STEPS=(all)

ACC="$(account_id)" || die "AWS CLI не налаштований: aws configure"
grep -q "restaurant-terraform-state-$ACC" "$TF_DIR/backend.tf" \
  || die "Конфіги не під твій акаунт ($ACC). Спершу: scripts/configure-account.sh <email>"

step_state() {
  local b="restaurant-terraform-state-$ACC"
  if bucket_exists "$b"; then log "State bucket $b вже існує"; return; fi
  log "Створюю state bucket $b"
  if [[ "$AWS_REGION" == "us-east-1" ]]; then
    aws s3api create-bucket --bucket "$b" --region "$AWS_REGION" >/dev/null
  else
    aws s3api create-bucket --bucket "$b" --region "$AWS_REGION" \
      --create-bucket-configuration LocationConstraint="$AWS_REGION" >/dev/null
  fi
  aws s3api put-bucket-versioning --bucket "$b" --versioning-configuration Status=Enabled
  aws s3api put-public-access-block --bucket "$b" --public-access-block-configuration \
    "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
  aws s3api put-bucket-encryption --bucket "$b" --server-side-encryption-configuration \
    '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
}

step_infra() {
  cd "$TF_DIR"
  terraform init -input=false
  terraform fmt -check -recursive || warn "terraform fmt знайшов неформатовані файли (не блокує деплой)"
  terraform validate
  terraform plan -input=false -out=tfplan
  if [[ $YES -eq 0 ]]; then
    read -r -p "Застосувати цей план? [y/N] " r
    [[ "$r" == [yY] ]] || die "Скасовано"
  fi
  terraform apply -input=false tfplan
  rm -f tfplan
  log "Перевір пошту: AWS надішле лист 'AWS Notification - Subscription Confirmation' — підтверди, інакше алерти не прийдуть"
}

step_backend() {
  ensure_syndicate
  cd "$BE_DIR"
  local art="marathon2026-restaurant-artifacts-$ACC"
  if bucket_exists "$art"; then log "Artifacts bucket $art вже існує"; else syndicate create-deploy-target-bucket; fi
  syndicate build
  if [[ $REDEPLOY -eq 0 ]] && api_url >/dev/null; then
    log "API вже існує → syndicate update"
    syndicate update
  else
    log "Перший деплой → syndicate deploy --deploy_name dev"
    syndicate deploy --deploy_name dev
  fi
  log "API: $(api_url)"
}

step_frontend() {
  local url; url="$(api_url)" || die "API Gateway '$API_NAME' не знайдено — спершу scripts/deploy.sh backend"
  local bucket="restaurant-frontend-$ACC"
  cd "$FE_DIR"
  log "BACKEND_URL=$url"
  npm ci
  BACKEND_URL="$url" npm run build
  log "Синхронізую dist → s3://$bucket"
  aws s3 sync dist/restaurant-frontend-app/browser "s3://$bucket/" --delete
  local dist_id; dist_id="$(cd "$TF_DIR" && terraform output -raw cloudfront_distribution_id)"
  aws cloudfront create-invalidation --distribution-id "$dist_id" --paths '/*' >/dev/null
  log "CloudFront invalidation запущено (1–3 хв до оновлення)"
}

step_outputs() {
  local api cf dash
  api="$(api_url || echo '— (backend ще не задеплоєно)')"
  cf="$(cd "$TF_DIR" && terraform output -raw cloudfront_distribution_domain_name 2>/dev/null || echo '—')"
  dash="$(cd "$TF_DIR" && terraform output -raw dashboard_url 2>/dev/null || echo '—')"
  cat <<EOF

  Account   : $ACC ($AWS_REGION)
  Frontend  : https://$cf
  API       : $api
  Dashboard : $dash

EOF
}

for s in "${STEPS[@]}"; do
  case "$s" in
    all) step_state; step_infra; step_backend; step_frontend; step_outputs ;;
    state|infra|backend|frontend|outputs) "step_$s" ;;
    *) die "Невідомий крок: $s (див. scripts/deploy.sh --help)" ;;
  esac
done

#!/usr/bin/env bash
# Видаляє все, що створив deploy.sh, у зворотному порядку. НЕЗВОРОТНО — дані в DynamoDB та S3 зникнуть.
#
# Використання:
#   scripts/destroy.sh          # файли з бакетів + syndicate clean + terraform destroy
#   scripts/destroy.sh --all    # + terraform state bucket і syndicate artifacts bucket
source "$(dirname "$0")/lib.sh"

ALL=0; [[ "${1:-}" == "--all" ]] && ALL=1
ACC="$(account_id)"
read -r -p "Видалити ВСІ ресурси застосунку в акаунті $ACC ($AWS_REGION)? Введи 'destroy': " r
[[ "$r" == "destroy" ]] || die "Скасовано"

empty_bucket() {
  bucket_exists "$1" || return 0
  log "Очищаю s3://$1"
  aws s3 rm "s3://$1" --recursive >/dev/null || true
}

# Для versioned-бакета (terraform state) треба видалити всі версії й delete markers
purge_versions() {
  local b="$1" q batch
  bucket_exists "$b" || return 0
  log "Видаляю всі версії в s3://$b"
  for q in Versions DeleteMarkers; do
    while :; do
      batch="$(aws s3api list-object-versions --bucket "$b" --max-items 500 --output json \
        --query "{Objects: ${q}[].{Key:Key,VersionId:VersionId}}")"
      grep -q '"Key"' <<<"$batch" || break
      aws s3api delete-objects --bucket "$b" --delete "$batch" >/dev/null
    done
  done
}

# 1. Бакети, якими керує Terraform (без force_destroy — мають бути порожні)
for b in restaurant-frontend restaurant-backend-dev-images restaurant-backend-dev-profile-images restaurant-backend-dev-swagger-ui; do
  empty_bucket "$b-$ACC"
done

# 2. Backend (лямбди, API Gateway, DynamoDB, ролі)
ensure_syndicate
( cd "$BE_DIR" && syndicate clean ) || warn "syndicate clean завершився з помилкою — перевір вручну"

# 3. Спільна інфраструктура
( cd "$TF_DIR" && terraform init -input=false >/dev/null && terraform destroy -input=false -auto-approve )

# 4. Службові бакети
if [[ $ALL -eq 1 ]]; then
  purge_versions "restaurant-terraform-state-$ACC"
  aws s3api delete-bucket --bucket "restaurant-terraform-state-$ACC" --region "$AWS_REGION" || true
  empty_bucket "marathon2026-restaurant-artifacts-$ACC"
  aws s3api delete-bucket --bucket "marathon2026-restaurant-artifacts-$ACC" --region "$AWS_REGION" || true
fi
log "Готово. Перевір Billing → Bills, що нічого не лишилось."

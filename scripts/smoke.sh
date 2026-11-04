#!/usr/bin/env bash
# Швидка перевірка, що задеплоєне живе: API відповідає, CloudFront віддає SPA (включно з deep link).
source "$(dirname "$0")/lib.sh"

API="$(api_url)" || die "API не знайдено — backend не задеплоєно"
CF="$(cd "$TF_DIR" && terraform output -raw cloudfront_distribution_domain_name)"

fail=0
probe() { # назва, url
  local c; c="$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 "$2")"
  if [[ "$c" =~ ^2 ]]; then printf '  \033[32m✔\033[0m %s %-22s %s\n' "$c" "$1" "$2"
  else printf '  \033[31m✘\033[0m %s %-22s %s\n' "$c" "$1" "$2"; fail=1; fi
}

probe "API locations"      "$API/locations"
probe "API popular dishes" "$API/dishes/popular"
probe "Frontend /"         "https://$CF/"
probe "Frontend deep link" "https://$CF/reservations/smoke-check/group-order"

echo
echo "Дані в API: $(curl -s --max-time 20 "$API/locations" | head -c 200)"
exit $fail

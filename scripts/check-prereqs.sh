#!/usr/bin/env bash
# Перевіряє, що на машині є все для деплою. Нічого не встановлює — лише підказує команду.
source "$(dirname "$0")/lib.sh"
set +e
fail=0

check() { # назва, команда, підказка
  local out
  if out="$(eval "$2" 2>/dev/null)" && [[ -n "$out" ]]; then
    printf '  \033[32m✔\033[0m %-11s %s\n' "$1" "$(echo "$out" | head -1)"
  else
    printf '  \033[31m✘\033[0m %-11s → %s\n' "$1" "$3"; fail=1
  fi
}

echo "Інструменти:"
check aws        "aws --version"                                   "brew install awscli"
check terraform  "terraform version | head -1 | grep -E 'v1\.(1[0-9]|[2-9][0-9])'" \
                                                                   "brew tap hashicorp/tap && brew install hashicorp/tap/terraform  (потрібна ≥ 1.10)"
check dotnet10   "dotnet --list-sdks | grep -E '^10\.'"            "brew install --cask dotnet-sdk  (потрібен .NET 10 SDK)"
check node       "node --version | grep -E '^v(22|24)\.'"          "nvm install 22  (див. frontend/restaurant-frontend-app/.nvmrc)"
check npm        "npm --version"                                   "ставиться разом з node"
check python3.11 "python3.11 --version || python3.10 --version"    "brew install python@3.11  (Syndicate працює лише на 3.10–3.11)"
check git        "git --version"                                   "xcode-select --install"

echo "AWS доступ (регіон $AWS_REGION):"
check identity   "aws sts get-caller-identity --query Arn --output text" \
                                                                   "aws configure  (Access Key IAM-користувача, region us-west-2)"
exit $fail

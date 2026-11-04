---
name: deploy-app
description: Задеплоїти Green & Tasty у власний AWS-акаунт (Terraform, Syndicate, Angular → S3/CloudFront) і перевірити, що він живий. Використовуй після merge фічі в main або коли користувач просить задеплоїти.
---

# Деплой

## Перед деплоєм
1. `git branch --show-current` → `main`, `git status` → чисто. Інакше — зупинись і скажи користувачу.
2. `aws sts get-caller-identity` → account ID збігається з тим, що в `infra/terraform/backend.tf` (`restaurant-terraform-state-<ID>`). Якщо ні — неправильний `AWS_PROFILE`, зупинись.
3. Перший деплой акаунта: `scripts/check-prereqs.sh`, потім `scripts/configure-account.sh <email>`.

## Що запускати
| Змінилось | Команда |
|---|---|
| `backend/**` | `scripts/deploy.sh backend` |
| `frontend/**` | `scripts/deploy.sh frontend` |
| обидва | `scripts/deploy.sh backend frontend` |
| `infra/terraform/**` | `scripts/deploy.sh infra` — **покажи користувачу підсумок плану (add/change/destroy) і чекай явного «так»** |
| перший деплой | `scripts/deploy.sh all` |

Новий ендпоінт не з'явився після `syndicate update` → `scripts/deploy.sh backend --redeploy`.

## Після деплою
1. `scripts/smoke.sh` — усе ✔.
2. Якщо є `scripts/e2e-*.sh` для фічі — прожени з `API_URL=$(scripts/deploy.sh outputs | awk '/API/{print $3}')`.
3. CloudFront оновлюється 1–3 хв після invalidation — перевіряй UI після паузи.
4. Онови `~/EPAM/<папка проєкту>/DEPLOYMENT.local.md` (URL-и, дата), якщо щось змінилось.

## Ніколи без явного дозволу користувача
`scripts/destroy.sh`, `terraform destroy`, `syndicate clean`, видалення бакетів, таблиць DynamoDB чи IAM-ресурсів.

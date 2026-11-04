# Green & Tasty — монорепо IT Marathon 2026

Ресторанний застосунок: бронювання столиків, меню, груповий pre-order. Читай `CLAUDE.md` у підпапці, з якою працюєш.

| Папка | Що там | Стек |
|---|---|---|
| `infra/terraform` | спільна AWS-інфраструктура: Cognito, S3, CloudFront, SQS, EventBridge, алерти, Budget | Terraform ≥ 1.10, AWS provider 5 |
| `backend/restaurant-backend-app` | 3 лямбди (`api-handler`, `reports-handler`, `report-sender`), API Gateway, DynamoDB | .NET 10, AWS Syndicate |
| `frontend/restaurant-frontend-app` | SPA, хоститься в S3 за CloudFront | Angular 22, signals |
| `aqa/restaurant-aqa-framework` | e2e/API тести | .NET 10, Reqnroll (BDD), Playwright, Screenplay |
| `scripts/` | деплой: `check-prereqs.sh`, `configure-account.sh`, `deploy.sh`, `smoke.sh`, `destroy.sh` | bash |

Умови домашок: `homework-backend.md`, `homework-frontend.md` (+ `practice-frontend.md`, `homework-design/`), `homework-aqa.md`.

## Правила

- Регіон — **us-west-2** (захардкожений у Terraform, Syndicate і CI).
- Деплой лише через `scripts/deploy.sh <крок>`. Порядок: `state → infra → backend → frontend`.
- **Ніколи без явного дозволу користувача:** `scripts/destroy.sh`, `terraform destroy`, `syndicate clean`, видалення бакетів чи таблиць DynamoDB, зміни IAM поза репозиторієм.
- Не коміть секрети: `.env`, `*.tfvars`, access keys, паролі тестових акаунтів. Account ID не секрет, email — у `terraform.tfvars` (gitignored).
- Імена ресурсів фіксовані (`api-handler`, `restaurant-backend-api`, `restaurant-backend-user-pool`, таблиці з `syndicate_aliases.yml`) — Terraform посилається на них за іменем, не перейменовуй.
- Новий HTTP-ендпоінт = 3 місця: хендлер у `Handlers/`, маршрут у `Routing/RouteRegistry.cs`, ресурс у `deployment_resources.json` (`enable_cors`, `authorization_type`, `lambda_name: api-handler`, `enable_proxy: true`).
- Коміти — Conventional Commits (`feat(backend): …`, `fix(frontend): …`); AQA-хуки це перевіряють.
- Готово = тести зелені + перевірки з відповідної домашки пройдені + коротко описано, що і як перевірено.

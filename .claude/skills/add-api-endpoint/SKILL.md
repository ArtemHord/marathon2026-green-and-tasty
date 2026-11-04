---
name: add-api-endpoint
description: Покроково додати новий HTTP-ендпоінт у бекенд Green & Tasty (хендлер + маршрут + API Gateway ресурс + тест + документація). Використовуй щоразу, коли фіча потребує нового методу API.
---

# Додати ендпоінт до `api-handler`

Усі шляхи — від `backend/restaurant-backend-app/dnapp/lambdas/api-handler/`, якщо не сказано інше.

## 1. Хендлер — `Handlers/<Область>/<Дія>Handler.cs`
Реалізує `IApiRequestHandler`. Еталон: `Handlers/GuestSessions/DeleteGuestSessionHandler.cs`.
- path-параметри: `request.TryGetPathValue("id", out var id)` → 400, якщо немає;
- користувач: `request.RequestContext?.Authorizer?.Claims["sub"]` → 401, якщо немає;
- тіло: `JsonSerializer.Deserialize<…>(request.Body)` → 400 на невалідний JSON;
- бізнес-логіка — лише в сервісі (`Services/<Область>/`), хендлер тільки мапить винятки на коди:
  `*NotFoundException` → 404, `ActionForbiddenException` → 403, `ValidationException` → `ResponseBuilder.CreateErrorCodeResponse(ex)`;
- успіх: `ResponseBuilder.CreateSuccessResponse(dto)`.

## 2. Маршрут — `Routing/RouteRegistry.cs`
Додай `new RouteHandler(new Route(HttpMethods.<M>, "/шлях/{param}"), ActivatorUtilities.CreateInstance<…Handler>(provider))` у відповідний список (`GetGuestAndGroupRoutes`, `GetReservationAndOrderRoutes`, …). Новий сервіс — зареєструй у DI вище (`services.AddSingleton<I…, …>()`).

## 3. API Gateway — `backend/restaurant-backend-app/deployment_resources.json`
У `"${api_gateway_name}" → "resources"` додай (або доповни наявний шлях новим методом):
```json
"/шлях/{param}": {
  "enable_cors": { "state": true },
  "GET": {
    "authorization_type": "cognito_authorizer",
    "integration_type": "lambda",
    "lambda_name": "api-handler",
    "api_key_required": false,
    "method_request_parameters": {},
    "integration_request_body_template": {},
    "responses": [],
    "integration_responses": [],
    "default_error_pattern": true,
    "enable_proxy": true
  }
}
```
`"authorization_type": "NONE"` — лише для свідомо публічних ендпоінтів (гостьові сесії з токеном, меню). Без цього кроку ендпоінт працює в юніт-тестах, але після деплою повертає 403 `Missing Authentication Token`.

## 4. Тести — `backend/restaurant-backend-app/Tests/Services/<Сервіс>Tests.cs`
У стилі наявних тестів: happy path + кожен код помилки + граничні значення. `cd backend/restaurant-backend-app && dotnet test` — exit 0.

## 5. Документація
- `backend/restaurant-backend-app/docs/ApiRoutes.md` — рядок з методом, шляхом, доступом, кодами помилок;
- `backend/restaurant-backend-app/swagger_src/swagger-ui/swagger-endpoints.yaml` — шлях, параметри, схеми запиту/відповіді.

## Чекліст
- [ ] хендлер · [ ] маршрут · [ ] DI · [ ] `deployment_resources.json` · [ ] тести зелені · [ ] `ApiRoutes.md` · [ ] swagger

# Груповий pre-order для Owner

Специфікації: `agent-tasks/A2-backend.md` (backend L1–L3), `agent-tasks/A3-frontend.md` (frontend practice, L1–L3, бонус). Умови: `homework-backend.md`, `homework-frontend.md`, `practice-frontend.md`, `homework-design/`.

## Сценарій
1. Customer (Owner) бронює столик на 2+ гостей, отримує invite token (`GET /reservations/{id}/invite-token`) і ділиться посиланням.
2. Гості відкривають `/guest/invites/{token}`, створюють guest-сесії, додають страви, сабмітять кошик.
3. Owner відкриває `/reservations/{reservationId}/group-order`: бачить учасників, їхні страви й суми, свій «My Pre-Order», консолідований Grand Total (лише submitted).
4. Owner може видалити гостя (бонус) і підтвердити груповий pre-order → страви йдуть в order ресторану, екран стає read-only («Sent to restaurant»).
5. Поки група `Open`, екран оновлюється кожні 15 с (L3).

## Рішення

### Контракт API (спільний для backend-dev і frontend-dev)
Усі ендпоінти нижче — під `cognito_authorizer`, `enable_cors: true`, `lambda_name: api-handler`, `enable_proxy: true`.
Owner = customer, якому належить reservation, до якої прив'язана group reservation. Помилки: 401/403/404 і валідація в хендлері — `{ "error": "…" }` (`CreateErrorResponse`); 400 бізнес-правил — `{ "message": "…", "errorCode": "…" }` (`CreateErrorCodeResponse`). `tokenInterceptor` фронтенду читає обидва ключі.

#### Participant / snapshot
```json
{
  "participants": [
    {
      "id": "session1",
      "type": "Owner | Guest",
      "name": "Andrii",
      "items": [ { "dish": { "id": "dish1", "name": "Caesar Salad", "price": 180 }, "quantity": 2 } ],
      "total": 360,
      "isSubmitted": true
    }
  ],
  "total": 580
}
```
- `type` = `"Owner"` для сесії з `IsOwnerSession = true`, інакше `"Guest"` (фікс бага L3: раніше всі були `Guest`).
- `participant.total` = Σ `price × quantity`; `snapshot.total` = Σ `participant.total` **усіх** учасників (submitted і ні), як у прикладі ДЗ (360 + 220 = 580).
- Порожня група → `{ "participants": [], "total": 0 }`.

| Метод і шлях | Успіх | Помилки |
|---|---|---|
| `GET /guest/groups/{groupReservationId}/sessions` (L1) | 200 snapshot | 401 без токена · 403 не Owner · 404 немає group reservation (або її reservation) |
| `GET /reservations/{reservationId}/guest-session-id` (L2) | 200 `{ "guestSessionId": "…" }` | 401 · 403 не Owner · 404 немає reservation · 404 немає group reservation / owner-сесії |
| `POST /group-reservations/{groupReservationId}/submit` (L2), body `{ "reservationId": "…" }` | 200 `[ { "dishId": "dish1", "quantity": 3 } ]` — об'єднані по `dishId` страви submitted-сесій | 401 · 403 не Owner · 404 немає reservation / group reservation · 400 body без `reservationId` або group належить іншій reservation · 400 повторний submit (група вже не `Open`): `"Group pre-order has already been submitted."` · 400 немає submitted-сесій · 400 у submitted-сесіях немає страв · 400 немає order |
| `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` (L3) | 200 оновлений snapshot | 401 · 403 не Owner · 404 немає group reservation · 404 guest session не знайдена · 400 сесія з іншої групи · 400 спроба видалити owner-сесію |

Наявні ендпоінти, які використовує фронтенд: `GET /reservations/{id}/invite-token`, `GET /guest/invites/{token}`, `GET /guest/groups/{id}/status`. Після раунду tester змінено (щоб екран Owner відкривався, коли група повна / `Submitted` / `Closed`, зокрема після перезавантаження):
- `GET /reservations/{id}/invite-token` повертає токен за будь-якого статусу групи; немає групи → 404 (було 500).
- `GET /guest/invites/{token}` більше не дає 400 для повної / не-Open групи; у відповідь додано `Status` і `CanJoin` (наявні поля без змін). Захист входу гостя (ліміт, `Submitted`, `Closed`, бронювання не `Reserved`) лишився в `POST /guest/sessions`.
- Snapshot після DELETE відфільтровує видалену сесію (GSI eventually consistent); owner-сесію, якої ще немає в GSI, дочитує за id.
- Фронтенд: owner-сесія не рахується в «N participant(s) hasn't submitted»; 404 `guest-session-id` без toast; після 400 на submit — один тихий перезапит.

Етап «безпека й демо»:
- `GET /reservations/{id}/invite-token` — лише Owner: без `sub` → 401, інший customer → 403 (було: будь-який автентифікований користувач).
- Snapshot (`GET …/sessions` і після `DELETE`): учасник `Owner` бере `items` з order бронювання (не `Cancelled`; без рядків `Canceled` і з quantity ≤ 0),
  `total` = Σ рядків, `isSubmitted` = order є і має страви (інакше `false`, `items: []`). Для не-Open групи з order віднімаються
  `submittedGuestDishes` групи (по `dishId`, рядки ≤ 0 відкидаються), тож після submit Owner показує лише свої страви, а консолідація = order.
- Submit пропускає owner-сесію при об'єднанні; відповідь — лише страви гостей, страви Owner у order не дублюються.

#### Owner-сесія (рішення під час реалізації)
У наявному коді owner-сесія ніде не створювалась (`CreateGroupOwnerSessionAsync` не викликався), тому `guest-session-id` завжди давав би 404. Рішення: `GET /reservations/{id}/guest-session-id` ліниво створює owner-сесію, поки група `Open`. Id детермінований (`owner-<groupReservationId>`), тож повторні й паралельні виклики дають той самий id. 404 лишається, лише якщо групи немає або група не `Open` і owner-сесії немає. Owner-сесія не рахується як гість у `/status` (owner, як і раніше, +1). Власні страви Owner лежать у звичайному order (`POST /orders`); submit групи додає до нього страви гостей. Owner-сесія без `isSubmitted` у submit не потрапляє.

#### Submit — порядок дій
1. Знайти reservation (404), group reservation (404), перевірити Owner (403), відповідність `reservationId` (400), статус групи `Open` (`Submitted` → 400 «already submitted», `Closed` → 400 «Group reservation is closed.»).
2. Взяти лише guest-сесії з `isSubmitted = true` (owner-сесію пропускаємо — страви Owner уже в order, інакше задублюються); зібрати їхні items в один список і об'єднати по `dishId` (сума quantity).
3. Знайти order бронювання. **Owner зі стравами в order** (order не `Cancelled`, є рядок не `Canceled` з quantity > 0) **рахується як submitted-учасник**:
   немає submitted-гостей і в Owner немає страв → 400 «no submitted sessions»; у submitted-гостей немає страв і в Owner немає страв → 400 «no dishes»;
   немає order (або `Cancelled`) → 400; страв гостей немає, але є order Owner → submit проходить, відповідь `[]`.
4. Додати страви гостей в order (якщо страва вже є — збільшити quantity; змінені рядки → `state = Pending`), order → `SubmittedByCustomer`;
   group reservation → `Submitted` тим самим записом (з перевіркою версії), що й збереження `submittedGuestDishes` (об'єднаний список гостей); компенсація (reopen) його очищає.
5. Відповідь — об'єднаний список з п. 2 (лише страви гостей). Захист від гонки: див. «Обмеження» і `docs/ApiRoutes.md` «Concurrent submits».

### Frontend
- Сервіс `GroupOrdersService`: `getGroupInvite$`, `getSessions$`, `getOwnerSessionId$`, `getStatus$`, `submit$`, `removeSession$` — URL'и точно як у тестах `homework-tests/`.
- Тексти UI — дослівно з `homework-frontend.md` / `practice-frontend.md`; макети — `homework-design/`.
- Grand Total — через `consolidatePreOrder()` (лише submitted): $540, а не $760.
- L3 перевіряється моками й юніт-тестами (`HttpTestingController`, fake timers); справжній `backendUrl` не комітиться.

### Authorization
Аудит усіх ендпоінтів групового pre-order (authorizer у `backend/restaurant-backend-app/deployment_resources.json` + перевірка в коді;
authorizer'и закріплені тестом `Tests/Services/AuthorizationConfigTests.cs`). Owner = customer, якому належить reservation.

| Endpoint | Who has access | How it is checked |
|---|---|---|
| `GET /reservations/{reservationId}/invite-token` | Owner (customer of the reservation) | `cognito_authorizer` (no/invalid token → 401 from API Gateway) + handler: no `sub` → 401; service `GetOwnerInviteTokenAsync`: `reservation.customerId == sub`, else 403 (also for reservations without a customer); 404 reservation / group |
| `GET /reservations/{reservationId}/guest-session-id` | Owner | `cognito_authorizer` + handler `sub` (401) + `EnsureOwner` in `GetOwnerGuestSessionIdAsync` (403) |
| `GET /guest/groups/{groupReservationId}/sessions` | Owner | `cognito_authorizer` + handler `sub` (401) + `ValidateReservationOwnerAsync` (group → reservation → `customerId == sub`, 403) |
| `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` | Owner | `cognito_authorizer` + handler `sub` + `ValidateReservationOwnerAsync` (403); session must belong to the group (400), owner session cannot be removed (400) |
| `POST /group-reservations/{groupReservationId}/submit` | Owner | `cognito_authorizer` + handler `sub` + `EnsureOwner` on the reservation from the body (403); the group must belong to that reservation (400) |
| `GET /guest/invites/{token}` | Anyone with the invite token (anonymous) | `NONE` (public); the 32-hex invite token is the secret; unknown token → 404 |
| `POST /guest/sessions` | Anyone who knows the `groupReservationId` (anonymous guest) | `NONE`; join rules in `CreateGuestSessionAsync` (group Open, reservation Reserved, free seat); returns a random 32-byte session token, only its SHA-256 is stored |
| `GET /guest/sessions/{guestSessionId}`, `POST/DELETE …/dishes`, `GET …/menu`, `POST …/submit` | The guest holding **that** session's token | `NONE` + `GuestAuthorizationService`: `Authorization: Bearer <session token>` (header name case-insensitive), SHA-256 of the token is looked up and must belong to the session in the path — another guest's token → 401; missing/malformed header or token (incl. a Cognito JWT) → 401. The owner session has no token, so it cannot be modified through these endpoints |
| `GET /guest/groups/{groupReservationId}/status` | Anyone (anonymous) | `NONE`, no check — returns only status, participant count and capacity |

Знайдені й закриті діри:
- `GET /reservations/{id}/invite-token` — IDOR: будь-який автентифікований користувач отримував invite token чужого бронювання → тепер лише Owner (403).
- `GuestAuthorizationService`: запит без заголовків → `NullReferenceException` (500); токен не в base64 (напр. Cognito JWT) → `FormatException` (500);
  заголовок `authorization` у нижньому регістрі не розпізнавався → тепер 401 / регістр не важливий.
- Owner-перевірка в `GET/DELETE …/sessions` пропускала бронювання без customer, якщо `sub` збігався з `null` — тепер явно 403.

Свідомо не змінювали (документовано): `POST /guest/sessions` приймає `groupReservationId`, а не invite token (так працює наявний гостьовий UI);
`GET /guest/groups/{id}/status` публічний.

## Що відкинули
- Окремий ендпоінт «unsubmit» групи і повторний submit як merge — немає в ДЗ; повторний submit → 400.
- WebSocket/push замість polling — новий AWS-сервіс, не просили.
- Заборона DELETE гостя після submit на бекенді — ДЗ не вимагає, не додаємо; фронтенд після submit read-only і кнопок Remove не показує.

## Як перевірити
```bash
(cd backend/restaurant-backend-app && dotnet test)
(cd frontend/restaurant-frontend-app && npx tsc -p tsconfig.app.json --noEmit && npx tsc -p tsconfig.spec.json --noEmit \
  && BACKEND_URL=https://example.com npm run build && npm run test:hw \
  && npx ng test --watch=false --include src/app/auth/token.interceptor.spec.ts)
# після деплою бекенду:
API_URL=… CUSTOMER_EMAIL=… CUSTOMER_PASSWORD=… OTHER_CUSTOMER_EMAIL=… OTHER_CUSTOMER_PASSWORD=… scripts/e2e-group-preorder.sh
```
Демо екрана без бекенду: `npm start` → `http://localhost:4200/reservations/demo-reservation/group-order?demo`.

## Обмеження
- Після submit кнопка «Submit Pre-Order» прихована, а не неактивна, як на `02-sent-to-restaurant.png`: так вимагає тест L2 з `homework-tests` («кнопки Submit більше немає»).
- ~~Картка «My Pre-Order» порожня на справжньому API~~ — закрито на етапі «безпека й демо»: Owner у snapshot бере страви з order; якщо owner-сесії ще немає, а в order є страви, Owner усе одно повертається з id `owner-<groupReservationId>` (той, що віддасть `GET guest-session-id`).
- `DELETE` гостя після submit: його страви лишаються в order і в `submittedGuestDishes` групи, але зникають зі snapshot (UI після submit кнопки Remove не показує).
- ~~`GET /reservations/{id}/invite-token` без перевірки Owner~~ — закрито: лише Owner (403 для інших), див. «Authorization».
- Бекенд ще не задеплоєно; `scripts/e2e-group-preorder.sh` написаний, але на реальному API не проганявся (крок 7 A2 — після merge і дозволу людини).
- Інтеграцію фронтенду L3 зі справжнім API не перевірено — лише моки/юніт-тести.
- Submit без DynamoDB-транзакції: спершу умовний перехід групи `Open → Submitted` (`[DynamoDBVersion]` на `GroupReservation`), потім запис order з перевіркою версії (`[DynamoDBVersion]` на `Order`); два одночасні submit → рівно один 200, другий 400 «already submitted». Якщо order змінили паралельно (Owner/waiter) — групу повертаємо в `Open` (теж з перевіркою версії) і відповідаємо 400 «The pre-order was changed while submitting. Please try again.»; інша помилка запису order — та сама компенсація й помилка далі; якщо впаде і компенсація — група лишиться `Submitted` без страв гостей. Конфлікт версій у звичайних флоу order (edit/cancel/waiter confirm) дає 409 «The resource was changed concurrently. Please retry.» — див. `backend/restaurant-backend-app/docs/ApiRoutes.md` «Known limitations». Умовні записи перевірені лише юніт-тестами з фейками, не на справжньому DynamoDB.

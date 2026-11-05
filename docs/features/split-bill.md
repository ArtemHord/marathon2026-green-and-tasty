# Split bill — «хто що замовив» для офіціанта і розділений рахунок

Специфікація: `agent-tasks/A7-split-bill.md`. Залежить від групового pre-order (`docs/features/group-preorder.md`).

## Сценарій
3 гості (двоє беруть Caesar) → submit кошиків → Owner відправляє груповий pre-order → офіціант бачить «Caesar Salad ×3 — Andrii ×2, Olena ×1» → офіціант скасовує одну страву → у Owner блок Split bill перераховується → Copy summary.

## Рішення

### Дані
- `OrderItem.Guests: List<OrderItemGuestShare>?` (`GuestSessionId`, `GuestName`, `Quantity`), nullable — старі замовлення читаються як раніше (`Guests == null`).
- Заповнюється лише груповим submit (`POST /group-reservations/{id}/submit`). Для кожного `dishId`, якого торкається submit:
  - частки submitted-гостей (по одній на сесію, quantity з кошика);
  - якщо в order уже була ця страва від Owner (і в рядка ще немає `Guests`) — частка Owner: `GuestSessionId = owner-<groupReservationId>`, `GuestName` = ім'я Owner (як у snapshot), `Quantity` = кількість, що була до submit.
  - Інваріант: Σ `Guests[].Quantity` == `Quantity` рядка, якщо `Guests != null`.
- Рядки order, яких submit не торкався (лише страви Owner), лишаються з `Guests == null`; у bill-split вони йдуть Owner-у.
- **Відповідь submit не змінюється** (`[{dishId, quantity}]`).

### API
`GET /orders/{reservationId}` — у кожну позицію додається `guests: [{ guestSessionId, guestName, quantity }]`, лише якщо `Guests != null` (інакше поле відсутнє або `null`; наявні поля без змін).

`GET /reservations/{reservationId}/bill-split` (новий; `cognito_authorizer`, `enable_cors`, `api-handler`, `enable_proxy`):
```json
{ "participants": [ { "guestSessionId": "s1", "name": "Andrii", "type": "Guest",
    "items": [ { "dishId": "d1", "name": "Caesar Salad", "quantity": 2, "price": 180, "subtotal": 360 } ],
    "total": 360 } ],
  "total": 580, "excludedCanceled": 0 }
```
- Доступ: customer-власник бронювання (`Reservation.CustomerId == sub`) **або** призначений офіціант (`Reservation.WaiterId == sub`); інакше 403; без токена 401; немає reservation → 404; немає order (або `Cancelled`) → 404.
- Учасники: по одному на `GuestSessionId` з `Guests`; Owner — `type: "Owner"`, `guestSessionId: "owner-<gid>"` (або `null`, якщо бронювання без групи), отримує свої частки + усі рядки з `Guests == null`. Порядок: Owner першим, далі гості в порядку першої появи.
- Рядки зі станом `OrderItemState.Canceled` не входять у суми й у `items`; `excludedCanceled` = кількість таких рядків order.
- `price` — поточна ціна з `Dishes`; `subtotal = price × quantity`; `total` учасника = Σ subtotal; `total` = Σ total учасників. Страва, видалена з каталогу, — пропускається (і рахується в обмеженнях).
- Order без жодних `Guests` (звичайний pre-order) → один учасник Owner з усіма рядками.

### Frontend
- Сторінка офіціанта `pages/waiter-pre-order-page`: під позицією рядок `Andrii ×2 · Olena ×1` (якщо `guests` є і не порожні).
- Екран Owner у стані «Sent to Restaurant»: блок **«Split bill»** — ім'я, страви (`Caesar Salad ×2`), сума на учасника, загальна сума; кнопка **«Copy summary»** → `navigator.clipboard.writeText`, один рядок на учасника `Andrii — $360.00 (Caesar Salad ×2)`, далі `Total — $580.00`; toast «Copied». Стани loading / error («Couldn't load the split bill.» + Try again) / порожній.
- `GroupOrdersService.getBillSplit$(reservationId)`; стан — signals.

## Що відкинули
- Окрема таблиця BillSplits — зайва, дані вже в order.
- Зміна відповіді submit — ламає контракт домашки.
- Історичні ціни в order — потребує міграції; беремо поточні ціни (див. обмеження).

## Як перевірити
```bash
(cd backend/restaurant-backend-app && dotnet test)
(cd frontend/restaurant-frontend-app && npx tsc -p tsconfig.app.json --noEmit && npx tsc -p tsconfig.spec.json --noEmit \
  && BACKEND_URL=https://example.com npm run build && npm run test:hw)
API_URL=… CUSTOMER_EMAIL=… CUSTOMER_PASSWORD=… WAITER1_EMAIL=… WAITER1_PASSWORD=… WAITER2_EMAIL=… WAITER2_PASSWORD=… OTHER_CUSTOMER_EMAIL=… OTHER_CUSTOMER_PASSWORD=… scripts/e2e-split-bill.sh
```

## Обмеження
- Ціни поточні: якщо ціну страви змінили після замовлення, розбивка покаже нову ціну.
- Скасування waiter-ом діє на весь рядок (`dishId`), тому з сум виключаються частки всіх гостей цього рядка.
- Учасник, усі рядки якого скасовано, лишається в розбивці з `items: []` і `total: 0` (видно, чий рахунок обнулився); Owner без рядків не показується; бронювання без групи → `guestSessionId: null`.
- `PUT /orders/{reservationId}` (редагування customer/waiter) перебудовує рядки без `Guests` — після такого редагування весь order рахується на Owner.
- Доступ до bill-split суворіший за `GET /orders`: лише призначений офіціант (`Reservation.WaiterId`), а не будь-який.
- Порядок гостей усередині рядка не гарантований (GSI); між рядками — за першою появою.
- Блок Split bill вантажиться один раз при появі (polling після submit зупинений): щоб побачити скасування страви офіціантом, Owner перезавантажує сторінку.
- `guests` (імена й `guestSessionId`) видно в `GET /orders/{reservationId}` усім, хто має доступ до цього ендпоінта (зокрема будь-якому waiter); `guestSessionId` без токена сесії нічого не дає.

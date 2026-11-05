#!/usr/bin/env bash
# Регресія звичайного pre-order після [DynamoDBVersion] на Order (optimistic locking) проти задеплоєного API.
#
# Кожен save order — умовний запис за версією, а order читається через GSI_ReservationId + ConsistentRead за ключем.
# Скрипт перевіряє, що послідовні записи одного order (customer: create → edit → edit → submit;
# waiter: confirm → confirm одразу підряд) НЕ дають 409 і жоден запис не губиться.
#
# Флоу:
#   1. signin customer і waiter-ів; бронювання (випадкова дата з повторними спробами) у локації waiter-а;
#      визначити призначеного waiter-а (waiterId з відповіді POST /reservations = sub з idToken waiter-а).
#   2. customer POST /orders (A×1, B×1) → PUT /orders/{reservationId} (A×2, B×1) → одразу PUT (A×2, B×3).
#   3. customer POST /orders/{reservationId}/submit — для підтвердження waiter-ом НЕ обов'язковий
#      (ConfirmPreOrderDishAsync статус order не перевіряє), але це реальний флоу і ще один записаний за версією save.
#   4. waiter PATCH /reservations/{id}/order/confirmations/{A}, одразу …/{B}.
#   5. GET /orders/{reservationId}: A×2, B×3; статус Submitted (1) — він ставиться, лише коли ОБИДВА рядки
#      Confirmed (стан рядків API не віддає, тому «обидва підтверджені» перевіряється через статус order:
#      після першого confirm статус ще SubmittedByCustomer (3), після другого — Submitted (1)).
#   6. cleanup: trap скасовує бронювання (DELETE /reservations).
# Будь-який 409 → окреме зрозуміле повідомлення про конфлікт версії з тілом відповіді.
#
# Env (нічого не хардкодиться):
#   API_URL, CUSTOMER_EMAIL, CUSTOMER_PASSWORD, WAITER1_EMAIL, WAITER1_PASSWORD
# Необов'язкові:
#   WAITER2_EMAIL, WAITER2_PASSWORD   другий waiter (якщо бронювання дістанеться йому)
#   LOCATION_ID                       локація (за замовчуванням — локація WAITER1 з GET /waiters/{sub}/location)
#   E2E_KEEP_RESERVATION=1            не скасовувати бронювання наприкінці
#
# Використання:
#   API_URL=… CUSTOMER_EMAIL=… CUSTOMER_PASSWORD=… WAITER1_EMAIL=… WAITER1_PASSWORD=… scripts/e2e-preorder-regression.sh
set -euo pipefail

# ---------- вивід ----------
log()  { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
ok()   { printf '  \033[32m✔\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2; exit 1; }

# ---------- env ----------
command -v curl >/dev/null || die "Потрібен curl"
command -v jq   >/dev/null || die "Потрібен jq (brew install jq)"

missing=()
for v in API_URL CUSTOMER_EMAIL CUSTOMER_PASSWORD WAITER1_EMAIL WAITER1_PASSWORD; do
  [[ -n "${!v:-}" ]] || missing+=("$v")
done
(( ${#missing[@]} == 0 )) || die "Не задано змінні оточення: ${missing[*]}"
if [[ -n "${WAITER2_EMAIL:-}" && -z "${WAITER2_PASSWORD:-}" ]]; then die "Задано WAITER2_EMAIL без WAITER2_PASSWORD"; fi

API="${API_URL%/}"
GUEST_COUNT=1   # без групового бронювання: перевіряємо лише звичайний pre-order

# ---------- HTTP ----------
HTTP_CODE=""; HTTP_BODY=""
# req METHOD PATH [JSON_BODY] [AUTHORIZATION_TOKEN] → HTTP_CODE, HTTP_BODY
req() {
  local method="$1" path="$2" body="${3:-}" token="${4:-}" out
  local args=(-sS --max-time 30 -X "$method" -H 'Content-Type: application/json' -w $'\n%{http_code}')
  [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
  [[ -n "$body" ]] && args+=(--data "$body")
  out="$(curl "${args[@]}" "$API$path")" || die "curl $method $path впав"
  HTTP_CODE="${out##*$'\n'}"
  HTTP_BODY="${out%$'\n'*}"
}
# expect "<коди через пробіл>" "<що робили>"; 409 = конфлікт версії order → окреме повідомлення
expect() {
  local c; for c in $1; do [[ "$HTTP_CODE" == "$c" ]] && return 0; done
  if [[ "$HTTP_CODE" == 409 ]]; then
    die "$2 → HTTP 409: конфлікт версії order (optimistic locking) на ПОСЛІДОВНОМУ записі — регресія [DynamoDBVersion]. Тіло: $HTTP_BODY"
  fi
  die "$2 → очікував HTTP $1, отримав $HTTP_CODE: $HTTP_BODY"
}
# jq-опції з двома значеннями (--arg name value, --argjson name json) можна передати ПЕРЕД фільтром:
#   assert_jq [--arg n v …] '<jq-фільтр, що повертає true>' "<опис>" [jq-аргументи…]
JQ_OPTS=()
take_jq_opts() { # збирає провідні --arg/--argjson у JQ_OPTS; повертає кількість спожитих аргументів у JQ_SHIFT
  JQ_OPTS=(); JQ_SHIFT=0
  while [[ "${1:-}" == --arg || "${1:-}" == --argjson ]]; do
    JQ_OPTS+=("$1" "$2" "$3"); shift 3; JQ_SHIFT=$((JQ_SHIFT + 3))
  done
}
assert_jq() {
  take_jq_opts "$@"; shift "$JQ_SHIFT"
  local filter="$1" what="$2"; shift 2
  jq -e ${JQ_OPTS[@]+"${JQ_OPTS[@]}"} "$@" "$filter" <<<"$HTTP_BODY" >/dev/null || die "$what — не виконано. Тіло: $HTTP_BODY"
  ok "$what"
}
# Тіло помилки: 401/403/404/409 → {"error": …}, 400 бізнес-правил → {"message": …, "errorCode": …}
err_msg() { jq -r '.message // .error // .Message // empty' <<<"$HTTP_BODY" 2>/dev/null || true; }

# ---------- auth ----------
signin() { # EMAIL PASSWORD → idToken
  req POST /signin "$(jq -n --arg e "$1" --arg p "$2" '{email:$e, password:$p}')"
  expect 200 "POST /signin $1"
  local t; t="$(jq -r '.idToken // empty' <<<"$HTTP_BODY")"
  [[ -n "$t" ]] || die "POST /signin $1 не повернув idToken"
  echo "$t"
}
jwt_sub() { # idToken → sub (payload base64url)
  local p; p="$(cut -d. -f2 <<<"$1" | tr '_-' '/+')"
  while (( ${#p} % 4 )); do p+="="; done
  base64 --decode <<<"$p" 2>/dev/null | jq -r '.sub // empty'
}

# ---------- дати ----------
future_utc() { # DAYS HOUR → YYYY-MM-DDTHH:00:00Z (BSD або GNU date)
  date -u -v+"$1"d -v"$2"H -v0M -v0S +%Y-%m-%dT%H:%M:%SZ 2>/dev/null \
    || date -u -d "$(date -u -d "+$1 days" +%Y-%m-%d) $2:00:00" +%Y-%m-%dT%H:%M:%SZ
}
plus_90_min() { # ISO → ISO + 90 хв
  date -u -j -f %Y-%m-%dT%H:%M:%SZ -v+90M "$1" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null \
    || date -u -d "$1 + 90 minutes" +%Y-%m-%dT%H:%M:%SZ
}

# GET /orders/{reservationId} → {dishId: quantity}; status — число (OrderStatus без JsonStringEnumConverter):
#   0 Created, 1 Submitted (усі рядки Confirmed/Canceled і хоч один Confirmed), 2 Cancelled, 3 SubmittedByCustomer
ORDER_QTY_FILTER='[.orderItems[] | {key: .dish.id, value: .quantity}] | from_entries'

RESERVATION_ID=""
# Скасовує створене бронювання (curl напряму, щоб збій тут не перебивав exit-код основного флоу).
cleanup() {
  local rc=$? code
  if [[ -n "$RESERVATION_ID" && "${E2E_KEEP_RESERVATION:-0}" != 1 && -n "${CUSTOMER_TOKEN:-}" ]]; then
    code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 -X DELETE \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $CUSTOMER_TOKEN" \
      --data "$(jq -n --arg id "$RESERVATION_ID" '{id:$id}')" "$API/reservations" 2>/dev/null || true)"
    if [[ "$code" == 200 ]]; then echo "  (бронювання $RESERVATION_ID скасовано)"
    else echo "  (не вдалося скасувати бронювання $RESERVATION_ID: HTTP ${code:-?})"; fi
  elif [[ -n "$RESERVATION_ID" ]]; then
    echo "  (бронювання $RESERVATION_ID залишено)"
  fi
  exit "$rc"
}
trap cleanup EXIT

log "API: $API"

# ---------- 1. signin, локація, бронювання, призначений waiter ----------
log "1. Signin"
CUSTOMER_TOKEN="$(signin "$CUSTOMER_EMAIL" "$CUSTOMER_PASSWORD")"; ok "customer $CUSTOMER_EMAIL"
WAITER1_TOKEN="$(signin "$WAITER1_EMAIL" "$WAITER1_PASSWORD")"; WAITER1_SUB="$(jwt_sub "$WAITER1_TOKEN")"
[[ -n "$WAITER1_SUB" ]] || die "Не вдалося прочитати sub з idToken $WAITER1_EMAIL"
ok "waiter1 $WAITER1_EMAIL ($WAITER1_SUB)"
WAITER2_TOKEN=""; WAITER2_SUB=""
if [[ -n "${WAITER2_EMAIL:-}" ]]; then
  WAITER2_TOKEN="$(signin "$WAITER2_EMAIL" "$WAITER2_PASSWORD")"; WAITER2_SUB="$(jwt_sub "$WAITER2_TOKEN")"
  ok "waiter2 $WAITER2_EMAIL ($WAITER2_SUB)"
fi

if [[ -n "${LOCATION_ID:-}" ]]; then
  LOC="$LOCATION_ID"
else
  req GET "/waiters/$WAITER1_SUB/location"; expect 200 "GET /waiters/{userId}/location ($WAITER1_EMAIL)"
  LOC="$(jq -r '.locationId // empty' <<<"$HTTP_BODY")"
  [[ -n "$LOC" ]] || die "Немає locationId для $WAITER1_EMAIL (задай LOCATION_ID): $HTTP_BODY"
fi
echo "  локація: $LOC"

log "2. Бронювання"
for attempt in 1 2 3 4 5 6 7 8; do
  days=$(( 60 + RANDOM % 300 )); hour=$(( 10 + RANDOM % 9 ))
  START="$(future_utc "$days" "$hour")"; END="$(plus_90_min "$START")"
  req GET "/available-tables?locationId=$LOC&capacity=$GUEST_COUNT&startTime=$START"; expect 200 "GET /available-tables"
  TABLE_ID="$(jq -r '.[0].id // empty' <<<"$HTTP_BODY")"
  [[ -n "$TABLE_ID" ]] || { echo "  спроба $attempt: немає вільного столу на $START"; continue; }
  req POST /reservations "$(jq -n --arg t "$TABLE_ID" --arg s "$START" --arg e "$END" --argjson g "$GUEST_COUNT" \
    '{tableId:$t, startTime:$s, endTime:$e, guestCount:$g}')" "$CUSTOMER_TOKEN"
  if [[ "$HTTP_CODE" == 201 || "$HTTP_CODE" == 200 ]]; then
    RESERVATION_ID="$(jq -r '.id' <<<"$HTTP_BODY")"
    ASSIGNED_WAITER="$(jq -r '.waiterId // empty' <<<"$HTTP_BODY")"
    break
  fi
  echo "  спроба $attempt: POST /reservations → HTTP $HTTP_CODE: $(err_msg)"
done
[[ -n "$RESERVATION_ID" && "$RESERVATION_ID" != null ]] || die "Не вдалося створити бронювання в локації $LOC"
ok "reservation $RESERVATION_ID (стіл $TABLE_ID, $START – $END), waiterId=${ASSIGNED_WAITER:-?}"

# Призначений waiter: waiterId з відповіді = sub з idToken. Якщо не збігся жоден — шукаємо бронювання
# у GET /reservations кожного waiter-а (для waiter-а повертає призначені йому бронювання).
WAITER_TOKEN=""; WAITER_EMAIL=""
if [[ -n "${ASSIGNED_WAITER:-}" && "$ASSIGNED_WAITER" == "$WAITER1_SUB" ]]; then
  WAITER_TOKEN="$WAITER1_TOKEN"; WAITER_EMAIL="$WAITER1_EMAIL"
elif [[ -n "${ASSIGNED_WAITER:-}" && -n "$WAITER2_SUB" && "$ASSIGNED_WAITER" == "$WAITER2_SUB" ]]; then
  WAITER_TOKEN="$WAITER2_TOKEN"; WAITER_EMAIL="$WAITER2_EMAIL"
else
  for pair in "$WAITER1_EMAIL|$WAITER1_TOKEN" "${WAITER2_EMAIL:-}|$WAITER2_TOKEN"; do
    email="${pair%%|*}"; token="${pair#*|}"
    [[ -n "$email" && -n "$token" ]] || continue
    req GET /reservations "" "$token"
    if [[ "$HTTP_CODE" == 200 ]] && jq -e --arg id "$RESERVATION_ID" 'any(.[]?; .id == $id)' <<<"$HTTP_BODY" >/dev/null; then
      WAITER_TOKEN="$token"; WAITER_EMAIL="$email"; break
    fi
  done
fi
[[ -n "$WAITER_TOKEN" ]] || die "Бронювання призначене waiter-у ${ASSIGNED_WAITER:-?}, якого немає серед WAITER1/WAITER2 (задай WAITER2_* або LOCATION_ID локації WAITER1)"
ok "призначений waiter: $WAITER_EMAIL"

# ---------- 3. страви ----------
req GET "/locations/$LOC/dishes"; expect 200 "GET /locations/{id}/dishes"
read -r DISH_A DISH_B < <(jq -r '[.[]? | select((.state // "") == "Available") | .id] | unique | "\(.[0] // "") \(.[1] // "")"' <<<"$HTTP_BODY")
if [[ -z "$DISH_A" || -z "$DISH_B" ]]; then
  req GET /dishes; expect 200 "GET /dishes"
  read -r DISH_A DISH_B < <(jq -r '[.[]? | select((.state // "") == "Available") | .id] | unique | "\(.[0] // "") \(.[1] // "")"' <<<"$HTTP_BODY")
fi
[[ -n "$DISH_A" && -n "$DISH_B" ]] || die "Немає двох доступних страв (запусти scripts/seed.sh)"
echo "  страва A: $DISH_A, страва B: $DISH_B"

order_items() { # A_QTY B_QTY → JSON-масив orderItems
  jq -n --arg a "$DISH_A" --arg b "$DISH_B" --argjson qa "$1" --argjson qb "$2" '[{dishId:$a, quantity:$qa}, {dishId:$b, quantity:$qb}]'
}

# ---------- 4. customer: create → edit → edit → submit ----------
log "3. Customer: створення і два редагування поспіль"
req POST /orders "$(jq -n --arg r "$RESERVATION_ID" --argjson items "$(order_items 1 1)" '{reservationId:$r, orderItems:$items}')" "$CUSTOMER_TOKEN"
expect "200 201" "POST /orders (A×1, B×1)"; ok "order створено"

req PUT "/orders/$RESERVATION_ID" "$(jq -n --argjson items "$(order_items 2 1)" '{orderItems:$items}')" "$CUSTOMER_TOKEN"
expect 200 "PUT /orders/{reservationId} #1 (A×2, B×1)"; ok "редагування #1 → 200"
req PUT "/orders/$RESERVATION_ID" "$(jq -n --argjson items "$(order_items 2 3)" '{orderItems:$items}')" "$CUSTOMER_TOKEN"
expect 200 "PUT /orders/{reservationId} #2 одразу після #1 (A×2, B×3)"; ok "редагування #2 одразу після #1 → 200"

req GET "/orders/$RESERVATION_ID" "" "$CUSTOMER_TOKEN"; expect 200 "GET /orders/{reservationId}"
assert_jq --arg a "$DISH_A" --arg b "$DISH_B" "($ORDER_QTY_FILTER) == {(\$a): 2, (\$b): 3}" "після двох редагувань: A×2, B×3 (жодне не загубилось)"
assert_jq '.status == 0 or .status == "Created"' "order → Created"

log "4. Customer: submit pre-order"
req POST "/orders/$RESERVATION_ID/submit" "" "$CUSTOMER_TOKEN"; expect 200 "POST /orders/{reservationId}/submit"; ok "submit → 200"

# ---------- 5. waiter: два підтвердження поспіль ----------
log "5. Waiter: confirm A, одразу confirm B"
req PATCH "/reservations/$RESERVATION_ID/order/confirmations/$DISH_A" "" "$WAITER_TOKEN"; expect 200 "PATCH confirmations/{A}"; ok "confirm A → 200"
req PATCH "/reservations/$RESERVATION_ID/order/confirmations/$DISH_B" "" "$WAITER_TOKEN"; expect 200 "PATCH confirmations/{B} одразу після A"; ok "confirm B одразу після A → 200"

# ---------- 6. результат ----------
log "6. GET /orders/{reservationId}"
req GET "/orders/$RESERVATION_ID" "" "$CUSTOMER_TOKEN"; expect 200 "GET /orders/{reservationId}"
assert_jq --arg a "$DISH_A" --arg b "$DISH_B" "($ORDER_QTY_FILTER) == {(\$a): 2, (\$b): 3}" "кількості: A×2, B×3"
assert_jq '.status == 1 or .status == "Submitted"' "order → Submitted: обидва рядки Confirmed (якби confirm A загубився, лишився б SubmittedByCustomer)"

log "Готово: послідовні записи order без 409, жоден запис не загубився"

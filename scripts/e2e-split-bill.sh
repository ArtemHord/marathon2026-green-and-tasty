#!/usr/bin/env bash
# E2E split bill («хто що замовив» + розділений рахунок) проти задеплоєного API.
#
# Флоу (розділ «Демо» з agent-tasks/A7-split-bill.md):
#   1. signin customer (Owner), іншого customer, waiter-ів; локація WAITER1 (GET /waiters/{sub}/location).
#   2. бронювання на GUEST_COUNT гостей у цій локації; призначений waiter = waiterId з відповіді (sub з idToken).
#      bill-split до появи order → 404.
#   3. invite token → група; 3 гості: Andrii Caesar×2, Olena Caesar×1, Ivan інша страва (B)×1; усі submit кошиків.
#   4. Owner POST /orders (B×1 — та сама страва, що в Ivan: перевіряє частку Owner) → submit групи
#      (відповідь не змінилась: [{dishId, quantity}]).
#   5. GET /orders/{reservationId}: рядок Caesar ×3 з guests = Andrii×2, Olena×1; рядок B ×2 з guests = owner-<gid>×1, Ivan×1;
#      Σ guests.quantity == quantity.
#   6. GET /reservations/{id}/bill-split: Owner 200 (суми сходяться з order), призначений waiter 200,
#      інший customer 403, без токена 401, непризначений waiter 403 (якщо задано WAITER2_*), неіснуюче бронювання 404.
#   7. waiter PATCH /reservations/{id}/order/cancellations/{B} (дозволено призначеному waiter-у, поки до початку > 30 хв) →
#      bill-split: excludedCanceled = 1, B не в сумах, total = 3 × ціна Caesar; Ivan і Owner лишаються з total 0.
#   8. cleanup: trap скасовує бронювання (DELETE /reservations).
#
# Env (нічого не хардкодиться):
#   API_URL                   базовий URL стейджу, напр. https://<id>.execute-api.us-west-2.amazonaws.com/develop
#   CUSTOMER_EMAIL            Owner (роль Customer)
#   CUSTOMER_PASSWORD
#   OTHER_CUSTOMER_EMAIL      інший customer — для перевірки 403
#   OTHER_CUSTOMER_PASSWORD
#   WAITER1_EMAIL             waiter; бронювання робиться в його локації
#   WAITER1_PASSWORD
# Необов'язкові:
#   WAITER2_EMAIL, WAITER2_PASSWORD   другий waiter: перевірка 403 для непризначеного (або призначений, якщо бронювання дісталось йому)
#   LOCATION_ID                       локація (за замовчуванням — локація WAITER1)
#   GUEST_COUNT                       кількість гостей у бронюванні, ≥ 4 (за замовчуванням 4: owner + 3 гостя)
#   E2E_KEEP_RESERVATION=1            не скасовувати бронювання наприкінці
#
# Використання:
#   API_URL=… CUSTOMER_EMAIL=… CUSTOMER_PASSWORD=… OTHER_CUSTOMER_EMAIL=… OTHER_CUSTOMER_PASSWORD=… \
#   WAITER1_EMAIL=… WAITER1_PASSWORD=… [WAITER2_EMAIL=… WAITER2_PASSWORD=…] scripts/e2e-split-bill.sh
set -euo pipefail

# ---------- вивід ----------
log()  { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
ok()   { printf '  \033[32m✔\033[0m %s\n' "$*"; }
skip() { printf '  \033[33m–\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2; exit 1; }

# ---------- env ----------
command -v curl >/dev/null || die "Потрібен curl"
command -v jq   >/dev/null || die "Потрібен jq (brew install jq)"

missing=()
for v in API_URL CUSTOMER_EMAIL CUSTOMER_PASSWORD OTHER_CUSTOMER_EMAIL OTHER_CUSTOMER_PASSWORD WAITER1_EMAIL WAITER1_PASSWORD; do
  [[ -n "${!v:-}" ]] || missing+=("$v")
done
(( ${#missing[@]} == 0 )) || die "Не задано змінні оточення: ${missing[*]}"
[[ "$CUSTOMER_EMAIL" != "$OTHER_CUSTOMER_EMAIL" ]] || die "OTHER_CUSTOMER_EMAIL має відрізнятися від CUSTOMER_EMAIL"
if [[ -n "${WAITER2_EMAIL:-}" && -z "${WAITER2_PASSWORD:-}" ]]; then die "Задано WAITER2_EMAIL без WAITER2_PASSWORD"; fi

API="${API_URL%/}"
GUEST_COUNT="${GUEST_COUNT:-4}"
if ! [[ "$GUEST_COUNT" =~ ^[0-9]+$ ]] || (( GUEST_COUNT < 4 )); then
  die "GUEST_COUNT має бути цілим ≥ 4 (owner + 3 гостя)"
fi

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
# expect "<коди через пробіл>" "<що робили>"
expect() {
  local c; for c in $1; do [[ "$HTTP_CODE" == "$c" ]] && return 0; done
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
# Тіло помилки: 401/403/404 → {"error": …} (401 від API Gateway → {"message": …}), 400 бізнес-правил → {"message": …, "errorCode": …}
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

# ---------- jq-фільтри bill-split (перевіряються й офлайн на зразках) ----------
# Гроші в API — decimal, у jq — double (12.99 × 3 ≠ 38.97 точно), тому порівняння сум — з допуском півцента.
JQ_NEAR='def near(a; b): ((a - b) | fabs) < 0.005;'
# кожен subtotal = price × quantity, total учасника = Σ subtotal, total = Σ total учасників
SPLIT_SUMS_FILTER="$JQ_NEAR"'
  ([.participants[].items[] | near(.subtotal; .price * .quantity)] | all)
  and ([.participants[] | near(.total; [.items[].subtotal] | add // 0)] | all)
  and near(.total; [.participants[].total] | add // 0)'
# {dishId: quantity} учасника з guestSessionId $id
SPLIT_ITEMS_OF='[.participants[] | select(.guestSessionId == $id) | .items[] | {key: .dishId, value: .quantity}] | from_entries'
# Σ price × quantity рядків GET /orders/{reservationId} (ціни поточні — ті самі, що бере bill-split)
ORDER_TOTAL_FILTER='[.orderItems[] | .dish.price * .quantity] | add // 0'

RESERVATION_ID=""
# Скасовує створене бронювання (curl напряму, щоб збій тут не перебивав exit-код основного флоу).
cleanup() {
  local rc=$? code
  if [[ -n "$RESERVATION_ID" && "${E2E_KEEP_RESERVATION:-0}" != 1 && -n "${OWNER_TOKEN:-}" ]]; then
    code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 -X DELETE \
      -H 'Content-Type: application/json' -H "Authorization: Bearer $OWNER_TOKEN" \
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

# ---------- 1. signin, локація ----------
log "1. Signin"
OWNER_TOKEN="$(signin "$CUSTOMER_EMAIL" "$CUSTOMER_PASSWORD")"; ok "owner $CUSTOMER_EMAIL"
OTHER_TOKEN="$(signin "$OTHER_CUSTOMER_EMAIL" "$OTHER_CUSTOMER_PASSWORD")"; ok "other customer $OTHER_CUSTOMER_EMAIL"
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

# ---------- 2. бронювання, призначений waiter ----------
log "2. Бронювання на $GUEST_COUNT гостей"
ASSIGNED_WAITER=""
for attempt in 1 2 3 4 5 6 7 8; do
  days=$(( 60 + RANDOM % 300 )); hour=$(( 10 + RANDOM % 9 ))
  START="$(future_utc "$days" "$hour")"; END="$(plus_90_min "$START")"
  req GET "/available-tables?locationId=$LOC&capacity=$GUEST_COUNT&startTime=$START"; expect 200 "GET /available-tables"
  TABLE_ID="$(jq -r '.[0].id // empty' <<<"$HTTP_BODY")"
  [[ -n "$TABLE_ID" ]] || { echo "  спроба $attempt: немає столу на $GUEST_COUNT місць на $START"; continue; }
  req POST /reservations "$(jq -n --arg t "$TABLE_ID" --arg s "$START" --arg e "$END" --argjson g "$GUEST_COUNT" \
    '{tableId:$t, startTime:$s, endTime:$e, guestCount:$g}')" "$OWNER_TOKEN"
  if [[ "$HTTP_CODE" == 201 || "$HTTP_CODE" == 200 ]]; then
    RESERVATION_ID="$(jq -r '.id' <<<"$HTTP_BODY")"
    ASSIGNED_WAITER="$(jq -r '.waiterId // empty' <<<"$HTTP_BODY")"
    break
  fi
  echo "  спроба $attempt: POST /reservations → HTTP $HTTP_CODE: $(err_msg)"
done
[[ -n "$RESERVATION_ID" && "$RESERVATION_ID" != null ]] || die "Не вдалося створити бронювання на $GUEST_COUNT гостей у локації $LOC"
ok "reservation $RESERVATION_ID (стіл $TABLE_ID, $START – $END), waiterId=${ASSIGNED_WAITER:-?}"

# Призначений waiter: waiterId з відповіді = sub з idToken. Якщо не збігся жоден — шукаємо бронювання
# у GET /reservations кожного waiter-а (для waiter-а повертає призначені йому бронювання).
WAITER_TOKEN=""; WAITER_EMAIL=""; UNASSIGNED_TOKEN=""; UNASSIGNED_EMAIL=""
if [[ -n "$ASSIGNED_WAITER" && "$ASSIGNED_WAITER" == "$WAITER1_SUB" ]]; then
  WAITER_TOKEN="$WAITER1_TOKEN"; WAITER_EMAIL="$WAITER1_EMAIL"
elif [[ -n "$ASSIGNED_WAITER" && -n "$WAITER2_SUB" && "$ASSIGNED_WAITER" == "$WAITER2_SUB" ]]; then
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
if [[ -n "$WAITER2_TOKEN" ]]; then
  if [[ "$WAITER_EMAIL" == "$WAITER1_EMAIL" ]]; then UNASSIGNED_TOKEN="$WAITER2_TOKEN"; UNASSIGNED_EMAIL="$WAITER2_EMAIL"
  else UNASSIGNED_TOKEN="$WAITER1_TOKEN"; UNASSIGNED_EMAIL="$WAITER1_EMAIL"; fi
fi

req GET "/reservations/$RESERVATION_ID/bill-split" "" "$OWNER_TOKEN"; expect 404 "bill-split до появи order"
ok "bill-split без order → 404 ($(err_msg))"

# ---------- 3. група, гості, кошики ----------
log "3. Invite token, 3 гості, кошики"
req GET "/reservations/$RESERVATION_ID/invite-token" "" "$OWNER_TOKEN"; expect 200 "GET invite-token"
INVITE="$(jq -r '.token // .Token // empty' <<<"$HTTP_BODY")"; [[ -n "$INVITE" ]] || die "Немає token: $HTTP_BODY"
req GET "/guest/invites/$INVITE"; expect 200 "GET /guest/invites/{token}"
GROUP_ID="$(jq -r '.groupReservationId // .GroupReservationId // empty' <<<"$HTTP_BODY")"
[[ -n "$GROUP_ID" ]] || die "Немає groupReservationId: $HTTP_BODY"
OWNER_SHARE_ID="owner-$GROUP_ID"
ok "groupReservationId $GROUP_ID"

declare -a G_ID G_TOKEN
G_NAMES=(Andrii Olena Ivan)
for i in 0 1 2; do
  req POST /guest/sessions "$(jq -n --arg g "$GROUP_ID" --arg n "${G_NAMES[$i]}" '{groupReservationId:$g, guestName:$n}')"
  expect "200 201" "POST /guest/sessions ${G_NAMES[$i]}"
  G_ID[i]="$(jq -r '.sessionId // .SessionId // empty' <<<"$HTTP_BODY")"
  G_TOKEN[i]="$(jq -r '.sessionToken // .SessionToken // empty' <<<"$HTTP_BODY")"
  [[ -n "${G_ID[i]}" && -n "${G_TOKEN[i]}" ]] || die "POST /guest/sessions не повернув SessionId/SessionToken"
  ok "${G_NAMES[$i]}: ${G_ID[i]}"
done

# Страва A — Caesar, якщо є в меню (інакше перша доступна), B — будь-яка інша доступна.
req GET "/guest/sessions/${G_ID[0]}/menu" "" "${G_TOKEN[0]}"; expect 200 "GET guest menu"
read -r DISH_A DISH_B < <(jq -r '[(.dishes // .Dishes) | .[] | .[] | select(.selectable // .Selectable // false)]
  | unique_by(.id // .Id)
  | (map(select(((.name // .Name // "") | ascii_downcase | contains("caesar")))) + .) as $ordered
  | ($ordered[0] // {}) as $a
  | ($ordered | map(select((.id // .Id) != ($a.id // $a.Id))) | .[0] // {}) as $b
  | "\($a.id // $a.Id // "") \($b.id // $b.Id // "")"' <<<"$HTTP_BODY")
[[ -n "$DISH_A" && -n "$DISH_B" ]] || die "У меню локації менше двох доступних страв: $HTTP_BODY"
echo "  страва A (Caesar): $DISH_A, страва B: $DISH_B"

add_dish() { # GUEST_INDEX DISH_ID QUANTITY
  req POST "/guest/sessions/${G_ID[$1]}/dishes" "$(jq -n --arg d "$2" --argjson q "$3" '{dishId:$d, quantity:$q}')" "${G_TOKEN[$1]}"
  expect 200 "POST dishes ${G_NAMES[$1]} $2 x$3"
  ok "${G_NAMES[$1]}: $2 × $3"
}
add_dish 0 "$DISH_A" 2   # Andrii Caesar×2
add_dish 1 "$DISH_A" 1   # Olena  Caesar×1
add_dish 2 "$DISH_B" 1   # Ivan   B×1
for i in 0 1 2; do
  req POST "/guest/sessions/${G_ID[$i]}/submit" "" "${G_TOKEN[$i]}"; expect 200 "submit cart ${G_NAMES[$i]}"
  ok "${G_NAMES[$i]} submitted"
done

# ---------- 4. order Owner і submit групи ----------
log "4. Owner pre-order (B×1) і submit групи"
req POST /orders "$(jq -n --arg r "$RESERVATION_ID" --arg d "$DISH_B" '{reservationId:$r, orderItems:[{dishId:$d, quantity:1}]}')" "$OWNER_TOKEN"
expect "200 201" "POST /orders"; ok "order створено (B×1)"

req POST "/group-reservations/$GROUP_ID/submit" "$(jq -n --arg r "$RESERVATION_ID" '{reservationId:$r}')" "$OWNER_TOKEN"
expect 200 "POST /group-reservations/{id}/submit"
assert_jq --arg a "$DISH_A" --arg b "$DISH_B" \
  'type == "array" and all(.[]; (keys | sort) == ["dishId", "quantity"]) and (map({key: .dishId, value: .quantity}) | from_entries) == {($a): 3, ($b): 1}' \
  "відповідь submit не змінилась: [{dishId, quantity}] — A×3, B×1"

# ---------- 5. GET /orders: guests ----------
log "5. GET /orders/{reservationId}: хто що замовив"
for who in owner waiter; do
  if [[ "$who" == owner ]]; then token="$OWNER_TOKEN"; else token="$WAITER_TOKEN"; fi
  req GET "/orders/$RESERVATION_ID" "" "$token"; expect 200 "GET /orders/{reservationId} ($who)"
  assert_jq --arg a "$DISH_A" --arg andrii "${G_ID[0]}" --arg olena "${G_ID[1]}" \
    '.orderItems[] | select(.dish.id == $a) | .quantity == 3 and ([.guests[] | {id: .guestSessionId, name: .guestName, q: .quantity}] | sort_by(.id)) == ([{id: $andrii, name: "Andrii", q: 2}, {id: $olena, name: "Olena", q: 1}] | sort_by(.id))' \
    "$who: Caesar ×3 — Andrii ×2, Olena ×1"
  assert_jq --arg b "$DISH_B" --arg owner "$OWNER_SHARE_ID" --arg ivan "${G_ID[2]}" \
    '.orderItems[] | select(.dish.id == $b) | .quantity == 2 and ([.guests[] | {id: .guestSessionId, q: .quantity}] == [{id: $owner, q: 1}, {id: $ivan, q: 1}])' \
    "$who: B ×2 — Owner ×1 (owner-<gid>), Ivan ×1"
  assert_jq '[.orderItems[] | select(.guests != null) | .quantity == ([.guests[].quantity] | add)] | all' "$who: Σ guests.quantity == quantity"
done
ORDER_TOTAL="$(jq -r "$ORDER_TOTAL_FILTER" <<<"$HTTP_BODY")"
PRICE_A="$(jq -r --arg a "$DISH_A" '[.orderItems[] | select(.dish.id == $a) | .dish.price][0]' <<<"$HTTP_BODY")"
echo "  сума order: $ORDER_TOTAL, ціна Caesar: $PRICE_A"

# ---------- 6. bill-split: доступ і суми ----------
log "6. GET /reservations/{id}/bill-split"
req GET "/reservations/$RESERVATION_ID/bill-split"; expect 401 "bill-split без токена"; ok "без токена → 401"
req GET "/reservations/$RESERVATION_ID/bill-split" "" "$OTHER_TOKEN"; expect 403 "bill-split чужим customer"; ok "чужий customer → 403"
if [[ -n "$UNASSIGNED_TOKEN" ]]; then
  req GET "/reservations/$RESERVATION_ID/bill-split" "" "$UNASSIGNED_TOKEN"; expect 403 "bill-split непризначеним waiter-ом"
  ok "непризначений waiter ($UNASSIGNED_EMAIL) → 403"
else
  skip "непризначений waiter → 403: пропущено (WAITER2_* не задано)"
fi
req GET "/reservations/00000000-0000-0000-0000-000000000000/bill-split" "" "$OWNER_TOKEN"; expect 404 "bill-split неіснуючого бронювання"
ok "неіснуюче бронювання → 404"

req GET "/reservations/$RESERVATION_ID/bill-split" "" "$OWNER_TOKEN"; expect 200 "bill-split (owner)"
OWNER_SPLIT="$HTTP_BODY"
assert_jq --arg owner "$OWNER_SHARE_ID" \
  '.participants[0] | .type == "Owner" and .guestSessionId == $owner' "Owner першим, guestSessionId = owner-<gid>"
# Порядок гостей = перша поява в рядках order: рядок B (створений Owner-ом) раніше за доданий submit-ом рядок Caesar → Ivan перший;
# Andrii / Olena — у порядку сесій з індексу GroupReservationId (не гарантований), тому порівнюються як множина.
assert_jq --arg andrii "${G_ID[0]}" --arg olena "${G_ID[1]}" --arg ivan "${G_ID[2]}" \
  '(.participants | length) == 4
   and ([.participants[1:][] | .type] | all(. == "Guest"))
   and (.participants[1] | .guestSessionId == $ivan and .name == "Ivan")
   and ([.participants[2:][] | {id: .guestSessionId, name}] | sort_by(.id)) == ([{id: $andrii, name: "Andrii"}, {id: $olena, name: "Olena"}] | sort_by(.id))' \
  "гості: Ivan, далі Andrii і Olena (type = Guest)"
assert_jq --arg id "${G_ID[0]}" --arg a "$DISH_A" "($SPLIT_ITEMS_OF) == {(\$a): 2}" "Andrii: Caesar ×2"
assert_jq --arg id "${G_ID[1]}" --arg a "$DISH_A" "($SPLIT_ITEMS_OF) == {(\$a): 1}" "Olena: Caesar ×1"
assert_jq --arg id "${G_ID[2]}" --arg b "$DISH_B" "($SPLIT_ITEMS_OF) == {(\$b): 1}" "Ivan: B ×1"
assert_jq --arg id "$OWNER_SHARE_ID" --arg b "$DISH_B" "($SPLIT_ITEMS_OF) == {(\$b): 1}" "Owner: B ×1"
assert_jq "$SPLIT_SUMS_FILTER" "subtotal = price × quantity, total учасника = Σ subtotal, total = Σ учасників"
assert_jq --argjson t "$ORDER_TOTAL" "$JQ_NEAR"' near(.total; $t) and .excludedCanceled == 0' "total = сума order ($ORDER_TOTAL), excludedCanceled = 0"

req GET "/reservations/$RESERVATION_ID/bill-split" "" "$WAITER_TOKEN"; expect 200 "bill-split (призначений waiter)"
assert_jq --argjson owner "$OWNER_SPLIT" '. == $owner' "призначений waiter бачить ту саму розбивку"

# ---------- 7. waiter скасовує страву B ----------
log "7. Waiter скасовує страву B → розбивка перераховується"
req PATCH "/reservations/$RESERVATION_ID/order/cancellations/$DISH_B" "" "$WAITER_TOKEN"; expect 200 "PATCH …/order/cancellations/{B}"
ok "B скасовано"
req GET "/reservations/$RESERVATION_ID/bill-split" "" "$OWNER_TOKEN"; expect 200 "bill-split після скасування"
assert_jq '.excludedCanceled == 1' "excludedCanceled = 1"
assert_jq --arg b "$DISH_B" '[.participants[].items[] | select(.dishId == $b)] | length == 0' "B немає в items"
assert_jq --argjson p "$PRICE_A" "$JQ_NEAR"' near(.total; 3 * $p)' "total = 3 × ціна Caesar"
assert_jq --arg id "${G_ID[2]}" '.participants[] | select(.guestSessionId == $id) | .total == 0 and (.items | length) == 0' "Ivan лишився з total 0"
assert_jq --arg id "$OWNER_SHARE_ID" '.participants[] | select(.guestSessionId == $id) | .total == 0' "Owner лишився з total 0"
assert_jq --arg id "${G_ID[0]}" --arg a "$DISH_A" "($SPLIT_ITEMS_OF) == {(\$a): 2}" "Andrii: Caesar ×2 без змін"
assert_jq "$SPLIT_SUMS_FILTER" "суми після скасування сходяться"

log "Готово: усі перевірки split bill пройдено"

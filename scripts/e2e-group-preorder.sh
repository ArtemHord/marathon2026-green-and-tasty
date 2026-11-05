#!/usr/bin/env bash
# E2E груповий pre-order для Owner (homework-backend.md, рівні 1–3) проти задеплоєного API.
#
# Флоу: customer signin → бронювання на GUEST_COUNT гостей → invite token → 4 guest-сесії →
# страви (одна страва у двох сесіях) → submit кошиків (двоє гостей — ні) → GET sessions → DELETE гостя →
# GET guest-session-id → owner pre-order (POST /orders) → submit групи → GET /orders/{reservationId}
# містить об'єднані страви. Негативні: без токена 401, чужий customer 403, неіснуюча група/бронювання 404,
# 400 (без order, без reservationId, повторний submit, видалення owner-сесії).
#
# Нічого не хардкодиться — усе з env:
#   API_URL                   базовий URL стейджу, напр. https://<id>.execute-api.us-west-2.amazonaws.com/develop
#   CUSTOMER_EMAIL            Owner (роль Customer)
#   CUSTOMER_PASSWORD
#   OTHER_CUSTOMER_EMAIL      інший customer — для перевірок 403
#   OTHER_CUSTOMER_PASSWORD
# Необов'язкові:
#   LOCATION_ID               локація для бронювання (за замовчуванням — перша з GET /locations)
#   GUEST_COUNT               кількість гостей у бронюванні, ≥ 5 (за замовчуванням 5: owner + 4 guest-сесії)
#   E2E_KEEP_RESERVATION=1    не скасовувати створене бронювання наприкінці
#
# Використання:
#   API_URL=… CUSTOMER_EMAIL=… CUSTOMER_PASSWORD=… OTHER_CUSTOMER_EMAIL=… OTHER_CUSTOMER_PASSWORD=… \
#     scripts/e2e-group-preorder.sh
set -euo pipefail

# ---------- вивід ----------
log()  { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
ok()   { printf '  \033[32m✔\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2; exit 1; }
# М'які перевірки флоу фронтенду (екран Owner): не зупиняють основний флоу, але дають exit 1 наприкінці.
FRONTEND_ISSUES=()
soft_expect() { # soft_expect "<коди>" "<що перевіряли>"
  local c; for c in $1; do [[ "$HTTP_CODE" == "$c" ]] && { ok "$2"; return 0; }; done
  printf '  \033[33m✘\033[0m %s → очікував HTTP %s, отримав %s: %s\n' "$2" "$1" "$HTTP_CODE" "$HTTP_BODY"
  FRONTEND_ISSUES+=("$2 → HTTP $HTTP_CODE: $HTTP_BODY")
}

# ---------- env ----------
command -v curl >/dev/null || die "Потрібен curl"
command -v jq   >/dev/null || die "Потрібен jq (brew install jq)"

missing=()
for v in API_URL CUSTOMER_EMAIL CUSTOMER_PASSWORD OTHER_CUSTOMER_EMAIL OTHER_CUSTOMER_PASSWORD; do
  [[ -n "${!v:-}" ]] || missing+=("$v")
done
(( ${#missing[@]} == 0 )) || die "Не задано змінні оточення: ${missing[*]}"
[[ "$CUSTOMER_EMAIL" != "$OTHER_CUSTOMER_EMAIL" ]] || die "OTHER_CUSTOMER_EMAIL має відрізнятися від CUSTOMER_EMAIL"

API="${API_URL%/}"
GUEST_COUNT="${GUEST_COUNT:-5}"
if ! [[ "$GUEST_COUNT" =~ ^[0-9]+$ ]] || (( GUEST_COUNT < 5 )); then
  die "GUEST_COUNT має бути цілим ≥ 5 (owner + 4 гостя)"
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
assert_jq() { # assert_jq '<jq-фільтр, що повертає true>' "<опис>" [jq-аргументи…]
  local filter="$1" what="$2"; shift 2
  jq -e "$@" "$filter" <<<"$HTTP_BODY" >/dev/null || die "$what — не виконано. Тіло: $HTTP_BODY"
  ok "$what"
}
# Тіло помилки: 401/403/404 → {"error": …}, 400 бізнес-правил → {"message": …, "errorCode": …}
err_msg() { jq -r '.message // .error // .Message // empty' <<<"$HTTP_BODY" 2>/dev/null || true; }

soft_jq() { # soft_jq '<jq-фільтр>' "<опис>" — як assert_jq, але не зупиняє флоу
  if jq -e "$1" <<<"$HTTP_BODY" >/dev/null 2>&1; then ok "$2"
  else printf '  \033[33m✘\033[0m %s — тіло: %s\n' "$2" "$HTTP_BODY"; FRONTEND_ISSUES+=("$2: $HTTP_BODY"); fi
}

# ---------- auth ----------
signin() { # EMAIL PASSWORD → idToken
  req POST /signin "$(jq -n --arg e "$1" --arg p "$2" '{email:$e, password:$p}')"
  expect 200 "POST /signin $1"
  local t; t="$(jq -r '.idToken // empty' <<<"$HTTP_BODY")"
  [[ -n "$t" ]] || die "POST /signin $1 не повернув idToken"
  echo "$t"
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

# ---------- 1. signin ----------
log "1. Signin"
OWNER_TOKEN="$(signin "$CUSTOMER_EMAIL" "$CUSTOMER_PASSWORD")"; ok "owner $CUSTOMER_EMAIL"
OTHER_TOKEN="$(signin "$OTHER_CUSTOMER_EMAIL" "$OTHER_CUSTOMER_PASSWORD")"; ok "other customer $OTHER_CUSTOMER_EMAIL"

# ---------- 2. бронювання ----------
log "2. Бронювання на $GUEST_COUNT гостей"
if [[ -n "${LOCATION_ID:-}" ]]; then
  LOC="$LOCATION_ID"
else
  req GET /locations; expect 200 "GET /locations"
  LOC="$(jq -r '.[0].id // empty' <<<"$HTTP_BODY")"
  [[ -n "$LOC" ]] || die "Немає жодної локації (запусти scripts/seed.sh або задай LOCATION_ID)"
fi
echo "  локація: $LOC"

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
    break
  fi
  echo "  спроба $attempt: POST /reservations → HTTP $HTTP_CODE: $(err_msg)"
done
[[ -n "$RESERVATION_ID" && "$RESERVATION_ID" != null ]] || die "Не вдалося створити бронювання на $GUEST_COUNT гостей у локації $LOC"
ok "reservation $RESERVATION_ID (стіл $TABLE_ID, $START – $END)"

# ---------- 3. invite token → group ----------
log "3. Invite token і група"
req GET "/reservations/$RESERVATION_ID/invite-token"; expect 401 "invite-token без токена"; ok "invite-token без токена → 401"
req GET "/reservations/$RESERVATION_ID/invite-token" "" "$OTHER_TOKEN"; expect 403 "invite-token чужим customer"; ok "invite-token чужим customer → 403"
req GET "/reservations/00000000-0000-0000-0000-000000000000/invite-token" "" "$OWNER_TOKEN"; expect 404 "invite-token неіснуючого бронювання"; ok "invite-token неіснуючого бронювання → 404"
req GET "/reservations/$RESERVATION_ID/invite-token" "" "$OWNER_TOKEN"; expect 200 "GET invite-token"
INVITE="$(jq -r '.token // .Token // empty' <<<"$HTTP_BODY")"; [[ -n "$INVITE" ]] || die "Немає token: $HTTP_BODY"
req GET "/guest/invites/$INVITE"; expect 200 "GET /guest/invites/{token}"
GROUP_ID="$(jq -r '.groupReservationId // .GroupReservationId // empty' <<<"$HTTP_BODY")"
[[ -n "$GROUP_ID" ]] || die "Немає groupReservationId: $HTTP_BODY"
ok "groupReservationId $GROUP_ID"

# ---------- 4. guest-сесії ----------
log "4. Guest-сесії"
declare -a G_ID G_TOKEN
G_NAMES=(Andrii Olena Ivan Taras)
for i in 0 1 2 3; do
  req POST /guest/sessions "$(jq -n --arg g "$GROUP_ID" --arg n "${G_NAMES[$i]}" '{groupReservationId:$g, guestName:$n}')"
  expect "200 201" "POST /guest/sessions ${G_NAMES[$i]}"
  G_ID[i]="$(jq -r '.sessionId // .SessionId // empty' <<<"$HTTP_BODY")"
  G_TOKEN[i]="$(jq -r '.sessionToken // .SessionToken // empty' <<<"$HTTP_BODY")"
  [[ -n "${G_ID[i]}" && -n "${G_TOKEN[i]}" ]] || die "POST /guest/sessions не повернув SessionId/SessionToken"
  ok "${G_NAMES[$i]}: ${G_ID[i]}"
done

# Екран Owner (GroupOrdersService.getGroupInvite$) бере groupReservationId через invite-token → /guest/invites/{token}.
# Група зараз повна (owner + 4 гостя = GUEST_COUNT) — екран Owner все одно має відкриватись.
log "4a. Фронтенд: екран Owner для повної групи"
req GET "/reservations/$RESERVATION_ID/invite-token" "" "$OWNER_TOKEN"; soft_expect 200 "invite-token для повної групи"
req GET "/guest/invites/$INVITE"; soft_expect 200 "GET /guest/invites/{token} для повної групи (getGroupInvite\$ екрана Owner)"
soft_jq '.Status == "Open" and .CanJoin == false' "повна група: Status = Open, CanJoin = false"
# захист join тепер лише в POST /guest/sessions — п'ятий гість не має пройти
req POST /guest/sessions "$(jq -n --arg g "$GROUP_ID" '{groupReservationId:$g, guestName:"Extra"}')"
expect 400 "POST /guest/sessions у повну групу"
[[ "$(jq -r '.errorCode // empty' <<<"$HTTP_BODY")" == GuestLimitExceeded ]] || die "Очікував errorCode GuestLimitExceeded: $HTTP_BODY"
ok "join у повну групу → 400 GuestLimitExceeded"
# Повна група не відкриває вхід: захист — на POST /guest/sessions, а не на читанні деталей.
if (( GUEST_COUNT == 5 )); then
  req POST /guest/sessions "$(jq -n --arg g "$GROUP_ID" '{groupReservationId:$g, guestName:"Extra"}')"; expect 400 "join у повну групу"
  assert_jq '.errorCode == "GuestLimitExceeded"' "join у повну групу → 400 GuestLimitExceeded"
fi

# ---------- 5. страви ----------
log "5. Страви"
req GET "/guest/sessions/${G_ID[0]}/menu" "" "${G_TOKEN[0]}"; expect 200 "GET guest menu"
read -r DISH_A DISH_B < <(jq -r '[(.dishes // .Dishes) | .[] | .[] | select(.selectable // .Selectable // false)]
  | unique_by(.id // .Id) | map(.id // .Id) | "\(.[0] // "") \(.[1] // "")"' <<<"$HTTP_BODY")
[[ -n "$DISH_A" && -n "$DISH_B" ]] || die "У меню локації менше двох доступних страв: $HTTP_BODY"
echo "  страва A: $DISH_A, страва B: $DISH_B"

add_dish() { # GUEST_INDEX DISH_ID QUANTITY
  req POST "/guest/sessions/${G_ID[$1]}/dishes" "$(jq -n --arg d "$2" --argjson q "$3" '{dishId:$d, quantity:$q}')" "${G_TOKEN[$1]}"
  expect 200 "POST dishes ${G_NAMES[$1]} $2 x$3"
  ok "${G_NAMES[$1]}: $2 × $3"
}
add_dish 0 "$DISH_A" 2   # Andrii  A×2, B×1  (submitted)
add_dish 0 "$DISH_B" 1
add_dish 1 "$DISH_A" 1   # Olena   A×1       (submitted) — та сама страва A, що в Andrii
add_dish 2 "$DISH_B" 5   # Ivan    B×5       (НЕ submitted — не має потрапити в order)
add_dish 3 "$DISH_A" 7   # Taras   A×7       (НЕ submitted — буде видалений)

# ---------- 6. submit кошиків ----------
log "6. Submit кошиків (Andrii, Olena; Ivan і Taras — ні)"
for i in 0 1; do
  req POST "/guest/sessions/${G_ID[$i]}/submit" "" "${G_TOKEN[$i]}"; expect 200 "submit cart ${G_NAMES[$i]}"
  ok "${G_NAMES[$i]} submitted"
done

# ---------- 7. GET sessions (L1) ----------
log "7. GET /guest/groups/{id}/sessions"
req GET "/guest/groups/$GROUP_ID/sessions"; expect 401 "GET sessions без токена"; ok "без токена → 401"
req GET "/guest/groups/$GROUP_ID/sessions" "" "$OTHER_TOKEN"; expect 403 "GET sessions чужим customer"; ok "чужий customer → 403"
req GET "/guest/groups/00000000-0000-0000-0000-000000000000/sessions" "" "$OWNER_TOKEN"; expect 404 "GET sessions неіснуючої групи"; ok "неіснуюча група → 404"
req GET "/guest/groups/$GROUP_ID/sessions" "" "$OWNER_TOKEN"; expect 200 "GET sessions"
assert_jq '.participants | length == 4' "4 учасники"
assert_jq '[.participants[] | .type] | all(. == "Guest")' "усі type = Guest (owner-сесії ще немає)"
assert_jq '.total == ([.participants[].total] | add)' "total = сума total усіх учасників (submitted і ні)"
assert_jq '[.participants[] | .total == ([.items[] | .dish.price * .quantity] | add // 0)] | all' "participant.total = Σ price × quantity"
assert_jq --arg id "${G_ID[0]}" '.participants[] | select(.id == $id) | .isSubmitted == true and .name == "Andrii"' "Andrii: isSubmitted = true"
assert_jq --arg id "${G_ID[2]}" '.participants[] | select(.id == $id) | .isSubmitted == false' "Ivan: isSubmitted = false"

# ---------- 8. DELETE гостя (L3) ----------
log "8. DELETE /guest/groups/{id}/sessions/{guestSessionId}"
req DELETE "/guest/groups/$GROUP_ID/sessions/${G_ID[3]}"; expect 401 "DELETE без токена"; ok "без токена → 401"
req DELETE "/guest/groups/$GROUP_ID/sessions/${G_ID[3]}" "" "$OTHER_TOKEN"; expect 403 "DELETE чужим customer"; ok "чужий customer → 403"
req DELETE "/guest/groups/00000000-0000-0000-0000-000000000000/sessions/${G_ID[3]}" "" "$OWNER_TOKEN"; expect 404 "DELETE у неіснуючій групі"; ok "неіснуюча група → 404"
req DELETE "/guest/groups/$GROUP_ID/sessions/00000000-0000-0000-0000-000000000000" "" "$OWNER_TOKEN"; expect 404 "DELETE неіснуючої сесії"; ok "неіснуюча сесія → 404"
req DELETE "/guest/groups/$GROUP_ID/sessions/${G_ID[3]}" "" "$OWNER_TOKEN"; expect 200 "DELETE Taras"
assert_jq --arg id "${G_ID[3]}" '(.participants | length == 3) and ([.participants[].id] | index($id) == null)' "Taras видалений, 3 учасники"
assert_jq '.total == ([.participants[].total] | add)' "оновлений total"

# ---------- 9. GET guest-session-id (L2) ----------
log "9. GET /reservations/{id}/guest-session-id"
req GET "/reservations/$RESERVATION_ID/guest-session-id"; expect 401 "guest-session-id без токена"; ok "без токена → 401"
req GET "/reservations/$RESERVATION_ID/guest-session-id" "" "$OTHER_TOKEN"; expect 403 "guest-session-id чужим customer"; ok "чужий customer → 403"
req GET "/reservations/00000000-0000-0000-0000-000000000000/guest-session-id" "" "$OWNER_TOKEN"; expect 404 "guest-session-id неіснуючого бронювання"; ok "неіснуюче бронювання → 404"
req GET "/reservations/$RESERVATION_ID/guest-session-id" "" "$OWNER_TOKEN"; expect 200 "GET guest-session-id"
OWNER_SESSION_ID="$(jq -r '.guestSessionId // empty' <<<"$HTTP_BODY")"; [[ -n "$OWNER_SESSION_ID" ]] || die "Немає guestSessionId: $HTTP_BODY"
ok "owner session $OWNER_SESSION_ID"
req GET "/reservations/$RESERVATION_ID/guest-session-id" "" "$OWNER_TOKEN"; expect 200 "GET guest-session-id (повторно)"
assert_jq --arg id "$OWNER_SESSION_ID" '.guestSessionId == $id' "повторний виклик повертає той самий id"
req GET "/guest/groups/$GROUP_ID/sessions" "" "$OWNER_TOKEN"; expect 200 "GET sessions"
assert_jq --arg id "$OWNER_SESSION_ID" '[.participants[] | select(.type == "Owner")] | length == 1 and .[0].id == $id' "у snapshot є рівно один учасник type = Owner з цим id"
req DELETE "/guest/groups/$GROUP_ID/sessions/$OWNER_SESSION_ID" "" "$OWNER_TOKEN"; expect 400 "DELETE owner-сесії"; ok "видалення owner-сесії → 400 ($(err_msg))"

# ---------- 10. submit групи (L2) ----------
log "10. POST /group-reservations/{id}/submit"
SUBMIT_BODY="$(jq -n --arg r "$RESERVATION_ID" '{reservationId:$r}')"
req POST "/group-reservations/$GROUP_ID/submit" "$SUBMIT_BODY"; expect 401 "submit без токена"; ok "без токена → 401"
req POST "/group-reservations/$GROUP_ID/submit" "$SUBMIT_BODY" "$OTHER_TOKEN"; expect 403 "submit чужим customer"; ok "чужий customer → 403"
req POST "/group-reservations/00000000-0000-0000-0000-000000000000/submit" "$SUBMIT_BODY" "$OWNER_TOKEN"; expect 404 "submit неіснуючої групи"; ok "неіснуюча група → 404"
req POST "/group-reservations/$GROUP_ID/submit" '{}' "$OWNER_TOKEN"; expect 400 "submit без reservationId"; ok "без reservationId → 400"
req POST "/group-reservations/$GROUP_ID/submit" "$SUBMIT_BODY" "$OWNER_TOKEN"; expect 400 "submit без order"; ok "order ще немає → 400 ($(err_msg))"

log "    owner pre-order: POST /orders (A×1)"
req POST /orders "$(jq -n --arg r "$RESERVATION_ID" --arg d "$DISH_A" '{reservationId:$r, orderItems:[{dishId:$d, quantity:1}]}')" "$OWNER_TOKEN"
expect "200 201" "POST /orders"; ok "order створено"

# Owner у snapshot = його order («My Pre-Order» = те, що отримає ресторан)
req GET "/guest/groups/$GROUP_ID/sessions" "" "$OWNER_TOKEN"; expect 200 "GET sessions після owner pre-order"
assert_jq --arg id "$OWNER_SESSION_ID" --arg a "$DISH_A" \
  '.participants[] | select(.id == $id) | .type == "Owner" and .isSubmitted == true and ([.items[] | {key:.dish.id, value:.quantity}] | from_entries) == {($a): 1} and .total == ([.items[] | .dish.price * .quantity] | add)' \
  "Owner: items з order (A×1), isSubmitted = true, total = Σ рядків order"
assert_jq '.total == ([.participants[].total] | add)' "snapshot.total включає order Owner"

# Подвійний клік: два submit одночасно — рівно один 200, другий 400 «already submitted»; страви не подвоюються (див. крок 11).
SUBMIT_TMP="$(mktemp -d)"
for n in 1 2; do
  curl -sS --max-time 30 -X POST -H 'Content-Type: application/json' -H "Authorization: Bearer $OWNER_TOKEN" \
    --data "$SUBMIT_BODY" -o "$SUBMIT_TMP/body$n" -w '%{http_code}' "$API/group-reservations/$GROUP_ID/submit" >"$SUBMIT_TMP/code$n" &
done
wait
CODES="$(cat "$SUBMIT_TMP/code1") $(cat "$SUBMIT_TMP/code2")"
case "$CODES" in
  "200 400") HTTP_CODE=200; HTTP_BODY="$(cat "$SUBMIT_TMP/body1")"; LOSER="$SUBMIT_TMP/body2" ;;
  "400 200") HTTP_CODE=200; HTTP_BODY="$(cat "$SUBMIT_TMP/body2")"; LOSER="$SUBMIT_TMP/body1" ;;
  *) die "Два одночасні submit → очікував один 200 і один 400, отримав: $CODES ($(cat "$SUBMIT_TMP/body1") | $(cat "$SUBMIT_TMP/body2"))" ;;
esac
jq -e '.errorCode == "GroupReservationSubmitted"' "$LOSER" >/dev/null || die "Другий одночасний submit: очікував errorCode GroupReservationSubmitted: $(cat "$LOSER")"
rm -rf "$SUBMIT_TMP"
ok "два одночасні submit → один 200, другий 400 GroupReservationSubmitted"
assert_jq --arg a "$DISH_A" --arg b "$DISH_B" \
  '(length == 2) and (map({key:.dishId, value:.quantity}) | from_entries) == {($a): 3, ($b): 1}' \
  "відповідь: A×3 (Andrii 2 + Olena 1), B×1 — без несабмічених Ivan і Taras"

req POST "/group-reservations/$GROUP_ID/submit" "$SUBMIT_BODY" "$OWNER_TOKEN"; expect 400 "повторний submit"
[[ "$(err_msg)" == "Group pre-order has already been submitted." ]] || die "Неочікуване повідомлення повторного submit: $HTTP_BODY"
ok "повторний submit → 400 \"Group pre-order has already been submitted.\""

req GET "/guest/groups/$GROUP_ID/status"; expect 200 "GET group status"
assert_jq '(.status // .Status) == "Submitted"' "група → Submitted"

# Перезавантаження екрана Owner після submit має показати read-only «Sent to Restaurant»:
# той самий ланцюжок запитів, що в GroupOrderPageComponent.load().
log "10a. Фронтенд: перезавантаження екрана Owner після submit"
req GET "/reservations/$RESERVATION_ID/invite-token" "" "$OWNER_TOKEN"; soft_expect 200 "invite-token після submit (getGroupInvite\$ при reload)"
req GET "/guest/invites/$INVITE"; soft_expect 200 "GET /guest/invites/{token} після submit (getGroupInvite\$ при reload)"
soft_jq '.Status == "Submitted" and .CanJoin == false' "після submit: Status = Submitted, CanJoin = false"
req POST /guest/sessions "$(jq -n --arg g "$GROUP_ID" '{groupReservationId:$g, guestName:"Late"}')"
expect 400 "POST /guest/sessions після submit"; ok "join після submit → 400 ($(err_msg))"
req GET "/guest/groups/$GROUP_ID/sessions" "" "$OWNER_TOKEN"; soft_expect 200 "GET sessions після submit"
# read-only екран: Grand Total (= консолідація submitted-учасників) має дорівнювати order, тобто A×4, B×1 —
# страви гостей не можуть рахуватись і в Owner (його order уже містить їх після submit), і в самих гостях
if jq -e --arg a "$DISH_A" --arg b "$DISH_B" \
  '[.participants[] | select(.isSubmitted) | .items[]] | group_by(.dish.id) | map({key: .[0].dish.id, value: (map(.quantity) | add)}) | from_entries == {($a): 4, ($b): 1}' \
  <<<"$HTTP_BODY" >/dev/null; then ok "після submit: консолідація snapshot = order (A×4, B×1)"
else
  printf '  \033[33m✘\033[0m після submit: консолідація snapshot ≠ order (A×4, B×1): %s\n' "$HTTP_BODY"
  FRONTEND_ISSUES+=("після submit консолідація snapshot ≠ order A×4, B×1 (страви гостей пораховано двічі?)")
fi
req GET "/reservations/$RESERVATION_ID/guest-session-id" "" "$OWNER_TOKEN"; soft_expect 200 "guest-session-id після submit"
req POST /guest/sessions "$(jq -n --arg g "$GROUP_ID" '{groupReservationId:$g, guestName:"Late"}')"; expect 400 "join після submit"
assert_jq '.errorCode == "GroupReservationSubmitted"' "join після submit → 400 GroupReservationSubmitted"

# ---------- 11. order ----------
log "11. GET /orders/{reservationId}"
req GET "/orders/$RESERVATION_ID" "" "$OWNER_TOKEN"; expect 200 "GET order"
assert_jq --arg a "$DISH_A" --arg b "$DISH_B" \
  '([.orderItems[] | {key:.dish.id, value:.quantity}] | from_entries) == {($a): 4, ($b): 1}' \
  "order: A×4 (owner 1 + група 3), B×1 — страви Owner не продубльовані"
assert_jq '.status == "SubmittedByCustomer" or .status == 3' "order → SubmittedByCustomer"

if (( ${#FRONTEND_ISSUES[@]} > 0 )); then
  printf '\033[1;33m[frontend-flow]\033[0m API-флоу пройдено, але екран Owner на цих даних не завантажиться:\n' >&2
  printf '  - %s\n' "${FRONTEND_ISSUES[@]}" >&2
  exit 1
fi
log "Готово: усі перевірки пройдено"

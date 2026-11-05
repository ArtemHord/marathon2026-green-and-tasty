#!/usr/bin/env bash
# Сидує демо-дані у задеплоєне API: 2 локації, 9 страв (3 Appetizer / 3 MainCourse / 3 Dessert),
# по 4 столи на локацію (2/4/4/6 місць), customer + 2 waiter-и (по одному на локацію).
# Ідемпотентний: перед створенням перевіряє, що вже є (локації — за address, страви — за name,
# столи — за tableNumber, юзери — пробним /signin).
#
# Паролі генеруються один раз і зберігаються ПОЗА репо (за замовчуванням ../.seed-credentials.env,
# chmod 600) — повторний запуск бере їх звідти.
#
# Використання: scripts/seed.sh            # API береться з API Gateway (як у smoke.sh)
#               API_URL=https://…/develop scripts/seed.sh
source "$(dirname "$0")/lib.sh"

command -v jq >/dev/null || die "Потрібен jq (brew install jq)"

API="${API_URL:-$(api_url)}" || die "API не знайдено — backend не задеплоєно (або задай API_URL)"
API="${API%/}"
CREDS_FILE="${SEED_CREDS_FILE:-$REPO_ROOT/../.seed-credentials.env}"

# ---------- HTTP ----------
HTTP_CODE=""; HTTP_BODY=""
# req METHOD PATH [JSON_BODY] [BEARER_TOKEN] → HTTP_CODE, HTTP_BODY
req() {
  local method="$1" path="$2" body="${3:-}" token="${4:-}" out
  local args=(-sS --max-time 30 -X "$method" -H 'Content-Type: application/json' -w $'\n%{http_code}')
  [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
  # тіло через stdin: base64-фото не впирається в ліміт довжини аргументів
  if [[ -n "$body" ]]; then
    out="$(curl "${args[@]}" --data-binary @- "$API$path" <<<"$body")" || die "curl $method $path впав"
  else
    out="$(curl "${args[@]}" "$API$path")" || die "curl $method $path впав"
  fi
  HTTP_CODE="${out##*$'\n'}"
  HTTP_BODY="${out%$'\n'*}"
}
expect() { # expect "<коди через пробіл>" "<що робили>"
  local c; for c in $1; do [[ "$HTTP_CODE" == "$c" ]] && return 0; done
  die "$2 → HTTP $HTTP_CODE: $HTTP_BODY"
}

# ---------- креденшели ----------
gen_password() { # 14 символів: усі класи гарантовано (Upper, lower, digit, special)
  local core; core="$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | cut -c1-10)"   # cut, не head: без SIGPIPE під pipefail
  printf 'Gt%s7!' "$core"
}

if [[ -f "$CREDS_FILE" ]]; then
  log "Креденшели: $CREDS_FILE"
  # shellcheck disable=SC1090
  source "$CREDS_FILE"
else
  log "Генерую креденшели → $CREDS_FILE"
  CUSTOMER_EMAIL="gt.customer@example.com"; CUSTOMER_PASSWORD="$(gen_password)"
  WAITER1_EMAIL="gt.waiter1@example.com";   WAITER1_PASSWORD="$(gen_password)"
  WAITER2_EMAIL="gt.waiter2@example.com";   WAITER2_PASSWORD="$(gen_password)"
  umask 077
  cat >"$CREDS_FILE" <<EOF
# Тестові акаунти Green & Tasty (згенеровано scripts/seed.sh $(date '+%Y-%m-%d')). НЕ КОМІТИТИ.
CUSTOMER_EMAIL='$CUSTOMER_EMAIL'
CUSTOMER_PASSWORD='$CUSTOMER_PASSWORD'
WAITER1_EMAIL='$WAITER1_EMAIL'
WAITER1_PASSWORD='$WAITER1_PASSWORD'
WAITER2_EMAIL='$WAITER2_EMAIL'
WAITER2_PASSWORD='$WAITER2_PASSWORD'
EOF
  chmod 600 "$CREDS_FILE"
fi

log "API: $API"

# ---------- 1. локації ----------
LOC_ADDRESSES=(
  "48 Khreshchatyk St, Kyiv"
  "12 Rynok Square, Lviv"
)
LOC_DESCRIPTIONS=(
  "Flagship Green & Tasty in the heart of Kyiv: seasonal farm-to-table menu, open kitchen and a summer terrace."
  "Cozy Green & Tasty in Lviv old town: plant-forward dishes, local cheeses and house-baked bread."
)
LOC_CAPACITY=(60 40)
LOC_OCCUPANCY=("72" "65")
LOC_RATING=(4.8 4.6)

req GET /locations; expect 200 "GET /locations"
LOCATIONS_JSON="$HTTP_BODY"
LOC_IDS=()
for i in 0 1; do
  addr="${LOC_ADDRESSES[$i]}"
  id="$(jq -r --arg a "$addr" 'map(select(.address == $a)) | .[0].id // empty' <<<"$LOCATIONS_JSON")"
  if [[ -n "$id" ]]; then
    echo "  = локація '$addr' вже є ($id)"
  else
    body="$(jq -n --arg a "$addr" --arg d "${LOC_DESCRIPTIONS[$i]}" --argjson c "${LOC_CAPACITY[$i]}" \
      --arg o "${LOC_OCCUPANCY[$i]}" --argjson r "${LOC_RATING[$i]}" \
      '{address:$a, description:$d, totalCapacity:$c, averageOccupancy:$o, rating:$r}')"
    req POST /locations "$body"; expect "200 201" "POST /locations '$addr'"
    id="$(jq -r '.id' <<<"$HTTP_BODY")"
    [[ -n "$id" && "$id" != null ]] || die "POST /locations не повернув id: $HTTP_BODY"
    echo "  + локація '$addr' ($id)"
  fi
  LOC_IDS+=("$id")
done
LOC_IDS_JSON="$(printf '%s\n' "${LOC_IDS[@]}" | jq -R . | jq -sc .)"

# ---------- 2. страви ----------
# name|dishType|price|weight|calories|proteins|fats|carbs|vitamins|description
DISHES=(
  "Burrata with Heirloom Tomatoes|Appetizer|12.50|250g|380|18|28|9|A, C, K|Creamy burrata, heirloom tomatoes, basil oil and toasted sourdough."
  "Beetroot Carpaccio|Appetizer|9.90|180g|210|6|12|19|A, C, B9|Thinly sliced roasted beetroot with goat cheese, walnuts and honey dressing."
  "Avocado Tartare|Appetizer|11.00|200g|320|5|26|14|E, K, C|Avocado and cucumber tartare with lime, chili and crispy rice crackers."
  "Grilled Salmon with Quinoa|MainCourse|24.00|380g|620|42|28|45|D, B12, B6|Wild salmon fillet, herbed quinoa, roasted asparagus and lemon butter."
  "Mushroom Risotto|MainCourse|18.50|350g|540|14|18|78|B2, B3, D|Arborio rice with porcini and forest mushrooms, parmesan and truffle oil."
  "Chicken Kyiv with Mashed Potatoes|MainCourse|19.90|400g|780|38|44|52|A, B6, E|Classic chicken Kyiv with herb butter, creamy mashed potatoes and pickles."
  "Cheesecake with Berries|Dessert|8.50|160g|420|9|24|42|A, B2|Baked vanilla cheesecake with seasonal berry compote."
  "Dark Chocolate Fondant|Dessert|9.50|140g|480|7|30|46|E, B1|Warm chocolate fondant with a molten centre and vanilla ice cream."
  "Honey Cake Medovik|Dessert|7.90|150g|450|6|22|58|B2, E|Layered honey cake with sour cream frosting."
)

req GET /dishes; expect 200 "GET /dishes"
DISHES_JSON="$HTTP_BODY"
for row in "${DISHES[@]}"; do
  IFS='|' read -r name type price weight cal prot fats carbs vit desc <<<"$row"
  if jq -e --arg n "$name" 'any(.[]; .name == $n)' <<<"$DISHES_JSON" >/dev/null; then
    echo "  = страва '$name' вже є"
    continue
  fi
  body="$(jq -n --arg n "$name" --arg d "$desc" --arg t "$type" --argjson p "$price" --arg w "$weight" \
    --arg cal "$cal" --arg pr "$prot" --arg f "$fats" --arg c "$carbs" --arg v "$vit" --argjson l "$LOC_IDS_JSON" \
    '{name:$n, description:$d, dishType:$t, price:$p, weight:$w, calories:$cal, proteins:$pr, fats:$f,
      carbohydrates:$c, vitamins:$v, locationIds:$l, state:"Available"}')"
  req POST /dishes "$body"; expect "200 201" "POST /dishes '$name'"
  echo "  + страва '$name' ($type)"
done

# ---------- 3. користувачі ----------
# ensure_user EMAIL PASSWORD FIRST LAST — реєструє, якщо /signin не проходить
ensure_user() {
  local email="$1" pass="$2" first="$3" last="$4" body
  body="$(jq -n --arg e "$email" --arg p "$pass" '{email:$e, password:$p}')"
  req POST /signin "$body"
  if [[ "$HTTP_CODE" == 200 ]]; then echo "  = юзер $email вже є"; return; fi
  body="$(jq -n --arg f "$first" --arg l "$last" --arg e "$email" --arg p "$pass" \
    '{firstName:$f, lastName:$l, email:$e, password:$p, confirmPassword:$p}')"
  req POST /register "$body"
  # 500 тут = юзер уже є в Cognito з іншим паролем (креденшел-файл загублено/змінено)
  expect "200 201" "POST /register $email (якщо юзер уже існує з іншим паролем — видали його в Cognito або віднови $CREDS_FILE)"
  echo "  + юзер $email"
}
ensure_user "$CUSTOMER_EMAIL" "$CUSTOMER_PASSWORD" "Olena"  "Customer"
ensure_user "$WAITER1_EMAIL"  "$WAITER1_PASSWORD"  "Taras"  "Waiter"
ensure_user "$WAITER2_EMAIL"  "$WAITER2_PASSWORD"  "Iryna"  "Waiter"

# ---------- 4. waiter-и → локації ----------
assign_waiter() { # EMAIL LOCATION_ID
  req POST /waiters "$(jq -n --arg e "$1" --arg l "$2" '{email:$e, locationId:$l}')"
  case "$HTTP_CODE" in
    200|201) echo "  + $1 → waiter локації $2" ;;
    409)     echo "  = $1 уже waiter" ;;
    *)       die "POST /waiters $1 → HTTP $HTTP_CODE: $HTTP_BODY" ;;
  esac
}
assign_waiter "$WAITER1_EMAIL" "${LOC_IDS[0]}"
assign_waiter "$WAITER2_EMAIL" "${LOC_IDS[1]}"

# ---------- 5. перевірка ролі Waiter ----------
jwt_payload() { # base64url → JSON
  local p; p="$(cut -d. -f2 <<<"$1" | tr '_-' '/+')"
  while (( ${#p} % 4 )); do p+="="; done
  base64 --decode <<<"$p" 2>/dev/null
}
# signin_token EMAIL PASSWORD → друкує idToken
signin_token() {
  req POST /signin "$(jq -n --arg e "$1" --arg p "$2" '{email:$e, password:$p}')"
  expect 200 "POST /signin $1"
  jq -r '.idToken' <<<"$HTTP_BODY"
}
verify_waiter() { # EMAIL PASSWORD EXPECTED_LOCATION_ID → друкує idToken
  local token groups role
  token="$(signin_token "$1" "$2")"
  groups="$(jwt_payload "$token" | jq -c '."cognito:groups" // []')"
  jq -e 'index("Waiter") != null' <<<"$groups" >/dev/null || die "$1: у токені немає групи Waiter ($groups)"
  req GET /me "" "$token"; expect 200 "GET /me ($1)"
  role="$(jq -r '.role' <<<"$HTTP_BODY")"
  [[ "$role" == Waiter ]] || die "$1: GET /me повертає role=$role, очікував Waiter"
  echo "  ✔ $1: cognito:groups=$groups, /me role=$role" >&2
  echo "$token"
}
log "Перевіряю ролі"
WAITER1_TOKEN="$(verify_waiter "$WAITER1_EMAIL" "$WAITER1_PASSWORD")"
verify_waiter "$WAITER2_EMAIL" "$WAITER2_PASSWORD" >/dev/null
CUSTOMER_TOKEN="$(signin_token "$CUSTOMER_EMAIL" "$CUSTOMER_PASSWORD")"
req GET /me "" "$CUSTOMER_TOKEN"; expect 200 "GET /me (customer)"
echo "  ✔ $CUSTOMER_EMAIL: /me role=$(jq -r '.role' <<<"$HTTP_BODY")"

# ---------- 6. столи ----------
# list_tables LOCATION_ID → JSON-масив столів локації.
# Основний шлях — GET /locations/{id}/tables/all (лише Waiter). Якщо API Gateway цього ресурсу не має
# (конфлікт імен path-параметрів {id}/{locationId} — Syndicate його не створює), fallback:
# публічний GET /available-tables на дату через рік (там ще немає бронювань → видно всі столи).
# Бекенд збирає той список паралельно в List<T> без локу, тож рідко губить елементи —
# об'єднуємо 3 запити за id, щоб не створити дубль.
FAR_START="$(date -u -v+1y +%Y-%m-%dT10:00:00Z 2>/dev/null || date -u -d '+1 year' +%Y-%m-%dT10:00:00Z)"
list_tables() {
  local loc="$1" acc='[]' k
  req GET "/locations/$loc/tables/all" "" "$WAITER1_TOKEN"
  if [[ "$HTTP_CODE" == 200 ]]; then echo "$HTTP_BODY"; return; fi
  for k in 1 2 3; do
    req GET "/available-tables?locationId=$loc&startTime=$FAR_START"; expect 200 "GET /available-tables ($loc)"
    acc="$(jq -c --argjson b "$HTTP_BODY" '. + $b | unique_by(.id)' <<<"$acc")"
  done
  echo "$acc"
}

TABLE_CAPACITIES=(2 4 4 6)   # tableNumber = індекс + 1
log "Столи"
for i in 0 1; do
  loc="${LOC_IDS[$i]}"
  existing="$(list_tables "$loc")"
  for n in 1 2 3 4; do
    cap="${TABLE_CAPACITIES[$((n-1))]}"
    if jq -e --argjson n "$n" 'any(.[]; .tableNumber == $n)' <<<"$existing" >/dev/null; then
      echo "  = стіл #$n у $loc вже є"; continue
    fi
    req POST /tables "$(jq -n --arg l "$loc" --argjson c "$cap" --argjson n "$n" '{locationId:$l, capacity:$c, tableNumber:$n}')"
    expect "200 201" "POST /tables #$n @ $loc"
    echo "  + стіл #$n ($cap місць) у $loc"
  done
done

# ---------- 7. фото страв (опційно) ----------
# Кладеш <slug>.jpg|.jpeg|.png|.webp у SEED_IMAGES_DIR (за замовчуванням ../dish-images поза репо),
# slug = назва страви в нижньому регістрі, не-літери/цифри → "-" (напр. honey-cake-medovik.png).
# Вантажимо лише стравам без imageUrl (FORCE_IMAGES=1 — перезалити всі). PUT /dishes/{id}
# вимагає будь-який Cognito-токен; бекенд кладе файл у images-бакет і пише imageUrl.
IMAGES_DIR="${SEED_IMAGES_DIR:-$REPO_ROOT/../dish-images}"
slugify() { tr '[:upper:]' '[:lower:]' <<<"$1" | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g'; }
if [[ -d "$IMAGES_DIR" ]]; then
  log "Фото страв з $IMAGES_DIR"
  req GET /dishes; expect 200 "GET /dishes"
  DISHES_JSON="$HTTP_BODY"
  TMP_IMG="$(mktemp -d)"; trap 'rm -rf "$TMP_IMG"' EXIT
  for row in "${DISHES[@]}"; do
    IFS='|' read -r name type price weight cal prot fats carbs vit desc <<<"$row"
    slug="$(slugify "$name")"; img=""
    for ext in jpg jpeg png webp; do [[ -f "$IMAGES_DIR/$slug.$ext" ]] && { img="$IMAGES_DIR/$slug.$ext"; break; }; done
    [[ -z "$img" ]] && { echo "  - немає $slug.{jpg,png,webp}"; continue; }
    dish="$(jq -c --arg n "$name" 'map(select(.name == $n)) | .[0] // empty' <<<"$DISHES_JSON")"
    [[ -z "$dish" ]] && die "страву '$name' не знайдено в GET /dishes"
    if [[ -z "${FORCE_IMAGES:-}" && -n "$(jq -r '.imageUrl // empty' <<<"$dish")" ]]; then
      echo "  = фото '$name' вже є"; continue
    fi
    # Стискаємо до 800px JPEG (~100–200 KB): Lambda-payload ≤ 6 MB, і сторінка не тягне мегабайти
    if command -v sips >/dev/null; then
      sips -s format jpeg -s formatOptions 82 -Z 800 "$img" --out "$TMP_IMG/$slug.jpg" >/dev/null && img="$TMP_IMG/$slug.jpg"
    fi
    b64="$(base64 <"$img" | tr -d '\n')"
    body="$(jq -n --arg n "$name" --arg d "$desc" --arg t "$type" --argjson p "$price" --arg w "$weight" \
      --arg cal "$cal" --arg pr "$prot" --arg f "$fats" --arg c "$carbs" --arg v "$vit" --argjson l "$LOC_IDS_JSON" \
      --rawfile img /dev/stdin \
      '{name:$n, description:$d, dishType:$t, price:$p, weight:$w, calories:$cal, proteins:$pr, fats:$f,
        carbohydrates:$c, vitamins:$v, locationIds:$l, state:"Available", imageBase64:($img | rtrimstr("\n"))}' <<<"$b64")"
    req PUT "/dishes/$(jq -r .id <<<"$dish")" "$body" "$CUSTOMER_TOKEN"; expect 200 "PUT /dishes '$name' (фото)"
    echo "  + фото '$name' ($(( $(wc -c <"$img") / 1024 )) KB)"
  done
fi

# ---------- підсумок ----------
log "Готово"
req GET /dishes; echo "  страв: $(jq length <<<"$HTTP_BODY")"
for i in 0 1; do
  echo "  ${LOC_ADDRESSES[$i]} (${LOC_IDS[$i]}):"
  list_tables "${LOC_IDS[$i]}" | jq -r 'sort_by(.tableNumber)[] | "    стіл #\(.tableNumber) (\(.capacity) місць) \(.id)"'
done
echo "  креденшели: $CREDS_FILE"

# Owner Flow — специфікація з Figma (для верстки)

Джерело: Figma «EPAM IT Marathon 6.0» → сторінка *✅ DESIGN FOR DEVELOPERS* → секція **Owner Flow** (`112080:121591`). Витягнуто через Figma MCP 05.11.2026.
Читай разом з `README.md` поруч — там, що з макета **не** входить у домашку (кнопки −/+, кошик біля страв, «Edit my order», сповіщення «Maria has filled…»). Ця специфікація їх не скасовує.

| Файл | Що це | Figma node |
|---|---|---|
| `01-owner-review.png` | Owner бачить кошики, Submit активна | — (експорт організаторів) |
| `02-sent-to-restaurant.png` / `figma/02-sent-modal-figma.png` | стан «Sent to Restaurant», Submit неактивна | `112080:124878` |
| `03-remove-guest.png` / `figma/03-remove-guest-modal-figma.png` | діалог Remove Guest | `112080:124573` |

## Токени → змінні проєкту
Усі кольори макета вже є в `src/app/shared/styles/abstracts/_colors.scss` — **не хардкодь hex**, `@use '…/abstracts/colors' as color` і `color.$…`. Шрифт — Poppins (уже підключений), типографіка — `abstracts/_typography.scss`.

| Figma | Значення | SCSS |
|---|---|---|
| Neutral 900 (основний текст) | #232323 | `color.$neutral-900` |
| Neutral 600 (коментар, буква-аватар) | #575757 | `color.$neutral-600` |
| Neutral 400 (неактивна кнопка) | #898989 | `color.$neutral-400` |
| Neutral 200 (рамки) | #DADADA | `color.$neutral-200` |
| Neutral 100 (фон модалки/сторінки, роздільники в картці) | #F7F7F7 | `color.$neutral-100` |
| Neutral 0 (картки) | #FFFFFF | `color.$neutral-0` |
| Green 100 (фон буква-аватара) | #E9FFEA | `color.$green-100` |
| Green 600 | #0F7A37 | `color.$green-600` |
| Green 800 (сума Grand Total) | #0B5B29 | `color.$green-800` |
| Активна кнопка Submit | градієнт 120° #0B5B29 → #0F7A37 | `color.$green-gradient` |
| BG Green (роздільники шапки й низу) | #D9E5D9 | `color.$bg-green` |
| Orange 100 (бейдж «Sent to Restaurant») | #FFF2D4 | `color.$orange-100` |
| Red 100 (бейдж Rejected) | #FCE9ED | `color.$red-100` |
| Red 400 (Remove Guest у діалозі) | #B70B0B | `color.$red-400` |
| Grey Shadow | 0 0 10px 4px #DADADAB2 | `color.$shadow` або локально |

| Стиль тексту | Poppins | розмір / line-height |
|---|---|---|
| H3 (заголовок, Grand Total) | Medium 500 | 26 / 40 |
| H4 (назва картки, Subtotal) | Medium 500 | 18 / 32 |
| H5 (сума Subtotal) | Medium 500 | 16 / 24 |
| Button light (текстові кнопки) | Medium 500 | 16 / 24 |
| Button L (Submit, Remove Guest) | SemiBold 600 | 16 / 24 |
| Body bold (назва страви, ціна) | Medium 500 | 14 / 24 |
| Body (деталі бронювання, текст діалогу) | Regular 400 | 14 / 24 |
| Caption (коментар, Qty, бейджі) | Regular 400 | 12 / 16 |

## Екран Owner (`/reservations/:id/group-order`)
Макет — модалка 835 px; у домашці це сторінка: контейнер `max-width: 835px`, по центру, ті самі відступи.

- **Контейнер:** фон `neutral-100`, `border-radius: 24px`, тінь Grey Shadow, padding 40 px. Кнопка закриття (необов'язково на сторінці) — іконка 24 px, `top/right: 16px`, padding 8.
- **Шапка** (`gap: 4px`):
  - рядок заголовка: `<h1>` «Group Pre-Order» (H3) + бейдж статусу, `gap: 8px`, `align-items: center`;
  - рядок деталей: адреса · дата · час · «N Guests», кожен — іконка 16 px + текст Body, `gap: 8px` всередині, `gap: 16px` між елементами, `padding: 8px 0`, `border-bottom: 1px solid bg-green`. Іконки вже є в проєкті (location, calendar, clock, people — шукай у `src/assets` / наявних компонентах картки бронювання).
- **Бейдж статусу:** `padding: 4px 12px`, `border-radius: 8px`, Caption, текст `neutral-900`. «Sent to Restaurant» — фон `orange-100`. «Collecting pre-orders» — нейтральний фон (`neutral-200`, як «Created New Pre-Order» в `01-owner-review.png`).
- **Список учасників:** `<ul>`, `gap: 24px` між картками.
- **Картка учасника** (`<li>` → `<article>`): фон `neutral-0`, `border-radius: 12px`, padding 16, `gap: 12px`.
  - **Top:** `justify-content: space-between`. Ліворуч — «My Pre-Order» / «Guest: Alex» (H4) + статус кошика текстом (Submitted / Still choosing — додається домашкою, Caption або бейдж). Праворуч — «Remove Guest» (лише гості, рівень 3): текстова кнопка Button light, `border-bottom: 1px solid neutral-900`, `padding: 4px 8px`, висота 32.
  - **Рядок страви** (`gap: 16px`, `padding: 8px 12px`, `border-bottom: 1px solid neutral-100`, в останнього рядка без межі):
    - фото 48×48, `border-radius: 50%`, `object-fit: cover`; якщо фото немає — коло 48 px фон `green-100`, перша літера назви по центру, 16/24 Regular `neutral-600`;
    - блок інфо (`flex: 1`, `gap: 2px`, колонка): назва — Body bold, `text-overflow: ellipsis`; коментар (якщо є) — Caption `neutral-600`;
    - кількість — **текстом**, як варіант компонента в Figma: «Qty: 1», Caption `neutral-600` (кнопок −/+ немає — див. README);
    - ціна — ширина 60 px, `text-align: right`, Body bold.
  - **Bottom:** роздільник 1 px `neutral-100`; рядок `padding: 0 12px`, `space-between`: «Subtotal» (H4) і сума (H5).
- **Низ сторінки** (`gap: 16px`): роздільник 1 px `bg-green`; рядок «Grand Total» (H3) і сума (H3, `green-800`); кнопка Submit Pre-Order — ширина 100 %, висота 56, `border-radius: 8px`, Button L, білий текст; активна — `green-gradient`, неактивна — фон `neutral-400`.
- Плашка «N participants haven't submitted…» (з домашки, у Figma немає) — між шапкою і списком; стиль як сповіщення в `01-owner-review.png`: світло-жовтий фон (`color.$orange-30`), рамка `orange-100`, `border-radius: 8px`, padding 12–16, іконка дзвоника 16–24 px, Body.

## Діалог Remove Guest (через спільний `<app-modal>`)
- ширина 520 px, фон `neutral-0`, `border-radius: 24px`, padding 40, `gap: 24px`, тінь Grey Shadow; кнопка закриття — як вище;
- заголовок «Remove Guest» (H3);
- текст Body: «Are you sure you want to remove **Alex** and their pre-order from this reservation? This action cannot be undone.» (ім'я — жирним; у Figma «his», домашка вимагає «their» — бери текст з домашки);
- дії праворуч, `gap: 16px`: «Cancel» — текстова кнопка (як Remove Guest у картці); «Remove Guest» — outline-danger: висота 44, `padding: 8px 24px`, `border: 1.4px solid red-400`, `border-radius: 8px`, текст `red-400` Button L, фон білий.
- Перевір, що в `src/app/shared/styles/_modal.scss` і `buttons.scss` уже немає готових класів для цих кнопок — **спершу перевикористай наявні**.

## Адаптив (у Figma немає — вимога домашки)
≤ 375 px: padding контейнера 16; рядок деталей переноситься (`flex-wrap: wrap`); у рядку страви ціна й Qty не налазять на назву (назва з ellipsis).

## Що ще є в секції Owner Flow (поза домашкою)
- «Automatic Pre-order Hard Lock (T-45 / T-30 Minutes Cut-off)» — `112599:146455`, `112599:148441`: дизайнери вже передбачили автоблокування pre-order перед візитом. Корисно для фічі A9 «дедлайн збору».
- «Guest Invite Link Modal» — `112080:145288`; «Cart — Owner's Pre-Order» — `112080:124056`.

/**
 * Повний підпис столу для заголовків: «Table 7».
 * Вже готовий підпис («Table 7», «Unknown table») лишається як є, голий номер («7») отримує префікс,
 * порожнє значення → «Unknown table». Захищає від «Table Table 7».
 */
export function toTableLabel(name: string | number | null | undefined): string {
  const value = `${name ?? ''}`.trim();
  if (!value) {
    return 'Unknown table';
  }
  return /^(table\b|unknown\b)/i.test(value) ? value : `Table ${value}`;
}

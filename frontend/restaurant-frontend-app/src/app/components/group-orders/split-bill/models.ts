/** Стан завантаження блоку «Split bill». */
export type SplitBillLoadState = 'loading' | 'ready' | 'error';

/** Рядок страви учасника: «Caesar Salad ×2» + сума. */
export interface SplitBillDishRow {
  dishId: string;
  label: string;
  amount: number;
}

/** Учасник у блоці «Split bill». */
export interface SplitBillPersonRow {
  key: string;
  name: string;
  isOwner: boolean;
  dishes: SplitBillDishRow[];
  total: number;
}

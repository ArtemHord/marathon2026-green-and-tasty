/** Стан першого завантаження екрана. */
export type GroupOrderLoadState = 'loading' | 'ready' | 'not-found' | 'error';

/** Рядок блоку «Consolidated pre-order». */
export interface ConsolidatedLine {
  dishId: string;
  label: string;
  amount: number;
}

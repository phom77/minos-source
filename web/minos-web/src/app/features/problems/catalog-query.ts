import { ParamMap } from '@angular/router';

export interface CatalogQuery { readonly page: number; readonly pageSize: number; }
export function readCatalogQuery(params: ParamMap): CatalogQuery | null {
  const read = (key: string, fallback: number) => {
    const values = params.getAll(key);
    if (!values.length) return fallback;
    return values.length === 1 && /^[1-9]\d*$/.test(values[0]) ? Number(values[0]) : NaN;
  };
  const page = read('page', 1), pageSize = read('pageSize', 20);
  return Number.isSafeInteger(page) && page <= 2_147_483_647 && Number.isSafeInteger(pageSize) && pageSize >= 1 && pageSize <= 100 &&
    (page - 1) * pageSize <= 2_147_483_647 ? { page, pageSize } : null;
}

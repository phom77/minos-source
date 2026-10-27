import { ParamMap } from '@angular/router';

export type Difficulty = 'Easy' | 'Medium' | 'Hard';
export type SolveStatus = 'NotAttempted' | 'Attempted' | 'Solved';
export type CatalogSort = 'Number' | 'Newest' | 'DifficultyAscending' | 'DifficultyDescending' | 'Title';
export interface CatalogQuery {
  readonly page: number; readonly pageSize: number; readonly q?: string;
  readonly difficulty?: Difficulty | 'Unclassified'; readonly topic?: string; readonly status?: SolveStatus; readonly sort?: CatalogSort;
  readonly match?: 'All' | 'Any'; readonly difficultyIsNot?: 'true'; readonly topicIsNot?: 'true'; readonly statusIsNot?: 'true';
}
export function readCatalogQuery(params: ParamMap): CatalogQuery | null {
  const read = (key: string, fallback: number) => {
    const values = params.getAll(key);
    if (!values.length) return fallback;
    return values.length === 1 && /^[1-9]\d*$/.test(values[0]) ? Number(values[0]) : NaN;
  };
  const page = read('page', 1), pageSize = read('pageSize', 20);
  if (!(Number.isSafeInteger(page) && page <= 2_147_483_647 && Number.isSafeInteger(pageSize) && pageSize >= 1 && pageSize <= 100 &&
    (page - 1) * pageSize <= 2_147_483_647)) return null;
  const query: Record<string, string | number> = { page, pageSize };
  for (const key of ['q', 'difficulty', 'topic', 'status', 'sort', 'match', 'difficultyIsNot', 'topicIsNot', 'statusIsNot']) {
    const values = params.getAll(key);
    if (values.length > 1) return null;
    if (!values.length) continue;
    const value = values[0];
    if (key === 'q') { if (value.length > 120 || /[\u0000-\u001f\u007f]/.test(value)) return null; if (value.trim()) query[key] = value.trim(); }
    else if (key === 'topic') { if (!/^[a-z]+(?:-[a-z]+)*$/.test(value) || value.length > 48) return null; query[key] = value; }
    else if (key.endsWith('IsNot')) { if (value !== 'true') return null; query[key] = value; }
    else if (key === 'match') { if (!['All', 'Any'].includes(value)) return null; query[key] = value; }
    else {
      const allowed = key === 'difficulty' ? ['Easy', 'Medium', 'Hard', 'Unclassified'] : key === 'status'
        ? ['NotAttempted', 'Attempted', 'Solved'] : ['Number', 'Newest', 'DifficultyAscending', 'DifficultyDescending', 'Title'];
      if (!allowed.includes(value)) return null;
      query[key] = value;
    }
  }
  for (const key of ['difficulty', 'topic', 'status']) if (query[key + 'IsNot'] && !query[key]) return null;
  return query as unknown as CatalogQuery;
}

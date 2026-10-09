import { inject, Injectable } from '@angular/core';
import { catchError, from, map, mergeMap, of, scan, startWith } from 'rxjs';
import { ProblemsApi } from '../../problems/data-access/problems-api';

export interface ProblemLabel { readonly title: string; readonly versionNumber: number | null; }
/** Public titles only. Bounded parallel reads avoid one detail/source request per history row. */
@Injectable({ providedIn: 'root' })
export class ProblemLabels {
  private readonly api = inject(ProblemsApi);
  private readonly cache = new Map<string, ProblemLabel>();
  read(versionIds: readonly string[]) {
    const ids = [...new Set(versionIds)];
    return from(ids).pipe(mergeMap(id => {
      const cached = this.cache.get(id);
      return cached ? of({ id, label: cached }) : this.api.version(id).pipe(map(problem => {
        const label = { title: problem.title, versionNumber: problem.versionNumber };
        if (this.cache.size >= 100) this.cache.delete(this.cache.keys().next().value!);
        this.cache.set(id, label); return { id, label };
      }), catchError(() => of({ id, label: { title: 'Unavailable problem', versionNumber: null } })));
    }, 4), scan((labels, item) => ({ ...labels, [item.id]: item.label }), {} as Record<string, ProblemLabel>), startWith({} as Record<string, ProblemLabel>));
  }
}

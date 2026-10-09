import { catchError, defer, EMPTY, expand, filter, map, Observable, of, switchMap, take, timer } from 'rxjs';

export type ReadResult<T> = { readonly data: T; readonly error: null } | { readonly data: null; readonly error: unknown };
/** Serial reads; hidden tabs pause, failures stop until explicit refresh, unsubscribe cancels reads/timers. */
export function pollSubmission<T>(read: () => Observable<T>, active: (data: T) => boolean, document: Document): Observable<ReadResult<T>> {
  const request = () => defer(read).pipe(map((data): ReadResult<T> => ({ data, error: null })), catchError((error: unknown) => of<ReadResult<T>>({ data: null, error })));
  return request().pipe(expand(result => result.data !== null && active(result.data)
    ? timer(2500, 2500).pipe(filter(() => document.visibilityState !== 'hidden'), take(1), switchMap(request)) : EMPTY));
}

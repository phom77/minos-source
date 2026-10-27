import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { MINOS_API_REQUEST } from '../../../core/http/api-url.interceptor';
import { isProblemId } from '../../problems/data-access/problems-api';

export interface ProblemComment {
  readonly id: string; readonly problemId: string; readonly publishedVersionId: string; readonly parentCommentId: string | null;
  readonly authorName: string; readonly body: string | null; readonly isSpoiler: boolean;
  readonly state: 'Visible' | 'Deleted' | 'Hidden'; readonly revision: number;
  readonly createdAtUtc: string; readonly updatedAtUtc: string; readonly replyCount: number;
  readonly canEdit: boolean; readonly canDelete: boolean; readonly canHide: boolean;
}
export interface CommentPage { readonly items: readonly ProblemComment[]; readonly nextCursor: string | null; }
export interface CommentInput { readonly body: string; readonly isSpoiler: boolean; }
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;
function comment(value: unknown, problemId: string): value is ProblemComment {
  return record(value) && isProblemId(String(value['id'])) && value['problemId'] === problemId &&
    isProblemId(String(value['publishedVersionId'])) && (value['parentCommentId'] === null || isProblemId(String(value['parentCommentId']))) &&
    typeof value['authorName'] === 'string' && value['authorName'].length > 0 && value['authorName'].length <= 80 &&
    ['Visible', 'Deleted', 'Hidden'].includes(String(value['state'])) &&
    (value['state'] === 'Visible' ? typeof value['body'] === 'string' && value['body'].length <= 4000 : value['body'] === null) &&
    typeof value['revision'] === 'number' && Number.isSafeInteger(value['revision']) && value['revision'] > 0 &&
    typeof value['replyCount'] === 'number' && Number.isSafeInteger(value['replyCount']) && value['replyCount'] >= 0 &&
    ['isSpoiler', 'canEdit', 'canDelete', 'canHide'].every(key => typeof value[key] === 'boolean') &&
    ['createdAtUtc', 'updatedAtUtc'].every(key => typeof value[key] === 'string' && Number.isFinite(Date.parse(value[key] as string)));
}

@Injectable({ providedIn: 'root' })
export class CommentsApi {
  private readonly http = inject(HttpClient);
  list(problemId: string, parentId: string | null = null, after: string | null = null) {
    return this.http.get<unknown>(this.url(problemId) + (parentId ? `/${parentId}/replies` : ''), {
      ...this.options(), params: { pageSize: 20, ...(after ? { after } : {}) },
    }).pipe(map(value => {
      if (!record(value) || !Array.isArray(value['items']) || value['items'].length > 20 ||
          !value['items'].every(item => comment(item, problemId) && item.parentCommentId === parentId) ||
          !(value['nextCursor'] === null || isProblemId(String(value['nextCursor'])))) throw new Error('Invalid comments response.');
      return value as unknown as CommentPage;
    }));
  }
  create(problemId: string, versionId: string, input: CommentInput, parentId: string | null = null) {
    return this.http.post<unknown>(this.url(problemId), { ...input, publishedVersionId: versionId, parentCommentId: parentId }, this.options())
      .pipe(map(value => this.read(value, problemId, parentId)));
  }
  edit(problemId: string, value: ProblemComment, input: CommentInput) {
    return this.http.put<unknown>(`${this.url(problemId)}/${value.id}`, { ...input, expectedRevision: value.revision }, this.options())
      .pipe(map(response => {
        const result = this.read(response, problemId, value.parentCommentId);
        if (result.id !== value.id) throw new Error('Invalid edited comment.');
        return result;
      }));
  }
  remove(problemId: string, value: ProblemComment, action: 'delete' | 'hide') {
    return this.http.post<void>(`${this.url(problemId)}/${value.id}/${action}`, { expectedRevision: value.revision }, this.options());
  }
  private read(value: unknown, problemId: string, parentId: string | null): ProblemComment {
    if (!comment(value, problemId) || value.parentCommentId !== parentId) throw new Error('Invalid comment response.');
    return value;
  }
  private url(problemId: string) { return `/api/problems/${problemId}/comments`; }
  private options() { return { context: new HttpContext().set(MINOS_API_REQUEST, true), timeout: 15_000 }; }
}

export function commentError(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  if (status === 401) return 'Your session has ended. Sign in again.';
  if (status === 403) return 'You do not have permission for this action.';
  if (status === 404) return 'This problem or comment is no longer available.';
  if (status === 409) return 'This comment changed. Refresh comments before trying again.';
  if (status === 429) return 'Too many comment actions. Wait a minute and try again.';
  if (status === 400) return 'Check your comment. It may have been changed or removed.';
  return 'Could not complete this action. Refresh comments to check before trying again.';
}

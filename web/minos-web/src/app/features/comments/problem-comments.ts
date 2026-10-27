import { DatePipe, DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, inject, Injector, input, signal, untracked } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { AuthPrompt } from '../../core/auth/auth-prompt.service';
import { SessionService } from '../../core/auth/session.service';
import { CommentBody } from './comment-body';
import { CommentEditor } from './comment-editor';
import { UiIcon } from '../../shared/ui/ui-icon';
import { CommentInput, commentError, CommentsApi, ProblemComment } from './data-access/comments-api';

interface Thread { readonly items: readonly ProblemComment[]; readonly nextCursor: string | null; readonly loading: boolean; readonly error: string | null; }
@Component({ selector: 'minos-problem-comments', imports: [DatePipe, NgTemplateOutlet, CommentBody, CommentEditor, UiIcon],
  changeDetection: ChangeDetectionStrategy.OnPush, templateUrl: './problem-comments.html', styleUrl: './problem-comments.css' })
export class ProblemComments {
  readonly problemId = input.required<string>(); readonly publishedVersionId = input.required<string>();
  readonly session = inject(SessionService); readonly auth = inject(AuthPrompt);
  private readonly api = inject(CommentsApi); private readonly document = inject(DOCUMENT); private readonly injector = inject(Injector);
  private requests = new Subscription(); private generation = 0;
  private scope = ''; private loaded = false;
  readonly expanded = signal(false); readonly activated = signal(false); readonly tipsVisible = signal(true);
  readonly openThreads = signal<ReadonlySet<string>>(new Set());
  readonly roots = signal<readonly ProblemComment[]>([]); readonly nextCursor = signal<string | null>(null);
  readonly loading = signal(false); readonly loadError = signal<string | null>(null); readonly threads = signal<ReadonlyMap<string, Thread>>(new Map());
  readonly editing = signal<ProblemComment | null>(null); readonly replying = signal<string | null>(null);
  readonly confirm = signal<{ value: ProblemComment; action: 'delete' | 'hide' } | null>(null);
  readonly editInitial = signal<CommentInput>({ body: '', isSpoiler: false }); readonly rootInitial = signal<CommentInput>({ body: '', isSpoiler: false });
  readonly emptyInput: CommentInput = { body: '', isSpoiler: false };
  readonly pending = signal(false); readonly writeError = signal<string | null>(null); readonly notice = signal('');
  readonly revealed = signal<ReadonlySet<string>>(new Set()); readonly composerRevision = signal(0);
  constructor() {
    effect(() => {
      const scope = this.problemId() + ':' + this.publishedVersionId(); this.session.account();
      untracked(() => {
        if (this.scope !== scope) { this.scope = scope; this.expanded.set(false); this.activated.set(false); this.tipsVisible.set(true); }
        this.generation++; this.requests.unsubscribe(); this.requests = new Subscription();
        this.loaded = false; this.openThreads.set(new Set());
        this.roots.set([]); this.nextCursor.set(null); this.threads.set(new Map()); this.cancelInteraction();
        this.pending.set(false); this.loading.set(false); this.loadError.set(null); this.notice.set(''); this.revealed.set(new Set()); this.rootInitial.set({ ...this.emptyInput });
        if (this.expanded()) this.load(false);
      });
    });
    inject(DestroyRef).onDestroy(() => this.requests.unsubscribe());
  }
  toggleDiscussion(): void {
    this.expanded.update(value => !value);
    if (this.expanded()) {
      this.activated.set(true);
      if (!this.loaded) this.load();
    }
  }
  toggleReplies(value: ProblemComment): void {
    if (this.pending()) return;
    if (this.openThreads().has(value.id)) this.openThreads.update(ids => new Set([...ids].filter(id => id !== value.id)));
    else {
      this.openThreads.update(ids => new Set([...ids, value.id]));
      if (!this.threads().has(value.id)) this.loadReplies(value.id);
    }
  }
  initial(name: string): string { return Array.from(name.trim())[0]?.toLocaleUpperCase() ?? '?'; }
  load(more = false): void {
    if (!this.expanded() || this.loading() || this.pending()) return;
    const retrying = this.loadError() !== null;
    this.loading.set(true); this.loadError.set(null);
    const generation = this.generation;
    this.requests.add(this.api.list(this.problemId(), null, more ? this.nextCursor() : null).subscribe({
      next: page => {
        if (generation !== this.generation) return;
        this.loaded = true;
        this.roots.set(more ? this.merge(this.roots(), page.items) : page.items); this.nextCursor.set(page.nextCursor); this.loading.set(false);
        if (more && page.items.length) this.focus('comment-' + page.items[0].id);
        else if (retrying) this.focus('comments-heading');
      },
      error: () => { if (generation === this.generation) { this.loading.set(false); this.loadError.set('Comments could not be loaded.'); } },
    }));
  }
  refresh(): void {
    if (this.loading() || this.pending()) return;
    this.generation++; this.requests.unsubscribe(); this.requests = new Subscription();
    this.threads.set(new Map()); this.openThreads.set(new Set()); this.cancelInteraction(); this.load(false);
  }
  loadReplies(rootId: string, more = false): void {
    const old = this.threads().get(rootId);
    if (old?.loading || this.pending()) return;
    this.setThread(rootId, { items: old?.items ?? [], nextCursor: old?.nextCursor ?? null, loading: true, error: null });
    const generation = this.generation;
    this.requests.add(this.api.list(this.problemId(), rootId, more ? old?.nextCursor ?? null : null).subscribe({
      next: page => {
        if (generation !== this.generation) return;
        this.setThread(rootId, { ...page, items: more ? this.merge(old?.items ?? [], page.items) : page.items, loading: false, error: null });
        this.focus('comment-' + (page.items[0]?.id ?? rootId));
      },
      error: () => { if (generation === this.generation) this.setThread(rootId, { items: old?.items ?? [], nextCursor: old?.nextCursor ?? null, loading: false, error: 'Replies could not be loaded.' }); },
    }));
  }
  reveal(id: string): void { this.revealed.update(value => new Set([...value, id])); this.focus('comment-' + id); }
  startReply(value: ProblemComment): void {
    if (!this.session.account()) { this.auth.open(); return; }
    this.cancelInteraction(); this.replying.set(value.id); this.composerRevision.update(value => value + 1);
  }
  startEdit(value: ProblemComment): void {
    this.cancelInteraction(); this.editInitial.set({ body: value.body ?? '', isSpoiler: value.isSpoiler }); this.editing.set(value);
  }
  startRemove(value: ProblemComment, action: 'delete' | 'hide'): void {
    this.cancelInteraction(); this.confirm.set({ value, action }); this.focus('comment-confirm-' + value.id);
  }
  cancelInteraction(restoreId?: string): void {
    this.editing.set(null); this.replying.set(null); this.confirm.set(null); this.writeError.set(null);
    if (restoreId) this.focus(restoreId);
  }
  post(input: CommentInput, parentId: string | null = null): void {
    if (parentId && this.threads().get(parentId)?.loading) return;
    this.mutate(this.api.create(this.problemId(), this.publishedVersionId(), input, parentId), value => {
      if (parentId) {
        this.openThreads.update(ids => new Set([...ids, parentId]));
        const thread = this.threads().get(parentId);
        if (thread) this.setThread(parentId, { ...thread, items: [value, ...thread.items] });
        else this.loadReplies(parentId);
        this.roots.update(roots => roots.map(root => root.id === parentId ? { ...root, replyCount: root.replyCount + 1 } : root));
        this.replying.set(null); this.focus('comment-reply-' + parentId);
      } else {
        this.roots.update(roots => [value, ...roots]); this.rootInitial.set({ ...this.emptyInput });
      }
      this.notice.set(parentId ? 'Reply posted.' : 'Comment posted.');
    });
  }
  saveEdit(input: CommentInput): void {
    const editing = this.editing(); if (!editing) return;
    this.mutate(this.api.edit(this.problemId(), editing, input), value => {
      this.replace({ ...value, replyCount: editing.replyCount }); this.editing.set(null); this.notice.set('Comment updated.');
      this.focus('comment-edit-' + value.id);
    });
  }
  remove(): void {
    const target = this.confirm(); if (!target) return;
    this.mutate(this.api.remove(this.problemId(), target.value, target.action), () => {
      this.replace({ ...target.value, body: null, isSpoiler: false, state: target.action === 'hide' ? 'Hidden' : 'Deleted',
        revision: target.value.revision + 1, canEdit: false, canDelete: false, canHide: false });
      this.confirm.set(null); this.notice.set(target.action === 'hide' ? 'Comment hidden.' : 'Comment deleted.');
      this.focus('comment-' + target.value.id);
    });
  }
  private mutate<T>(request: Observable<T>, success: (value: T) => void): void {
    if (this.pending() || this.loading() || !this.session.account()) return;
    this.pending.set(true); this.writeError.set(null); this.notice.set(''); const generation = this.generation;
    this.requests.add(request.subscribe({ next: value => {
      if (generation !== this.generation) return;
      this.pending.set(false); success(value);
    }, error: error => {
      if (generation !== this.generation) return;
      this.pending.set(false); this.writeError.set(commentError(error));
      this.focus('comments-write-error');
    } }));
  }
  private replace(value: ProblemComment): void {
    if (!value.parentCommentId) this.roots.update(roots => roots.map(root => root.id === value.id ? value : root));
    else {
      const thread = this.threads().get(value.parentCommentId);
      if (thread) this.setThread(value.parentCommentId, { ...thread, items: thread.items.map(reply => reply.id === value.id ? value : reply) });
    }
  }
  private setThread(id: string, value: Thread): void { this.threads.update(threads => new Map([...threads, [id, value]])); }
  private merge(old: readonly ProblemComment[], incoming: readonly ProblemComment[]): readonly ProblemComment[] {
    const ids = new Set(old.map(value => value.id)); return [...old, ...incoming.filter(value => !ids.has(value.id))];
  }
  private focus(id: string): void {
    afterNextRender(() => {
      const target = this.document.getElementById(id);
      if (this.expanded() && !target?.closest('[hidden]')) target?.focus();
    }, { injector: this.injector });
  }
}

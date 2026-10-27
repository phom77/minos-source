import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/auth/session.service';
import { AuthPrompt } from '../../core/auth/auth-prompt.service';
import { ProblemComments } from './problem-comments';
import { ProblemComment } from './data-access/comments-api';

const pid = '11111111-1111-4111-8111-111111111111', vid = '22222222-2222-4222-8222-222222222222';
const rid = '33333333-3333-4333-8333-333333333333', aid = '44444444-4444-4444-8444-444444444444';
const now = new Date().toISOString(), account = { id: aid, displayName: 'Writer', role: 'Solver' as const, createdAtUtc: now };
const root: ProblemComment = { id: rid, problemId: pid, publishedVersionId: vid, parentCommentId: null, authorName: 'Writer',
  body: 'A question', isSpoiler: true, state: 'Visible', revision: 1, createdAtUtc: now, updatedAtUtc: now,
  replyCount: 1, canEdit: true, canDelete: true, canHide: false };
const url = `/api/problems/${pid}/comments`;

describe('problem discussion journeys', () => {
  let fixture: ComponentFixture<ProblemComments>, http: HttpTestingController, session: SessionService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    session = TestBed.inject(SessionService); session.signedIn(account); http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProblemComments); fixture.componentRef.setInput('problemId', pid); fixture.componentRef.setInput('publishedVersionId', vid);
    fixture.detectChanges(); http.expectNone(url + '?pageSize=20');
    expect(fixture.nativeElement.querySelector('#discussion-toggle').getAttribute('aria-expanded')).toBe('false');
    fixture.componentInstance.toggleDiscussion(); fixture.detectChanges();
    http.expectOne(url + '?pageSize=20').flush({ items: [root], nextCursor: null }); fixture.detectChanges();
  });
  afterEach(() => http.verify({ ignoreCancelled: true }));
  const button = (fixture: ComponentFixture<ProblemComments>, label: string) =>
    Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(value => value.textContent?.trim() === label)!;

  it('hides spoilers until requested and preserves thread structure when an author deletes the root', async () => {
    expect(fixture.nativeElement.textContent).not.toContain('A question'); button(fixture, 'Show spoiler').click(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('A question');
    button(fixture, 'Delete').click(); fixture.detectChanges(); http.expectNone(request => request.method === 'POST');
    button(fixture, 'Confirm delete').click();
    const request = http.expectOne(url + '/' + rid + '/delete'); expect(request.request.body).toEqual({ expectedRevision: 1 }); request.flush(null);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Comment deleted.');
    expect(button(fixture, 'Show replies (1)')).toBeDefined(); expect(button(fixture, 'Edit')).toBeUndefined();
  });

  it('posts replies to a root and edits with the expected revision, without allowing a third level', () => {
    button(fixture, 'Reply').click(); fixture.detectChanges();
    fixture.componentInstance.post({ body: 'Answer', isSpoiler: false }, rid);
    const reply = { ...root, id: '55555555-5555-4555-8555-555555555555', parentCommentId: rid, body: 'Answer', isSpoiler: false, replyCount: 0 };
    const request = http.expectOne(url); expect(request.request.body.parentCommentId).toBe(rid); expect(request.request.body.publishedVersionId).toBe(vid);
    request.flush(reply); http.expectOne(url + '/' + rid + '/replies?pageSize=20').flush({ items: [reply], nextCursor: null }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.replies .comment-actions button')).toHaveLength(2);
    fixture.componentInstance.startEdit(root); fixture.detectChanges(); fixture.componentInstance.saveEdit({ body: 'Updated', isSpoiler: false });
    const edit = http.expectOne(url + '/' + rid); expect(edit.request.body.expectedRevision).toBe(1); edit.flush({ ...root, body: 'Updated', isSpoiler: false, revision: 2 }); fixture.detectChanges();
    expect(fixture.componentInstance.roots()[0].replyCount).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Updated');
  });

  it('keeps a draft after a failed save, exposes safe conflict feedback and never automatically retries', () => {
    const textarea = fixture.nativeElement.querySelector('#new-comment') as HTMLTextAreaElement;
    textarea.value = 'Keep my question'; textarea.dispatchEvent(new Event('input')); fixture.detectChanges();
    fixture.componentInstance.post({ body: textarea.value, isSpoiler: false });
    http.expectOne(url).flush({ detail: 'secret postgres trace' }, { status: 409, statusText: 'Conflict' }); fixture.detectChanges();
    expect(textarea.value).toBe('Keep my question'); expect(fixture.nativeElement.textContent).toContain('This comment changed');
    expect(fixture.nativeElement.textContent).not.toContain('secret postgres trace'); http.expectNone(request => request.method === 'POST');
  });

  it('cancels stale identity requests and resets drafts, spoilers and edit controls after sign-out', () => {
    fixture.componentInstance.startEdit(root); fixture.detectChanges();
    fixture.componentInstance.load(true); const pending = http.expectOne(url + '?pageSize=20');
    session.signedOut(); fixture.detectChanges(); expect(pending.cancelled).toBe(true);
    expect(fixture.nativeElement.querySelector('textarea')).toBeNull();
    http.expectOne(url + '?pageSize=20').flush({ items: [{ ...root, canEdit: false, canDelete: false }], nextCursor: null }); fixture.detectChanges();
    expect(button(fixture, 'Edit')).toBeUndefined(); expect(button(fixture, 'Sign in to comment')).toBeDefined();
    const open = vi.spyOn(TestBed.inject(AuthPrompt), 'open'); button(fixture, 'Reply').click(); expect(open).toHaveBeenCalled();
    session.signedIn({ ...account, id: '66666666-6666-4666-8666-666666666666' }); fixture.detectChanges();
    http.expectOne(url + '?pageSize=20').flush({ items: [], nextCursor: null }); fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#new-comment') as HTMLTextAreaElement).value).toBe('');
  });

  it('loads bounded independent root/reply pages and rejects rows from another problem', () => {
    fixture.componentInstance.load(); http.expectOne(url + '?pageSize=20').flush({ items: [root], nextCursor: rid }); fixture.detectChanges();
    button(fixture, 'More comments').click();
    http.expectOne(url + '?pageSize=20&after=' + rid).flush({ items: [{ ...root, id: '55555555-5555-4555-8555-555555555555', problemId: vid }], nextCursor: null }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Comments could not be loaded'); expect(fixture.componentInstance.roots()).toHaveLength(1);
    button(fixture, 'Show replies (1)').click();
    http.expectOne(url + '/' + rid + '/replies?pageSize=20').flush({ items: [], nextCursor: null }); fixture.detectChanges();
    expect(fixture.componentInstance.threads().get(rid)?.items).toHaveLength(0);
  });

  it('keeps the draft and loaded threads when collapsed and reopened without fetching again', () => {
    const textarea = fixture.nativeElement.querySelector('#new-comment') as HTMLTextAreaElement;
    textarea.value = 'Unsent question'; textarea.dispatchEvent(new Event('input')); fixture.detectChanges();
    button(fixture, 'Show replies (1)').click();
    http.expectOne(url + '/' + rid + '/replies?pageSize=20').flush({ items: [], nextCursor: null }); fixture.detectChanges();
    button(fixture, 'Hide replies (1)').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.replies').hidden).toBe(true);
    button(fixture, 'Show replies (1)').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.replies').hidden).toBe(false);
    button(fixture, 'Discussion').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#discussion-content').hidden).toBe(true);
    button(fixture, 'Discussion').click(); fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#new-comment') as HTMLTextAreaElement).value).toBe('Unsent question');
    http.expectNone(request => request.method === 'GET');
  });

  it('resets account-owned drafts while closed and lazily refreshes permissions on reopen', () => {
    const textarea = fixture.nativeElement.querySelector('#new-comment') as HTMLTextAreaElement;
    textarea.value = 'Private draft'; textarea.dispatchEvent(new Event('input'));
    fixture.componentInstance.toggleDiscussion(); fixture.detectChanges();
    session.signedOut(); fixture.detectChanges(); http.expectNone(request => request.method === 'GET');
    fixture.componentInstance.toggleDiscussion(); fixture.detectChanges();
    http.expectOne(url + '?pageSize=20').flush({ items: [], nextCursor: null }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('textarea')).toBeNull();
    expect(button(fixture, 'Sign in to comment')).toBeDefined();
  });

  it('cancels an in-flight reply read during refresh and resets the disclosure for another problem', () => {
    button(fixture, 'Show replies (1)').click();
    const pending = http.expectOne(url + '/' + rid + '/replies?pageSize=20');
    fixture.componentInstance.refresh(); expect(pending.cancelled).toBe(true);
    http.expectOne(url + '?pageSize=20').flush({ items: [root], nextCursor: null }); fixture.detectChanges();
    expect(fixture.componentInstance.openThreads().size).toBe(0);
    fixture.componentRef.setInput('problemId', vid); fixture.detectChanges();
    expect(fixture.componentInstance.expanded()).toBe(false); expect(fixture.componentInstance.activated()).toBe(false);
    expect(fixture.nativeElement.querySelector('textarea')).toBeNull(); http.expectNone(request => request.method === 'GET');
  });
});

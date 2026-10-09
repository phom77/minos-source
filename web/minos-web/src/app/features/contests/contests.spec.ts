import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { SessionService } from '../../core/auth/session.service';
import { SESSION_REQUIRED } from '../../core/http/session-error.interceptor';
import { ContestEditor } from './contest-editor';
import { ContestList } from './contest-list';
import { ContestProblemAuthoring } from './contest-problem-authoring';
import { Contest, ContestsApi, isContest } from './data-access/contests-api';
import { ContestProblemPicker } from './contest-problem-picker';

const cid = '11111111-1111-4111-8111-111111111111', pid = '22222222-2222-4222-8222-222222222222', vid = '33333333-3333-4333-8333-333333333333';
const account = { id: 'owner', displayName: 'An', role: 'Setter' as const, createdAtUtc: '2026-10-01T00:00:00Z' };
const contest: Contest = { id: cid, title: 'Weekly practice', description: 'Practice together', startsAtUtc: '2099-10-09T01:00:00Z', endsAtUtc: '2099-10-09T03:00:00Z',
  state: 'Draft', problemCount: 1, revision: 2, canManage: true, canRegister: false, canSubmit: false, isRegistered: false, serverTimeUtc: '2026-10-09T00:00:00Z',
  problems: [{ id: pid, problemId: pid, versionId: vid, order: 1, title: 'Sum' }] };

describe('contest workflows', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([
      { path: 'contests/new', component: ContestEditor }, { path: 'contests/:contestId/edit', component: ContestEditor },
      { path: 'contests/:contestId/problems/new', component: ContestProblemAuthoring }, { path: 'contests', component: ContestList },
    ])] });
    TestBed.inject(SessionService).signedIn(account); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function catalog() { http.expectOne('/api/problems?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 }); }
  it('shows organizer entry points only for Setter/Admin', async () => {
    TestBed.inject(SessionService).signedIn({ ...account, role: 'Solver' });
    const h = await RouterTestingHarness.create('/contests');
    http.expectOne('/api/contests?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 }); h.detectChanges();
    expect(h.routeNativeElement?.textContent).not.toContain('Create contest');
    TestBed.inject(SessionService).signedIn(account); h.detectChanges(); await h.fixture.whenStable();
    http.expectOne('/api/contests?page=1&pageSize=20').flush({ items: [], page: 1, pageSize: 20, total: 0 }); h.detectChanges();
    expect(h.routeNativeElement?.querySelector('a[href="/contests/new"]')).not.toBeNull();
  });
  it('denies solver editor access without fetching a draft or problem picker', async () => {
    TestBed.inject(SessionService).signedIn({ ...account, role: 'Solver' });
    const h = await RouterTestingHarness.create('/contests/new');
    expect(h.routeNativeElement?.querySelector('form')).toBeNull();
    expect(h.routeNativeElement?.textContent).toContain('Setter or Admin');
    http.expectNone(r => r.url.startsWith('/api/'));
  });
  it('does not reveal errors on unchanged blur; reveals edited errors and clears them on correction', async () => {
    const h = await RouterTestingHarness.create('/contests/new'); catalog(); h.detectChanges();
    const input = h.routeNativeElement!.querySelector<HTMLInputElement>('#contest-title')!;
    input.dispatchEvent(new Event('blur')); h.detectChanges(); expect(input.getAttribute('aria-invalid')).toBeNull();
    input.value = ' '; input.dispatchEvent(new Event('input')); input.dispatchEvent(new Event('blur')); h.detectChanges();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    input.value = 'Weekly'; input.dispatchEvent(new Event('input')); h.detectChanges(); expect(input.getAttribute('aria-invalid')).toBeNull();
  });
  it('allows invalid submit to show focused feedback without issuing a mutation', async () => {
    const h = await RouterTestingHarness.create('/contests/new'); catalog(); h.detectChanges();
    const button = h.routeNativeElement!.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(button.disabled).toBe(false); button.click(); h.detectChanges(); await h.fixture.whenStable();
    expect(h.routeNativeElement?.querySelector('[role="alert"]')?.textContent).toContain('highlighted');
    expect(document.activeElement?.getAttribute('role')).toBe('alert'); http.expectNone(r => r.method === 'POST');
  });
  it('saves UTC schedule and ordered pinned versions while preserving fields on conflict', async () => {
    const h = await RouterTestingHarness.create(`/contests/${cid}/edit`);
    http.expectOne('/api/contests/' + cid).flush(contest); h.detectChanges(); await h.fixture.whenStable(); catalog();
    const component = h.routeDebugElement!.componentInstance as ContestEditor;
    component.form.controls.title.setValue('Changed'); component.save();
    const request = http.expectOne('/api/contests/' + cid);
    expect(request.request.method).toBe('PUT'); expect(request.request.body.versionIds).toEqual([vid]); expect(request.request.body.revision).toBe(2);
    expect(request.request.body.startsAtUtc).toBe(contest.startsAtUtc.replace('Z', '.000Z'));
    request.flush({}, { status: 409, statusText: 'Conflict' }); h.detectChanges();
    expect(component.form.controls.title.value).toBe('Changed'); expect(h.routeNativeElement?.textContent).toContain('another tab');
  });
  it('clears private draft fields when the identity changes', async () => {
    const h = await RouterTestingHarness.create(`/contests/${cid}/edit`);
    http.expectOne('/api/contests/' + cid).flush(contest); h.detectChanges(); await h.fixture.whenStable(); catalog();
    TestBed.inject(SessionService).signedIn({ ...account, id: 'different', role: 'Solver' }); h.detectChanges(); await h.fixture.whenStable();
    expect(h.routeNativeElement?.textContent).not.toContain(contest.description);
    expect(h.routeNativeElement?.querySelector('form')).toBeNull();
  });
  it('keeps problem order and removes selected problems by identity', () => {
    const fixture = TestBed.createComponent(ContestProblemPicker);
    const first = { problemId: pid, versionId: vid, title: 'First' }, second = { problemId: cid, versionId: cid, title: 'Second' };
    fixture.componentRef.setInput('selected', [first, second]); fixture.detectChanges(); catalog();
    const changes = vi.fn(); fixture.componentInstance.changed.subscribe(changes);
    fixture.componentInstance.move(1, -1); expect(changes).toHaveBeenLastCalledWith([second, first]);
    fixture.componentInstance.remove(pid); expect(changes).toHaveBeenLastCalledWith([second]);
  });
  it('uses contest submission endpoint and current session protection, without practice fallback', () => {
    let result: unknown; TestBed.inject(ContestsApi).submit(cid, pid, 'int main(){}').subscribe(v => result = v);
    const request = http.expectOne(`/api/contests/${cid}/problems/${pid}/submissions`);
    expect(request.request.context.get(SESSION_REQUIRED)).toBe(true); expect(request.request.body).toEqual({ language: 'cpp20', source: 'int main(){}' });
    request.flush({ id: vid }); expect(result).toEqual({ id: vid });
  });
  it('rejects malformed contest details and wrong-resource responses', () => {
    expect(isContest({ ...contest, state: 'Unknown' })).toBe(false); expect(isContest({ ...contest, problems: [{ versionId: 'broken' }] })).toBe(false);
    const failure = vi.fn(); TestBed.inject(ContestsApi).detail(cid).subscribe({ error: failure });
    http.expectOne('/api/contests/' + cid).flush({ ...contest, id: pid }); expect(failure).toHaveBeenCalledOnce();
  });
  it('keeps a created problem draft recoverable when saving configuration fails', async () => {
    const h = await RouterTestingHarness.create(`/contests/${cid}/problems/new`);
    http.expectOne('/api/contests/' + cid).flush(contest); h.detectChanges();
    const component = h.routeDebugElement!.componentInstance as ContestProblemAuthoring;
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    component.form.patchValue({ title: 'Sum', statement: 'Print a sum' }); component.save();
    http.expectOne('/api/authoring/problems').flush({ id: pid, title: 'Sum', statement: 'Print a sum', samples: [], testCases: [] });
    http.expectOne(`/api/authoring/problems/${pid}/draft`).flush({}, { status: 500, statusText: 'Failure' }); h.detectChanges();
    expect(component.draft()?.id).toBe(pid); expect(component.form.controls.statement.value).toBe('Print a sum');
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { draft: pid } }));
  });
  it('retries contest attachment without publishing another problem version after a conflict', async () => {
    const h = await RouterTestingHarness.create(`/contests/${cid}/problems/new`);
    http.expectOne('/api/contests/' + cid).flush(contest); h.detectChanges();
    const component = h.routeDebugElement!.componentInstance as ContestProblemAuthoring;
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const draft = { id: pid, title: 'Sum', statement: 'Print a sum', timeLimitMilliseconds: 1000, memoryLimitMegabytes: 64, samples: [], testCases: [] };
    component.draft.set(draft);
    component.form.patchValue({ title: draft.title, statement: draft.statement, statementReviewed: true, samplesVerified: true, referenceSolutionChecked: true });
    component.publish();
    http.expectOne(`/api/authoring/problems/${pid}/draft`).flush(draft);
    http.expectOne(`/api/authoring/problems/${pid}/publish`).flush({ problemId: pid, versionId: vid });
    http.expectOne('/api/contests/' + cid).flush({ ...contest, problems: [], problemCount: 0 });
    const attach = http.expectOne('/api/contests/' + cid);
    expect(attach.request.body.versionIds).toEqual([vid]);
    attach.flush({}, { status: 409, statusText: 'Conflict' });
    expect(component.published()?.versionId).toBe(vid);
    component.publish();
    http.expectNone(r => r.url.startsWith('/api/authoring/'));
    http.expectOne('/api/contests/' + cid).flush(contest);
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/contests', cid, 'edit']);
  });
});

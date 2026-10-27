import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { SessionService } from '../../../core/auth/session.service';
import { SubmissionForm } from './submission-form';
import { account, completed, submission } from '../testing/submission-fixtures';
import { SubmissionDraft } from '../data-access/submission-draft';

describe('submission composer UX', () => {
  let fixture: ComponentFixture<SubmissionForm>, http: HttpTestingController;
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).signedIn(account); http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SubmissionForm); fixture.componentRef.setInput('problemId', submission.problemId);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
  });
  afterEach(() => { fixture.destroy(); http.verify(); });
  const textarea = () => fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
  function input(source: string) { textarea().value = source; textarea().dispatchEvent(new Event('input')); fixture.detectChanges(); }
  async function submit() { fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  it('keeps unchanged focus/blur and first edit quiet, then reveals errors on edit/blur and corrects immediately', () => {
    textarea().focus(); textarea().dispatchEvent(new Event('blur')); fixture.detectChanges(); expect(textarea().getAttribute('aria-invalid')).toBeNull();
    input(' '); expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
    textarea().dispatchEvent(new Event('blur')); fixture.detectChanges(); expect(textarea().getAttribute('aria-invalid')).toBe('true');
    input('int main() {}'); expect(textarea().getAttribute('aria-invalid')).toBeNull(); expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
  });
  it('allows an invalid submit to request validation and focuses a linked error summary', async () => {
    const button = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(false); await submit();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('.form-errors'));
    const link = fixture.nativeElement.querySelector('.form-errors a') as HTMLAnchorElement; link.click(); expect(document.activeElement).toBe(textarea());
    http.expectNone(request => request.method === 'POST');
    input('int main() {}'); expect(fixture.nativeElement.querySelector('.form-errors')).toBeNull();
  });
  it('defers edited blur errors during pointer submit so the button does not move', () => {
    input(' '); const button = fixture.nativeElement.querySelector('button[type="submit"]');
    button.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true })); textarea().dispatchEvent(new Event('blur')); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
  });
  it('submits untrimmed source once, then uses the server-pinned response and clears the draft', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    input('  int main() {}\n'); await submit(); fixture.componentInstance.submit();
    const post = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    expect(post.request.body).toEqual({ language: 'cpp20', source: '  int main() {}\n', requestKey: expect.any(String) }); expect(textarea().disabled).toBe(true);
    post.flush(submission); await fixture.whenStable(); fixture.detectChanges();
    expect(navigate).toHaveBeenCalledWith(['/submissions', submission.id]); expect(textarea().value).toBe('');
    expect(TestBed.inject(SubmissionDraft).read(submission.problemId)).toBe('');
  });
  it.each([0, 503])('preserves source after an ambiguous %s and offers history without automatically repeating', async status => {
    input('int main() {}'); await submit(); const post = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    if (status === 0) post.error(new ProgressEvent('error')); else post.flush({ detail: 'secret stack' }, { status, statusText: 'Unavailable' });
    await fixture.whenStable(); fixture.detectChanges(); expect(textarea().value).toBe('int main() {}');
    expect(fixture.nativeElement.textContent).toContain('check your history'); expect(fixture.nativeElement.textContent).not.toContain('secret stack');
    expect(fixture.nativeElement.querySelector('.form-errors a')?.getAttribute('href')).toBe('/submissions'); http.expectNone(request => request.method === 'POST');
  });
  it('reuses the key after a lost response and creates a new key after acknowledgement or editing', async () => {
    fixture.componentRef.setInput('inlineResult', true); fixture.detectChanges();
    input('int main() {}'); await submit();
    const first = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    const key = first.request.body.requestKey;
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
    first.error(new ProgressEvent('error')); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').textContent).toContain('Retry submission');
    await submit(); const retry = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    expect(retry.request.body.requestKey).toBe(key);
    retry.flush(completed); await fixture.whenStable(); fixture.detectChanges();
    http.expectOne('/api/submissions/' + submission.id).flush(completed); await fixture.whenStable(); fixture.detectChanges();
    await submit(); const next = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    expect(next.request.body.requestKey).not.toBe(key);
    const nextKey = next.request.body.requestKey;
    next.error(new ProgressEvent('error')); await fixture.whenStable(); fixture.detectChanges();
    input('int main(){return 1;}'); await submit(); const edited = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    expect(edited.request.body.requestKey).not.toBe(nextKey);
    edited.flush({}, { status: 429, statusText: 'Busy' }); await fixture.whenStable(); fixture.detectChanges();
  });
  it('uses Retry-After on overload and permits a deliberate retry with the same source', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    input('int main() {}'); await submit();
    http.expectOne('/api/problems/' + submission.problemId + '/submissions').flush({}, { status: 429, statusText: 'Full', headers: { 'Retry-After': '5' } });
    await fixture.whenStable(); fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('5 seconds'); expect(textarea().disabled).toBe(false);
    await submit(); http.expectOne('/api/problems/' + submission.problemId + '/submissions').flush(submission);
  });
  it('keeps guest composition through sign-in but clears previous account source and ignores old writes', async () => {
    const session = TestBed.inject(SessionService); session.signedOut(); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    input('guest code'); session.signedIn(account); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); expect(textarea().value).toBe('guest code');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await submit(); const post = http.expectOne('/api/problems/' + submission.problemId + '/submissions');
    session.signedIn({ ...account, id: 'other-account' }); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(textarea().value).toBe(''); post.flush(submission); expect(navigate).not.toHaveBeenCalled();
    expect(TestBed.inject(SubmissionDraft).read(submission.problemId)).toBe('');
  });
  it('restores an in-memory account draft after internal navigation and warns on reload', async () => {
    input('int main() {}'); const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true);
    fixture.destroy(); fixture = TestBed.createComponent(SubmissionForm); fixture.componentRef.setInput('problemId', submission.problemId);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); expect(textarea().value).toBe('int main() {}');
  });
  it('keeps the editor in place, selects Result and follows the admitted submission without route navigation', async () => {
    fixture.componentRef.setInput('inlineResult', true); fixture.detectChanges();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    input(submission.source); await submit();
    http.expectOne('/api/problems/' + submission.problemId + '/submissions').flush(submission);
    await fixture.whenStable(); fixture.detectChanges();
    expect(textarea().value).toBe(submission.source); expect(textarea().disabled).toBe(false);
    expect(navigate).not.toHaveBeenCalled(); expect(document.activeElement?.id).toBe('console-tab-result');
    expect(fixture.nativeElement.querySelector('#console-examples').hidden).toBe(true);
    http.expectOne('/api/submissions/' + submission.id).flush(completed);
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#console-result').textContent).toContain('Accepted');
    expect(fixture.nativeElement.querySelector('.console-header a').getAttribute('href')).toBe('/problems/' + submission.problemId + '?tab=submissions&submission=' + submission.id);
    const reload = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(reload); expect(reload.defaultPrevented).toBe(false);
    input(submission.source + '\n// edited'); const editedReload = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(editedReload); expect(editedReload.defaultPrevented).toBe(true);
  });
  it('retains the admitted result on a read outage, supports manual refresh and clears private evidence on identity change', async () => {
    fixture.componentRef.setInput('inlineResult', true); fixture.detectChanges(); input(submission.source); await submit();
    http.expectOne('/api/problems/' + submission.problemId + '/submissions').flush(submission);
    await fixture.whenStable(); fixture.detectChanges();
    http.expectOne('/api/submissions/' + submission.id).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Updates paused'); expect(textarea().value).toBe(submission.source);
    fixture.componentInstance.refreshResult(); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const request = http.expectOne('/api/submissions/' + submission.id);
    TestBed.inject(SessionService).signedIn({ ...account, id: 'other-account' }); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(request.cancelled).toBe(true); expect(fixture.componentInstance.result().data).toBeNull(); expect(textarea().value).toBe('');
    expect(fixture.nativeElement.querySelector('.console-header a')).toBeNull();
  });
  it('removes private result evidence when an owner detail read is denied', async () => {
    fixture.componentRef.setInput('inlineResult', true); fixture.detectChanges(); input(submission.source); await submit();
    http.expectOne('/api/problems/' + submission.problemId + '/submissions').flush(submission);
    await fixture.whenStable(); fixture.detectChanges();
    http.expectOne('/api/submissions/' + submission.id).flush({ detail: 'private trace' }, { status: 403, statusText: 'Denied' });
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.componentInstance.result().data).toBeNull(); expect(fixture.nativeElement.textContent).not.toContain('private trace');
    expect(fixture.nativeElement.querySelector('#console-result').textContent).toContain('Sign in');
  });
  it('supports keyboard console tabs without discarding code or fetching private data', () => {
    fixture.componentRef.setInput('inlineResult', true); fixture.componentRef.setInput('samples', [{ input: '  1 2\n', output: '3\n' }]);
    fixture.detectChanges(); input('int main() {}');
    const examples = fixture.nativeElement.querySelector('#console-tab-examples') as HTMLButtonElement;
    examples.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })); fixture.detectChanges();
    const result = fixture.nativeElement.querySelector('#console-tab-result') as HTMLButtonElement;
    expect(document.activeElement).toBe(result); expect(result.tabIndex).toBe(0); expect(examples.tabIndex).toBe(-1);
    result.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true })); fixture.detectChanges();
    expect(document.activeElement).toBe(examples); expect(fixture.nativeElement.querySelector('#console-examples pre').textContent).toBe('  1 2\n');
    expect(textarea().value).toBe('int main() {}'); http.expectNone(request => request.url.startsWith('/api/submissions'));
  });
  it('updates cursor position and renders a bounded gutter when a long source scrolls', async () => {
    input(Array.from({ length: 1000 }, (_, index) => '// line ' + index).join('\n')); await fixture.whenStable(); fixture.detectChanges();
    Object.defineProperty(textarea(), 'clientHeight', { value: 240 }); textarea().scrollTop = 240;
    textarea().dispatchEvent(new Event('scroll')); fixture.detectChanges();
    const numbers = fixture.nativeElement.querySelector('.line-gutter pre').textContent.split('\n');
    expect(numbers[0]).toBe('11'); expect(numbers.length).toBeLessThanOrEqual(12);
    textarea().setSelectionRange(10, 10); textarea().dispatchEvent(new Event('select')); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.editor-status').textContent).toContain('Ln 2, Col 1');
  });
  it('switches public cases with keyboard navigation, preserves exact whitespace and resets on a new problem', async () => {
    fixture.componentRef.setInput('inlineResult', true);
    fixture.componentRef.setInput('samples', [{ input: '  1 2\n', output: '3\n' }, { input: '4 5\n', output: '9\n' }]);
    fixture.detectChanges(); input('int main() {}');
    const first = fixture.nativeElement.querySelector('#example-tab-0') as HTMLButtonElement;
    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true })); fixture.detectChanges();
    expect(document.activeElement?.id).toBe('example-tab-1');
    expect(fixture.nativeElement.querySelector('#example-panel-0').hidden).toBe(true);
    expect(fixture.nativeElement.querySelector('#example-panel-1 pre').textContent).toBe('4 5\n');
    expect(textarea().value).toBe('int main() {}');
    fixture.componentRef.setInput('problemId', '11111111-1111-4111-8111-111111111111');
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#example-tab-0').getAttribute('aria-selected')).toBe('true');
    expect(fixture.nativeElement.querySelector('#example-panel-0 pre').textContent).toBe('  1 2\n');
    http.expectNone(request => request.url.startsWith('/api/submissions'));
  });
  it('summarizes real inline results without rendering test rows or resource measurements', async () => {
    fixture.componentRef.setInput('inlineResult', true); fixture.detectChanges(); input(submission.source); await submit();
    http.expectOne('/api/problems/' + submission.problemId + '/submissions').flush(submission);
    await fixture.whenStable(); fixture.detectChanges();
    http.expectOne('/api/submissions/' + submission.id).flush(completed);
    await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.test-summary').textContent).toContain('2 / 2 tests passed');
    expect(fixture.nativeElement.querySelector('.test-evidence, .metrics, #console-result table')).toBeNull();
    expect(fixture.nativeElement.querySelector('#console-result').textContent).not.toMatch(/CPU|wall time|memory|MiB|Limits:|\d+ ms/i);
  });
});

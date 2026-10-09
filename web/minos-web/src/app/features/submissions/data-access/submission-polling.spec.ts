import { Observable, of, Subject, throwError } from 'rxjs';
import { pollSubmission } from './submission-polling';

describe('submission polling', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());
  it('reads serially, pauses in hidden tabs, resumes and stops at a terminal result', () => {
    const doc = { visibilityState: 'visible' }, pending = new Subject<{ active: boolean }>();
    const read = vi.fn<() => Observable<{ active: boolean }>>().mockReturnValueOnce(of({ active: true })).mockReturnValueOnce(pending).mockReturnValue(of({ active: false }));
    const result = vi.fn(), subscription = pollSubmission(read, value => value.active, doc as Document).subscribe(result);
    doc.visibilityState = 'hidden'; vi.advanceTimersByTime(10000); expect(read).toHaveBeenCalledTimes(1);
    doc.visibilityState = 'visible'; vi.advanceTimersByTime(2500); expect(read).toHaveBeenCalledTimes(2);
    vi.advanceTimersByTime(10000); expect(read).toHaveBeenCalledTimes(2);
    pending.next({ active: true }); pending.complete(); vi.advanceTimersByTime(2500); expect(read).toHaveBeenCalledTimes(3);
    vi.advanceTimersByTime(10000); expect(read).toHaveBeenCalledTimes(3); expect(subscription.closed).toBe(true);
  });
  it('stops on a failed poll and leaves manual recovery to the view', () => {
    const read = vi.fn().mockReturnValueOnce(of({ active: true })).mockReturnValue(throwError(() => new Error('offline')));
    const results: unknown[] = []; pollSubmission(read, value => (value as { active: boolean }).active, { visibilityState: 'visible' } as Document).subscribe(v => results.push(v));
    vi.advanceTimersByTime(2500); vi.advanceTimersByTime(10000);
    expect(read).toHaveBeenCalledTimes(2); expect(results).toHaveLength(2); expect(results[1]).toMatchObject({ data: null, error: expect.any(Error) });
  });
  it('cancels reads and timers on navigation or identity changes', () => {
    const teardown = vi.fn(), read = vi.fn(() => new Observable<{ active: boolean }>(() => teardown));
    const subscription = pollSubmission(read, value => value.active, document).subscribe(); subscription.unsubscribe();
    expect(teardown).toHaveBeenCalledTimes(1); vi.advanceTimersByTime(10000); expect(read).toHaveBeenCalledTimes(1);
    const completedRead = vi.fn(() => of({ active: true })); const timer = pollSubmission(completedRead, value => value.active, document).subscribe(); timer.unsubscribe();
    vi.advanceTimersByTime(10000); expect(completedRead).toHaveBeenCalledTimes(1);
  });
});

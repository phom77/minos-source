import { ApplicationRef, ChangeDetectionStrategy, Component } from '@angular/core';
import { ViewportScroller } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthPrompt } from './auth-prompt.service';

@Component({ template: '', changeDetection: ChangeDetectionStrategy.OnPush }) class Background {}
describe('authentication overlay navigation', () => {
  let router: Router; let prompt: AuthPrompt;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([
      { path: 'problems', component: Background }, { path: 'account', component: Background },
    ])] });
    router = TestBed.inject(Router); prompt = TestBed.inject(AuthPrompt);
  });
  it('opens and closes over the same catalog URL without losing pagination or fragment', async () => {
    await router.navigateByUrl('/problems?page=2&pageSize=10#problem-library');
    prompt.open(); await TestBed.inject(ApplicationRef).whenStable();
    expect(router.url).toBe('/problems?page=2&pageSize=10&auth=login#problem-library');
    expect(prompt.mode()).toBe('login');
    expect(document.documentElement.classList.contains('auth-open')).toBe(true);
    prompt.open('register'); await TestBed.inject(ApplicationRef).whenStable(); expect(prompt.mode()).toBe('register');
    prompt.close(); await TestBed.inject(ApplicationRef).whenStable();
    expect(router.url).toBe('/problems?page=2&pageSize=10#problem-library'); expect(prompt.mode()).toBeNull();
    expect(document.documentElement.classList.contains('auth-open')).toBe(false);
  });
  it('completes on the current page when no protected destination is supplied', async () => {
    await router.navigateByUrl('/problems?page=3&auth=login#problem-library');
    prompt.complete(); await TestBed.inject(ApplicationRef).whenStable();
    expect(router.url).toBe('/problems?page=3#problem-library');
  });
  it('uses a public background for protected routes and rejects external/nested-modal returns', async () => {
    expect(router.serializeUrl(prompt.entryTree('login', '/account', 'expired'))).toBe('/problems?auth=login&authReturn=%2Faccount&authNotice=expired');
    await router.navigateByUrl('/problems?auth=login&authReturn=' + encodeURIComponent('//other.test'));
    prompt.complete(); await TestBed.inject(ApplicationRef).whenStable(); expect(router.url).toBe('/account');
    await router.navigateByUrl('/problems?auth=login&authReturn=' + encodeURIComponent('/problems?page=2&auth=login&authReturn=/account#problem-library'));
    prompt.complete(); await TestBed.inject(ApplicationRef).whenStable(); expect(router.url).toBe('/problems?page=2#problem-library');
  });
  it('retains browsing context when the account guard requests authentication', async () => {
    await router.navigateByUrl('/problems?page=2&pageSize=50');
    const scroll = vi.spyOn(TestBed.inject(ViewportScroller), 'getScrollPosition').mockReturnValue([0, 216]);
    const tree = prompt.entryTree('login', '/account');
    expect(router.serializeUrl(tree)).toBe('/problems?page=2&pageSize=50&auth=login&authReturn=%2Faccount');
    expect(scroll).toHaveBeenCalledOnce();
  });
});

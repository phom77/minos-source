import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIGURATION } from '../config/api.config';
import { MINOS_API_REQUEST, apiUrlInterceptor } from './api-url.interceptor';

describe('apiUrlInterceptor', () => {
  let http: HttpClient;
  let requests: HttpTestingController;

  function configure(apiBaseUrl: string): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiUrlInterceptor])),
        provideHttpClientTesting(),
        { provide: API_CONFIGURATION, useValue: { production: false, apiBaseUrl } },
      ],
    });
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
  }

  afterEach(() => requests.verify());

  it('prefixes an explicitly marked API request and preserves its query and headers', () => {
    configure('https://api.example.test///');
    http.get('/accounts/me', {
      context: new HttpContext().set(MINOS_API_REQUEST, true),
      params: { view: 'profile' },
      headers: { 'X-Request-Test': 'retained' },
    }).subscribe();

    const request = requests.expectOne('https://api.example.test/accounts/me?view=profile');
    expect(request.request.headers.get('X-Request-Test')).toBe('retained');
    request.flush({});
  });

  it('leaves root-relative assets and unmarked requests on the application origin', () => {
    configure('https://api.example.test');
    http.get('/assets/messages.json').subscribe();
    requests.expectOne('/assets/messages.json').flush({});
    http.get('/accounts/me').subscribe();
    requests.expectOne('/accounts/me').flush({});
  });

  it.each(['https://other.example.test/data', '//cdn.example.test/data', 'assets/messages.json'])(
    'does not rewrite a non-root-relative URL even when marked: %s',
    (url) => {
      configure('https://api.example.test');
      http.get(url, { context: new HttpContext().set(MINOS_API_REQUEST, true) }).subscribe();
      requests.expectOne(url).flush({});
    },
  );

  it('preserves same-origin API requests when no base URL is configured', () => {
    configure('');
    http.get('/accounts/me', { context: new HttpContext().set(MINOS_API_REQUEST, true) }).subscribe();
    requests.expectOne('/accounts/me').flush({});
  });
});

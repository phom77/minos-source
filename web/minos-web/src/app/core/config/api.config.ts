import { InjectionToken } from '@angular/core';

export interface ApiConfiguration {
  readonly production: boolean;
  readonly apiBaseUrl: string;
}

export const API_CONFIGURATION = new InjectionToken<ApiConfiguration>('Minos API configuration');

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { StatePanel } from './state-panel';
import { StatusBadge } from './status-badge';

@Component({
  imports: [StatePanel, StatusBadge],
  template: `<minos-state-panel heading="No problems yet" description="Check back later.">
    <button type="button">Retry</button>
  </minos-state-panel><minos-status-badge tone="warning">Not connected yet</minos-status-badge>`,
})
class StatePanelHost {}

describe('shared feedback', () => {
  it('renders descriptive feedback and projected native actions without hiding their semantics', () => {
    const fixture = TestBed.createComponent(StatePanelHost);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[role="status"]')?.textContent).toContain('No problems yet');
    expect(element.querySelector('[role="status"]')?.textContent).toContain('Check back later.');
    expect(element.querySelector('button')?.textContent).toBe('Retry');
    expect(element.querySelector('minos-status-badge')?.getAttribute('data-tone')).toBe('warning');
    expect(element.querySelector('minos-status-badge')?.textContent).toBe('Not connected yet');
  });

  it('announces an error and replaces it with labeled loading feedback', () => {
    const fixture = TestBed.createComponent(StatePanel);
    fixture.componentRef.setInput('heading', 'Could not load problems');
    fixture.componentRef.setInput('description', 'Try again.');
    fixture.componentRef.setInput('kind', 'error');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain('Try again.');

    fixture.componentRef.setInput('kind', 'loading');
    fixture.componentRef.setInput('heading', 'Loading problems');
    fixture.componentRef.setInput('description', 'Please wait.');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain('Loading problems');
    expect(fixture.nativeElement.querySelector('.skeleton')?.getAttribute('aria-hidden')).toBe('true');
  });
});

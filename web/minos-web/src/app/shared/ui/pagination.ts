import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'minos-pagination', imports: [RouterLink], changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `:host { display: block; } nav { display: flex; align-items: center; justify-content: space-between;
    flex-wrap: wrap; gap: var(--space-3); padding: var(--space-4) var(--space-5); border-top: 1px solid var(--color-border); }
    span { color: var(--color-text-muted); font-size: var(--text-label); }`,
  template: `<nav aria-label="Pagination">
    @if (page() > 1) { <a class="button button--secondary" [routerLink]="[]" [queryParams]="{ page: page() - 1, pageSize: pageSize() }" queryParamsHandling="merge" aria-label="Previous page">Previous</a> }
    @else { <button class="button button--secondary" disabled type="button">Previous</button> }
    <span>Page {{ page() }} of {{ pages() }}</span>
    @if (page() < pages()) { <a class="button button--secondary" [routerLink]="[]" [queryParams]="{ page: page() + 1, pageSize: pageSize() }" queryParamsHandling="merge" aria-label="Next page">Next</a> }
    @else { <button class="button button--secondary" disabled type="button">Next</button> }
  </nav>`,
})
export class Pagination {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
}

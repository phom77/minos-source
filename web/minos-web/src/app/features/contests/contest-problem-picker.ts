import { ChangeDetectionStrategy, Component, input, output, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, map, of, startWith, switchMap } from 'rxjs';
import { ProblemPage, ProblemsApi, PublishedProblemSummary } from '../problems/data-access/problems-api';
export interface SelectedProblem { versionId: string; problemId: string; title: string; }
@Component({ selector: 'minos-contest-problem-picker', changeDetection: ChangeDetectionStrategy.OnPush, styleUrl: './contests.css',
  template: `<fieldset><legend>Select published problems</legend><div class="stack">
    <p class="muted">Selected versions stay fixed for this contest. These problems are also public in the library.</p>
    @if (selected().length) { <ol class="selected-problems">@for (problem of selected(); track problem.problemId; let index = $index) {
      <li><div class="selected-row"><span>{{ problem.title }}</span><button class="button button--secondary" type="button" [disabled]="disabled() || index === 0" (click)="move(index, -1)" [attr.aria-label]="'Move ' + problem.title + ' up'">↑</button><button class="button button--secondary" type="button" [disabled]="disabled() || index === selected().length - 1" (click)="move(index, 1)" [attr.aria-label]="'Move ' + problem.title + ' down'">↓</button><button class="button button--secondary" type="button" [disabled]="disabled()" (click)="remove(problem.problemId)" [attr.aria-label]="'Remove ' + problem.title">Remove</button></div></li>
    }</ol> }
    @if (state().data; as data) { <div class="problem-picker">@for (problem of data.items; track problem.problemId) {
      <label class="choice"><input type="checkbox" [checked]="has(problem.problemId)" [disabled]="disabled() || (!has(problem.problemId) && selected().length >= 26)" (change)="toggle(problem)" /><span>{{ problem.title }}</span></label>
    } @empty { <p>No published problems available.</p> }</div>
    <div class="actions"><button class="button button--secondary" type="button" [disabled]="page() <= 1" (click)="page.set(page() - 1)">Previous</button><span>Page {{ page() }}</span><button class="button button--secondary" type="button" [disabled]="page() * 20 >= data.total" (click)="page.set(page() + 1)">Next</button></div>
    } @else if (state().error) { <p role="alert">Problems could not be loaded.</p><button class="button button--secondary" type="button" (click)="refresh()">Try again</button> }
    @else { <p role="status">Loading problems…</p> }
  </div></fieldset>` })
export class ContestProblemPicker {
  readonly selected = input.required<readonly SelectedProblem[]>(); readonly disabled = input(false); readonly changed = output<readonly SelectedProblem[]>();
  readonly page = signal(1); private readonly api = inject(ProblemsApi); private readonly generation = signal(0);
  readonly state = toSignal(combineLatest([toObservable(this.page), toObservable(this.generation)]).pipe(switchMap(([page]) => this.api.list(page, 20).pipe(
    map(data => ({ data, error: false })), catchError(() => of({ data: null, error: true })), startWith({ data: null, error: false })))),
    { initialValue: { data: null, error: false } as { data: ProblemPage | null; error: boolean } });
  has(id: string): boolean { return this.selected().some(p => p.problemId === id); }
  toggle(p: PublishedProblemSummary): void { if (this.has(p.problemId)) this.remove(p.problemId); else if (this.selected().length < 26) this.changed.emit([...this.selected(), p]); }
  remove(id: string): void { this.changed.emit(this.selected().filter(p => p.problemId !== id)); }
  move(index: number, delta: number): void { const items = [...this.selected()]; [items[index], items[index + delta]] = [items[index + delta], items[index]]; this.changed.emit(items); }
  refresh(): void { this.generation.update(n => n + 1); }
}

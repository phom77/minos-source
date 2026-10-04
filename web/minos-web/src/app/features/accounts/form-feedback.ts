import { signal } from '@angular/core';

/** Interaction state is independent of validity and Angular's touched flag. */
export class FormFeedback<Field extends string> {
  private readonly edited = new Set<Field>();
  private readonly revealed = signal<ReadonlySet<Field>>(new Set());
  private submittingByPointer = false;

  prepareSubmit(event: PointerEvent): void {
    this.submittingByPointer = !!(event.target as Element).closest('button[type="submit"]');
  }

  edit(field: Field): void { this.edited.add(field); }

  blur(field: Field, invalid: boolean): void {
    // A new inline error can move the button between pointer-down and click.
    // Let that click submit the whole form; keyboard blur still shows feedback.
    if (this.submittingByPointer) {
      this.submittingByPointer = false; this.edited.delete(field); return;
    }
    if (this.edited.has(field) && invalid) {
      this.revealed.update((fields) => new Set([...fields, field]));
    }
    this.edited.delete(field);
  }

  visible(field: Field, submitted: boolean): boolean {
    return submitted || this.revealed().has(field);
  }

  reset(): void { this.edited.clear(); this.revealed.set(new Set()); this.submittingByPointer = false; }
}

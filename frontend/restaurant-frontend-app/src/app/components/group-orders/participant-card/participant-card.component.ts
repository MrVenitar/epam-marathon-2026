import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { Participant } from '@app/models/group-orders/participant.model';

/** One participant of a group pre-order: their dishes, subtotal and cart status. */
@Component({
  selector: 'app-participant-card',
  imports: [CurrencyPipe],
  templateUrl: './participant-card.component.html',
  styleUrl: './participant-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class ParticipantCardComponent {
  readonly participant = input.required<Participant>();
  /** The owner's own session (shown as "My Pre-Order"). */
  readonly isMe = input(false);
  /** Show the "Remove Guest" action. */
  readonly canRemove = input(false);

  readonly removeClick = output<Participant>();

  readonly title = computed(() => {
    const person = this.participant();
    if (this.isMe()) {
      return 'My Pre-Order';
    }
    return person.type === 'Owner' ? `Owner: ${person.name}` : `Guest: ${person.name}`;
  });

  /** Guests' cart status in words — not only by colour. */
  readonly statusLabel = computed(() => {
    const person = this.participant();
    if (person.type !== 'Guest') {
      return '';
    }
    return person.isSubmitted ? 'Submitted' : 'Still choosing';
  });

  readonly lines = computed(() =>
    this.participant().items.map((item) => ({
      id: item.dish.id,
      name: item.dish.name,
      imageUrl: item.dish.imageUrl ?? null,
      initial: item.dish.name.charAt(0).toUpperCase(),
      quantity: item.quantity,
      lineTotal: item.dish.price * item.quantity,
    })),
  );

  onRemove(): void {
    this.removeClick.emit(this.participant());
  }
}

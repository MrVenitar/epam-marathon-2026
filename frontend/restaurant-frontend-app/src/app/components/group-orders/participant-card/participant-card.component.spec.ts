import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Participant } from '@app/models/group-orders/participant.model';
import { ParticipantCardComponent } from './participant-card.component';

const GUEST: Participant = {
  id: 'session-2',
  type: 'Guest',
  name: 'Olena',
  isSubmitted: false,
  total: 220,
  items: [{ dish: { id: 'dish2', name: 'Tom Yum', price: 220 }, quantity: 1 }],
};

describe('ParticipantCardComponent', () => {
  let fixture: ComponentFixture<ParticipantCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ParticipantCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(ParticipantCardComponent);
    fixture.componentRef.setInput('participant', GUEST);
  });

  it('shows a guest with their dishes and cart status in words', () => {
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;

    expect(text).toContain('Guest: Olena');
    expect(text).toContain('Tom Yum');
    expect(text).toContain('Still choosing');
  });

  it("labels the owner's own card as My Pre-Order", () => {
    fixture.componentRef.setInput('isMe', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('My Pre-Order');
  });

  it('hides Remove Guest unless it is allowed', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button[aria-label="Remove Olena"]')).toBeNull();
  });

  it('emits the participant when Remove Guest is clicked', () => {
    let emitted: Participant | undefined;
    fixture.componentInstance.removeClick.subscribe((person) => (emitted = person));
    fixture.componentRef.setInput('canRemove', true);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button[aria-label="Remove Olena"]') as HTMLButtonElement).click();

    expect(emitted).toEqual(GUEST);
  });
});

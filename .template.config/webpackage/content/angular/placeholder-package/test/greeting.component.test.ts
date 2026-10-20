import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { GreetingComponent } from '../src/index.ts';

describe('GreetingComponent', () => {
  it('names who it greets', () => {
    const fixture = TestBed.createComponent(GreetingComponent);
    fixture.componentRef.setInput('name', 'Ada');
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toBe('Hello, Ada.');
  });
});

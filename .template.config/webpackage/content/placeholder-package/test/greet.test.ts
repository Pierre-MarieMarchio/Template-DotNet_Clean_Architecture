import { describe, expect, it } from 'vitest';
import { greet } from '../src/index.ts';

describe('greet', () => {
  it('names who it greets', () => {
    expect(greet('Ada')).toBe('Hello, Ada.');
  });
});

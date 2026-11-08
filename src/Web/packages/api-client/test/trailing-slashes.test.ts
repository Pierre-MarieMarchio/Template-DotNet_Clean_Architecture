import { describe, expect, it } from 'vitest';
import { withoutTrailingSlashes } from '../src/session.ts';

describe('withoutTrailingSlashes', () => {
  it.each([
    ['', ''],
    ['/', ''],
    ['///', ''],
    ['a//', 'a'],
    ['a/b/', 'a/b'],
    ['a/b', 'a/b'],
    ['/a', '/a'],
    ['http://site.test/', 'http://site.test'],
  ])('turns %j into %j', (value, expected) => {
    expect(withoutTrailingSlashes(value)).toBe(expected);
  });

  it('answers in linear time for a long run of slashes that ends in another character', () => {
    const value = '/'.repeat(200_000) + 'x';
    const started = performance.now();

    expect(withoutTrailingSlashes(value)).toBe(value);
    expect(performance.now() - started).toBeLessThan(250);
  });
});

import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { Greeting } from '../src/index.ts';

describe('Greeting', () => {
  it('names who it greets', () => {
    expect(renderToStaticMarkup(<Greeting name="Ada" />)).toBe('<p>Hello, Ada.</p>');
  });
});

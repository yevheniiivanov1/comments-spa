import { isHttpUrl, validateMarkup } from './markup';

describe('validateMarkup', () => {
  it.each([
    'Hello',
    '<i>it</i> and <strong>bold</strong>',
    '<code>if (a < b) return;</code>',
    '<strong><i>both</i></strong>',
    'see <a href="https://example.com/?a=1&b=2" title="Docs">docs</a>',
    "<a title='t' href='http://x.io'>x</a>",
    '&lt;script&gt; is text',
  ])('accepts %s', (input) => {
    expect(validateMarkup(input)).toEqual([]);
  });

  it.each([
    ['<script>alert(1)</script>', 'Tag <script> is not allowed'],
    ['<img src=x onerror=alert(1)>', 'is malformed'],
    ['<i>unclosed', 'Tag <i> is not closed'],
    ['closing only</i>', 'has no matching opening tag'],
    ['<i><strong>x</i></strong>', 'not nested correctly'],
    ['<STRONG>x</STRONG>', 'must be lowercase'],
    ['<i class="x">x</i>', 'cannot have attributes'],
    ['<a href="javascript:alert(1)">x</a>', 'must be a full http:// or https:// URL'],
    ['<a>x</a>', 'needs an href'],
    ['<a href="https://a.com" onclick="x()">x</a>', 'Attribute "onclick" is not allowed'],
    ['<a href="https://a.com"><a href="https://b.com">x</a></a>', 'cannot be nested'],
    ['<i/>', 'must have content'],
    ['<!-- hidden -->', 'HTML comments are not allowed'],
    ['<i', "is not closed with '>'"],
  ])('rejects %s', (input, expected) => {
    const errors = validateMarkup(input);
    expect(errors.some((e) => e.includes(expected))).toBe(true);
  });

  it('requires visible text', () => {
    expect(validateMarkup('<i>  </i>')).toEqual(['Text is required.']);
    expect(validateMarkup('')).toEqual(['Text is required.']);
  });

  it('reports all errors at once', () => {
    expect(validateMarkup('<div>x</div> <b>y</b> <i>z')).toHaveLength(3);
  });
});

describe('isHttpUrl', () => {
  it('accepts http and https only', () => {
    expect(isHttpUrl('https://example.com')).toBe(true);
    expect(isHttpUrl('http://example.com/a?b=1')).toBe(true);
    expect(isHttpUrl('javascript:alert(1)')).toBe(false);
    expect(isHttpUrl('example.com')).toBe(false);
    expect(isHttpUrl('ftp://example.com')).toBe(false);
  });
});

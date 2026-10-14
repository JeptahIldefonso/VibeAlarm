import test from 'node:test';
import assert from 'node:assert/strict';
import {
  htmlToMarkdown,
  markdownToHtml,
  sanitizePastedHtml,
  normalizeMultilineMarkdown,
  renderInlineMarkdown,
} from './noteFormat.ts';

// Setup minimal DOMParser for Node test environment
if (typeof globalThis.DOMParser === 'undefined') {
  class MockNode {
    static readonly ELEMENT_NODE = 1;
    static readonly TEXT_NODE = 3;
    nodeType: number;
    nodeValue: string | null;
    tagName: string;
    childNodes: MockNode[] = [];
    style: { fontWeight: string; fontStyle: string } = { fontWeight: '', fontStyle: '' };

    constructor(type: number, tagName = '', value: string | null = null) {
      this.nodeType = type;
      this.tagName = tagName.toUpperCase();
      this.nodeValue = value;
    }
  }

  (globalThis as unknown as { Node: unknown }).Node = MockNode;

  class MockDOMParser {
    parseFromString(html: string, _type: string) {
      const root = new MockNode(MockNode.ELEMENT_NODE, 'BODY');
      const tokenRe = /<(\/)?([a-zA-Z0-9]+)([^>]*)>|([^<]+)/g;
      const stack: MockNode[] = [root];
      let match: RegExpExecArray | null;

      while ((match = tokenRe.exec(html)) !== null) {
        const [_, isClosing, tagName, attrs, text] = match;
        if (text) {
          const textNode = new MockNode(MockNode.TEXT_NODE, '', text);
          stack[stack.length - 1].childNodes.push(textNode);
        } else if (isClosing) {
          if (stack.length > 1 && stack[stack.length - 1].tagName === tagName.toUpperCase()) {
            stack.pop();
          }
        } else {
          const el = new MockNode(MockNode.ELEMENT_NODE, tagName);
          if (attrs) {
            const styleMatch = /style="([^"]*)"/i.exec(attrs);
            if (styleMatch) {
              const styles = styleMatch[1].split(';');
              for (const s of styles) {
                const [k, v] = s.split(':').map((x) => x.trim().toLowerCase());
                if (k === 'font-weight') el.style.fontWeight = v;
                if (k === 'font-style') el.style.fontStyle = v;
              }
            }
          }
          stack[stack.length - 1].childNodes.push(el);
          if (!['BR', 'HR', 'IMG', 'INPUT', 'META'].includes(tagName.toUpperCase())) {
            stack.push(el);
          }
        }
      }

      return { body: root };
    }
  }

  (globalThis as unknown as { DOMParser: unknown }).DOMParser = MockDOMParser;
}

test('htmlToMarkdown preserves bold, italic, and bullets while dropping styles', () => {
  const html = `
    <p>Normal <b>bold</b> and <i>italic</i></p>
    <ul>
      <li>First bullet</li>
      <li>Second bullet with <strong>bold</strong></li>
    </ul>
  `;
  const md = htmlToMarkdown(html);
  assert.ok(md.includes('Normal **bold** and *italic*'));
  assert.ok(md.includes('- First bullet'));
  assert.ok(md.includes('- Second bullet with **bold**'));
});

test('htmlToMarkdown handles Google Docs wrapping without turning everything bold', () => {
  const docsHtml = `
    <b style="font-weight:normal;">
      <p>Regular text</p>
      <p><span style="font-weight:bold;">Real bold</span></p>
    </b>
  `;
  const md = htmlToMarkdown(docsHtml);
  assert.ok(md.includes('Regular text'));
  assert.ok(md.includes('**Real bold**'));
  assert.ok(!md.startsWith('**Regular text'));
});

test('htmlToMarkdown treats BR as tight line break', () => {
  const html = '<div>Line 1<br>Line 2</div>';
  const md = htmlToMarkdown(html);
  assert.equal(md, 'Line 1\nLine 2');
});

test('markdownToHtml converts markdown to visual HTML (bold, italic, bullets, breaks)', () => {
  const md = 'Hello **world**\n*italic text*\n- First bullet\n- Second bullet\n\nFinal paragraph';
  const html = markdownToHtml(md);
  assert.ok(html.includes('<div>Hello <strong>world</strong></div>'));
  assert.ok(html.includes('<div><em>italic text</em></div>'));
  assert.ok(html.includes('<ul><li>First bullet</li><li>Second bullet</li></ul>'));
  assert.ok(html.includes('<div><br></div>'));
  assert.ok(html.includes('<div>Final paragraph</div>'));
});

test('markdownToHtml and htmlToMarkdown roundtrip preserves structure and formatting', () => {
  const original = 'Note title\n\n- **Important** item\n- Regular item\n\nEnding with *emphasis*.';
  const html = markdownToHtml(original);
  const backToMd = htmlToMarkdown(html);
  assert.equal(backToMd, original);
});

test('sanitizePastedHtml strips colors, fonts and external junk while keeping formatting', () => {
  const messyExternalHtml = `
    <div style="font-family: Arial; font-size: 24px; color: rgb(255, 0, 0); background: yellow;">
      <p style="margin: 0;">This is <span style="font-weight: bold; color: blue;">bold</span> and <span style="font-style: italic;">italic</span></p>
      <ul>
        <li><span style="font-size: 10px;">Item 1</span></li>
        <li>Item 2</li>
      </ul>
    </div>
  `;
  const sanitized = sanitizePastedHtml(messyExternalHtml);
  assert.ok(!sanitized.includes('rgb(255, 0, 0)'));
  assert.ok(!sanitized.includes('Arial'));
  assert.ok(!sanitized.includes('yellow'));
  assert.ok(sanitized.includes('<strong>bold</strong>'));
  assert.ok(sanitized.includes('<em>italic</em>'));
  assert.ok(sanitized.includes('<ul><li>Item 1</li><li>Item 2</li></ul>'));
});

test('normalizeMultilineMarkdown cleans up multi-line bold+italic across bullet points', () => {
  const input = `- ***we are told to study stacks\n- along with if el\n- se / for loop***`;
  const normalized = normalizeMultilineMarkdown(input);
  assert.ok(normalized.includes('- ***we are told to study stacks***'));
  assert.ok(normalized.includes('- ***along with if el***'));
  assert.ok(normalized.includes('- ***se / for loop***'));

  const html = markdownToHtml(input);
  assert.ok(html.includes('<li><strong><em>we are told to study stacks</em></strong></li>'));
  assert.ok(html.includes('<li><strong><em>along with if el</em></strong></li>'));
  assert.ok(html.includes('<li><strong><em>se / for loop</em></strong></li>'));
  assert.ok(!html.includes('***'));
});

test('renderInlineMarkdown cleans up stray asterisks', () => {
  const res = renderInlineMarkdown('***dangling start and end***');
  assert.equal(res, '<strong><em>dangling start and end</em></strong>');
});

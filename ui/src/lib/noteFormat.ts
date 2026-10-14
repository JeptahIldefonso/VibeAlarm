/**
 * High-performance HTML <-> Markdown conversion primitives for the Notes editor.
 *
 * Notes are stored on disk and exported as standard plain Markdown text (.txt),
 * while being edited visually (WYSIWYG) in the browser via contentEditable.
 *
 * This module converts:
 *  - Editor DOM / Clipboard HTML -> clean Markdown
 *  - Plain Markdown -> sanitized visual HTML
 *  - Paste data -> stripped, styled HTML preserving bold/italic/bullets
 */

/** One stretch of text sharing the same emphasis. */
interface Run {
  text: string;
  bold: boolean;
  italic: boolean;
}

/** One output line: its runs, whether it is a list item, indent level, and break flags. */
interface Block {
  runs: Run[];
  bullet: boolean;
  indent: number;
  isParagraph: boolean;
  explicitBreak?: boolean;
}

const BULLET = '- ';

/** Tags that represent distinct semantic paragraphs (separated by blank lines in text). */
const PARAGRAPH_TAGS = new Set([
  'ARTICLE', 'BLOCKQUOTE', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'P', 'SECTION',
]);

/** Tags that end the current line. */
const BLOCK_TAGS = new Set([
  'ADDRESS', 'ARTICLE', 'ASIDE', 'BLOCKQUOTE', 'DD', 'DIV', 'DL', 'DT', 'FIELDSET',
  'FIGCAPTION', 'FIGURE', 'FOOTER', 'FORM', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6',
  'HEADER', 'HR', 'MAIN', 'NAV', 'OL', 'P', 'PRE', 'SECTION', 'TABLE', 'TR', 'UL',
]);

/** Tags whose content is not prose. */
const SKIP_TAGS = new Set(['HEAD', 'LINK', 'META', 'NOSCRIPT', 'SCRIPT', 'STYLE', 'TITLE']);

/** Word and Outlook glyph bullets. */
const GLYPH_BULLET = /^[•·◦▪▫‣⁃]\s+/;

/**
 * Converts clipboard HTML or editor DOM into this editor's plain-text formatting, keeping only
 * what the editor expresses: bold, italic, and bullet lists (plus clean line structure).
 * Fonts, colors, sizes and foreign layouts are dropped.
 */
export function htmlToMarkdown(html: string): string {
  if (!html) return '';
  const doc = new DOMParser().parseFromString(html, 'text/html');
  const blocks: Block[] = [];
  let current: Block | null = null;

  const open = (bullet: boolean, indent: number, isParagraph = true): Block => {
    const block: Block = { runs: [], bullet, indent, isParagraph };
    blocks.push(block);
    current = block;
    return block;
  };

  const line = (bullet: boolean, indent: number, isParagraph = true): void => {
    const block = current;
    if (block != null && block.runs.length === 0 && !block.explicitBreak) {
      if (bullet) {
        block.bullet = true;
        block.indent = indent;
      }
      block.isParagraph = block.isParagraph || isParagraph;
      return;
    }
    open(bullet, indent, isParagraph);
  };

  const add = (value: string, bold: boolean, italic: boolean): void => {
    if (value === '') return;
    const block = current ?? open(false, 0, false);
    if (block.runs.length === 0 && value.trim() === '') return;
    block.runs.push({ text: value, bold, italic });
  };

  const walk = (node: Node, bold: boolean, italic: boolean, depth: number, pre: boolean): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      const value = node.nodeValue ?? '';
      if (!pre) {
        add(value.replace(/\s+/g, ' '), bold, italic);
        return;
      }
      value.split('\n').forEach((part, index) => {
        if (index > 0) line(false, 0, false);
        add(part, bold, italic);
      });
      return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return;

    const el = node as HTMLElement;
    const tag = el.tagName;
    if (SKIP_TAGS.has(tag)) return;

    if (tag === 'BR') {
      if (current != null && current.runs.length === 0) {
        current.explicitBreak = true;
      } else {
        const next = open(false, current?.indent ?? 0, false);
        next.explicitBreak = true;
      }
      return;
    }

    const isList = tag === 'UL' || tag === 'OL';
    const childDepth = isList ? depth + 1 : depth;

    if (tag === 'LI') {
      line(true, Math.max(0, depth - 1), false);
    } else if (BLOCK_TAGS.has(tag)) {
      line(false, 0, PARAGRAPH_TAGS.has(tag));
    }

    const childBold = boldOf(el) ?? bold;
    const childItalic = italicOf(el) ?? italic;
    const childPre = pre || tag === 'PRE';
    for (const child of Array.from(el.childNodes)) {
      walk(child, childBold, childItalic, childDepth, childPre);
    }

    if (tag === 'LI') {
      line(false, Math.max(0, depth - 1), false);
    } else if (BLOCK_TAGS.has(tag)) {
      line(false, 0, PARAGRAPH_TAGS.has(tag));
    }
  };

  walk(doc.body, false, false, 0, false);

  const out: string[] = [];
  let previousWasBullet = false;
  for (const block of blocks) {
    let body = renderRuns(block.runs).trim();
    if (body === '') {
      if (block.explicitBreak) {
        out.push('');
      }
      continue;
    }

    let bullet = block.bullet;
    const glyph = GLYPH_BULLET.exec(body);
    if (glyph != null) {
      bullet = true;
      body = body.slice(glyph[0].length);
    }

    if (out.length > 0 && !bullet && !previousWasBullet && block.isParagraph) out.push('');
    out.push('  '.repeat(block.indent) + (bullet ? BULLET : '') + body);
    previousWasBullet = bullet;
  }
  return out.join('\n');
}

/** Merges consecutive runs sharing the same styling to produce compact Markdown. */
function renderRuns(runs: Run[]): string {
  let out = '';
  let index = 0;
  while (index < runs.length) {
    const { bold, italic } = runs[index];
    let text = '';
    while (index < runs.length && runs[index].bold === bold && runs[index].italic === italic) {
      text += runs[index].text;
      index++;
    }
    if (italic) text = emphasize(text, '*');
    if (bold) text = emphasize(text, '**');
    out += text;
  }
  return out;
}

function emphasize(value: string, marker: string): string {
  const [lead, core, trail] = splitEdges(value);
  return core === '' ? value : lead + marker + core + marker + trail;
}

function splitEdges(value: string): [string, string, string] {
  let leadEnd = 0;
  while (leadEnd < value.length && /\s/.test(value[leadEnd])) leadEnd++;
  let trailStart = value.length;
  while (trailStart > leadEnd && /\s/.test(value[trailStart - 1])) trailStart--;
  return [value.slice(0, leadEnd), value.slice(leadEnd, trailStart), value.slice(trailStart)];
}

function boldOf(el: HTMLElement): boolean | undefined {
  const weight = el.style.fontWeight;
  if (weight !== '') {
    if (weight === 'bold' || weight === 'bolder') return true;
    if (weight === 'normal' || weight === 'lighter') return false;
    const numeric = Number(weight);
    if (Number.isFinite(numeric) && numeric > 0) return numeric >= 600;
  }
  return el.tagName === 'B' || el.tagName === 'STRONG' ? true : undefined;
}

function italicOf(el: HTMLElement): boolean | undefined {
  const style = el.style.fontStyle;
  if (style === 'italic' || style === 'oblique') return true;
  if (style === 'normal') return false;
  return el.tagName === 'I' || el.tagName === 'EM' ? true : undefined;
}

/**
 * Normalizes multi-line emphasis across bullet items (e.g. - ***item 1\n- item 2***)
 * so each list item receives its own bold/italic tags and renders cleanly.
 */
export function normalizeMultilineMarkdown(md: string): string {
  let normalized = md;

  // 1. Bold + italic across bullet lines
  normalized = normalized.replace(
    /(?:^|\n)([ \t]*[-*]\s+)\*\*\*([\s\S]+?)\*\*\*(?=$|\n)/g,
    (match, bulletPrefix, inner) => {
      const items = inner.split(/\n[ \t]*[-*]\s+/);
      if (items.length > 1) {
        return '\n' + items.map((it: string) => `${bulletPrefix}***${it.trim()}***`).join('\n');
      }
      return match;
    }
  );

  // 2. Bold across bullet lines
  normalized = normalized.replace(
    /(?:^|\n)([ \t]*[-*]\s+)\*\*([\s\S]+?)\*\*(?=$|\n)/g,
    (match, bulletPrefix, inner) => {
      const items = inner.split(/\n[ \t]*[-*]\s+/);
      if (items.length > 1) {
        return '\n' + items.map((it: string) => `${bulletPrefix}**${it.trim()}**`).join('\n');
      }
      return match;
    }
  );

  // 3. Italic across bullet lines
  normalized = normalized.replace(
    /(?:^|\n)([ \t]*[-*]\s+)\*([^*\s][\s\S]+?)\*(?=$|\n)/g,
    (match, bulletPrefix, inner) => {
      const items = inner.split(/\n[ \t]*[-*]\s+/);
      if (items.length > 1) {
        return '\n' + items.map((it: string) => `${bulletPrefix}*${it.trim()}*`).join('\n');
      }
      return match;
    }
  );

  return normalized.startsWith('\n') && !md.startsWith('\n') ? normalized.slice(1) : normalized;
}

/**
 * Converts a plain-text markdown note (from disk or memory) into visual HTML
 * suitable for rendering inside a contentEditable editor.
 */
export function markdownToHtml(md: string): string {
  if (!md) return '';
  const cleaned = normalizeMultilineMarkdown(md);
  const lines = cleaned.split(/\r?\n/);
  const out: string[] = [];
  let inList = false;

  for (let i = 0; i < lines.length; i++) {
    const rawLine = lines[i];
    const bulletMatch = rawLine.match(/^([ \t]*)[-*]\s+(.*)$/);
    if (bulletMatch) {
      if (!inList) {
        out.push('<ul>');
        inList = true;
      }
      out.push(`<li>${renderInlineMarkdown(bulletMatch[2])}</li>`);
    } else {
      if (inList) {
        out.push('</ul>');
        inList = false;
      }
      if (rawLine.trim() === '') {
        out.push('<div><br></div>');
      } else {
        out.push(`<div>${renderInlineMarkdown(rawLine)}</div>`);
      }
    }
  }

  if (inList) {
    out.push('</ul>');
  }

  return out.join('');
}

/**
 * Escapes HTML entities and applies bold and italic tags to inline Markdown syntax.
 */
export function renderInlineMarkdown(text: string): string {
  let s = text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');

  s = s.replace(/\*\*\*([^\s*](?:.*?[^\s*])?)\*\*\*/g, '<strong><em>$1</em></strong>');
  s = s.replace(/\*\*([^\s*](?:.*?[^\s*])?)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/(^|[^*])\*([^\s*](?:.*?[^\s*])?)\*(?!\*)/g, '$1<em>$2</em>');
  s = s.replace(/^\*{1,3}/, '').replace(/\*{1,3}$/, '');

  return s;
}

/**
 * Sanitizes rich clipboard HTML by stripping foreign layouts, colors, and fonts while
 * preserving bold, italic, and bullet lists in the editor's visual style.
 */
export function sanitizePastedHtml(html: string): string {
  const md = htmlToMarkdown(html);
  if (!md) return '';
  return markdownToHtml(md);
}

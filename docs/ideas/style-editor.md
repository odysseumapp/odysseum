# Styles: how they work today, and ideas for editing them without writing CSS

## What is built (2026-09-19)

- A style is a tag in the Markdown. Blocks: `::: letter … :::`. Inline: `[text]{.hector}`. Both are Pandoc syntax, so
  pandoc reads the files as they are.
- What a tag looks like is CSS in the project's **Styles** folder. Every Style document applies to every document.
  Only `.name { … }` rules count; `.normal` is what untagged text looks like. Anything not inside a `.name` is
  ignored, except `@font-face`, `@page` and `@import`, which pass through so custom fonts and print setup work.
- Every new Style document starts with an empty `.normal`. New projects get `Styles/Default.md` with `.normal`
  (Georgia) and `.letter` (italic).
- The toolbar's Style dropdown lists every `.name` found in the Styles folder. Nothing selected → the block the
  cursor is in is tagged. Text selected → just that text. `No style` removes the tag.
- Text tagged `normal` inside a styled block comes back to the page look: the compiler resets every property any
  other style uses (`letter-spacing`, `font-style`, …) to the page value unless `.normal` sets it itself.
- Two Style documents that both define the same `.name` behave like CSS: the later one (project order) wins.
  There is no warning yet.

## Rough edges to decide on

- **Targets.** Nothing chooses which Style documents apply; all do. When export lands, a target (screen, PDF, epub,
  docx) needs a way to pick. Two shapes were discussed: one Style document per target (`Print.md`, `Kindle.md`, each
  repeating the names), or per-target blocks inside one document (a ` ```css pdf ` block under the base one). The
  second keeps every style in one place; the first is simpler to explain. Undecided.
- **Duplicate names / duplicate `.normal`.** Should be flagged, or a single Default document should own `.normal`.
- **Tags with no style.** A `::: leter` typo currently renders as plain text. The picker cannot produce one, but an
  outside edit can. Underline it in the editor, and list them somewhere.
- **docx.** Pandoc maps `custom-style` to Word styles, not CSS. Font, size, italic/bold, alignment, indent and
  spacing translate; the rest cannot. Needs either a generated reference.docx or a Lua filter, and a decision about
  emitting both `.letter` and `custom-style="Letter"` on the same fence.

## Ideas for a friendlier editor ("sugar" over the CSS)

1. **Form view per style.** A Style document gets a second view beside Write: one card per `.name` with the handful
   of things writers actually change — font, size, bold/italic, alignment, first-line indent, spacing before/after,
   colour. Editing a card rewrites that `.name` block; properties the form does not know stay untouched in an
   "advanced" section. The CSS stays the file format, so hand edits and the form never fight.
2. **Live sample.** Beside the form, a paragraph of the writer's own text rendered with the style, updating as they
   change things. Cheap: it is the same `.document-prose` stylesheet applied to a preview element.
3. **Font picker that knows what is installed.** `queryLocalFonts()` in Chromium/Electron lists the system fonts;
   fall back to the short serif/sans/mono list otherwise. Bundled fonts could live in a `Fonts` folder and become
   `@font-face` rules automatically.
4. **New style from the toolbar.** Typing a name that does not exist in the Style dropdown offers "Create style
   *leter*", which appends an empty `.leter {}` to Default.md (or opens the form for it). Kills the typo problem
   from the other side too.
5. **Preview as target.** Once targets exist, a dropdown on the editor toolbar: Screen / PDF / Ebook. Only changes
   which stylesheet is applied; the document does not move.
6. **Style inspector.** Cursor in a passage → a small panel says "letter (Default.md)" with a jump-to link. Doubles
   as the place to show duplicate-name warnings.
7. **Project copy as the template mechanism.** Already wanted: "New project → copy an existing one" replaces saved
   templates, and a shipped "Default Template" project carries the starter styles. Styles then travel with the copy
   for free.

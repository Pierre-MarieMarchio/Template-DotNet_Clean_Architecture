# 0016. The launcher draws only glyphs every console font carries

Status: Accepted

## Context

`dnx CleanArch.Create` asks its questions on a thread: a `│` rail with a marker for each question,
its answer and each step. A terminal draws a glyph its font lacks from a fallback font, at that
font's width and offset, so the marker lands off the rail and the columns after it shift. The set
every console font carries is WGL4: Consolas, Cascadia Mono, Lucida Console, DejaVu Sans Mono and
Menlo all have it. The diamonds, rounded corners and braille spinner other command-line tools
draw are outside it.

## Decision

- **Above Latin-1, the launcher draws WGL4 glyphs only.** `♦` for the question asked, `◊` for an
  answer, `▲` for a warning, `●` for a note, `■` and `□` for the steps, square box-drawing borders,
  and a spinner turning through box-drawing glyphs (`Launcher/CleanArch.Tooling/Prompts/RailSpinner.cs`).
- **A test holds it.** `GuidedFlowTests.EveryGlyphDrawn_IsOneConsoleFontsCarry_SoTheThreadStaysAligned`
  runs a whole new project on the thread and fails on any glyph above Latin-1 outside the WGL4 list
  it holds.

## Alternatives considered

- **The glyphs other prompt libraries draw**, `◆ ◇ ╭ ⠋`. They look right in a font that has them,
  and off the rail in every other, which is where a first run on Windows usually happens.
- **Choosing the glyphs by terminal.** No console tells a program its font, and guessing from the
  environment misses the default Windows console, SSH sessions and editors' terminals.
- **ASCII only.** Aligned everywhere, but box drawing is in WGL4 already, and the thread would lose
  its shape for nothing.

## Consequences

- A new marker comes from WGL4, or the test fails; emoji, braille and the geometric shapes beyond
  WGL4 stay out.
- The thread looks a little plainer than the tools it resembles.

## Revisit when

The terminals the launcher runs in all ship a font that carries the wider set, or a console lets a
program read its font.

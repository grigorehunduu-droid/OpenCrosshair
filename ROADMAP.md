# Roadmap

Improvements that are possible and free to build, roughly easiest first. Contributions welcome.

## Done in 1.4

Community crosshair library on GitHub · color emoji in labels · translations (9 languages besides English) ·
a skippable guided tour with "Try it" steps · a menu bar with undo/redo, clearer feedback and a tidier window and
tray menu, following Apple's design principles (agency, familiarity, simplicity, responsibility).

## Done in 1.3

Back on top instantly · hotkeys set in the customize window · per-game crosshairs · Shoot only reacts in games ·
crosshair files that include the picture · dark tooltips, scrollbars and number boxes · window scaling for high-DPI
displays (built in, still to be tested on a scaled display).

## Moderate (a few hours each)

1. **Test at Windows display scaling above 100 %** and fix anything that looks off.
2. **Per-game position:** remember the nudge offset per game, not only per display.
3. **Translation review** by native speakers, and more languages (add a file in `src/OpenCrosshair/lang`).

## Left out on purpose

- **Code signing** (removes the SmartScreen warning) and **exclusive-fullscreen support** through an Xbox Game Bar
  widget: both need a paid certificate or developer account.
- **Steam Workshop:** needs a paid Steam listing.
- **Mac/Linux:** a different app. **Games that ban overlays:** their rules, not something code can change.
- **Right-to-left languages** (Arabic, Hebrew): the window would need a mirrored layout; possible later if asked for.

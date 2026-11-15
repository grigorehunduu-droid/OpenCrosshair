# Roadmap

Improvements that are possible and free to build, roughly easiest first. Contributions welcome.

## Done in 1.3

Back on top instantly · hotkeys set in the customize window · per-game crosshairs · Shoot only reacts in games ·
crosshair files that include the picture · dark tooltips, scrollbars and number boxes · window scaling for high-DPI
displays (built in, still to be tested on a scaled display).

## Moderate (a few hours each)

1. **Test at Windows display scaling above 100 %** and fix anything that looks off.
2. **Per-game position:** remember the nudge offset per game, not only per display.

## Bigger (days)

3. **Emoji in labels.** Needs a different text engine that can draw color emoji.
4. **Translations.** Move every text into a translatable file, then add languages.
5. **Community crosshair library on GitHub.** A shared, browsable list of share codes and `.ochair` files in this
   repository, importable from the app. No server or paid hosting needed.

## Left out on purpose

- **Code signing** (removes the SmartScreen warning) and **exclusive-fullscreen support** through an Xbox Game Bar
  widget: both need a paid certificate or developer account.
- **Steam Workshop:** needs a paid Steam listing.
- **Mac/Linux:** a different app. **Games that ban overlays:** their rules, not something code can change.

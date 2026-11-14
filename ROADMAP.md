# Roadmap

Improvements that are possible and free to build, roughly easiest first. Contributions welcome.

## Moderate (a few hours each)

1. **Back on top instantly.** React to window changes instead of re-checking every half second, so another
   always-on-top window can never cover the crosshair even briefly.
2. **Set hotkeys in the customize window.** Press the keys you want instead of editing `settings.json`.
3. **Per-game crosshairs.** Notice which game is in front and switch crosshair and position automatically.
4. **Shoot only reacts in games.** Builds on per-game detection, so clicks on the desktop and in other apps are ignored.
5. **Share codes that include the image.** Possible, but codes get very long, so probably as a separate file to share.
6. **Finish dark mode everywhere.** Tooltips, scrollbars and the number-box arrows are still light in the dark theme.
7. **Check at Windows display scaling above 100 %.** Needs testing on a scaled display; fix anything that's off.

## Bigger (days)

8. **Emoji in labels.** Needs a different text engine that can draw color emoji.
9. **Translations.** Move every text into a translatable file, then add languages.
10. **Community crosshair library on GitHub.** A shared, browsable list of share codes in this repository,
    importable from the app. No server or paid hosting needed.

## Left out on purpose

- **Code signing** (removes the SmartScreen warning) and **exclusive-fullscreen support** through an Xbox Game Bar
  widget: both need a paid certificate or developer account.
- **Steam Workshop:** needs a paid Steam listing.
- **Mac/Linux:** a different app. **Games that ban overlays:** their rules, not something code can change.

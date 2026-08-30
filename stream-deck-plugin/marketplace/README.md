# Marketplace media

This directory contains the source and exported media used for the WinNetSwitch Elgato Marketplace submission.

- `source/` contains editable 1920 × 960 SVG compositions built from the real WinNetSwitch plugin artwork.
- `media/` contains the exact exported files uploaded to Maker Console.
- `SUBMISSION.md` contains the field-by-field English listing copy and public links.

Export PNG media from the repository root:

```bash
rsvg-convert -w 288 -h 288 stream-deck-plugin/dev.witqq.win-net-switch.sdPlugin/imgs/plugin/marketplace.svg -o stream-deck-plugin/marketplace/media/app-icon.png
for source in stream-deck-plugin/marketplace/source/*.svg; do
  output="stream-deck-plugin/marketplace/media/$(basename "${source%.svg}").png"
  rsvg-convert -w 1920 -h 960 "$source" -o "$output"
done
```

The media must accurately represent the current plugin. Do not add real adapter identifiers, Wi-Fi network names, ratings, signatures, approval badges, or claims that the companion is bundled with the plugin.

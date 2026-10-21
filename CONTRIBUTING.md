# Contributing to Synqora

Synqora is an early native Windows/Android phone companion. Improvements to real device behavior, media latency, accessibility and measured rendering performance are welcome.

Read the [installation guide](docs/INSTALLATION.md), [design system](docs/DESIGN.md) and [validation limits](docs/VALIDATION.md) before changing device integration. Build instructions are in the [README](README.md).

For a bug report, include app version, Windows/Android versions, phone model, the affected feature, reproduction steps, expected/actual behavior and the exact error. Review screenshots and logs before uploading: remove pairing links, local addresses and personal device names.

Keep UI rendering separate from media processing and transport. Do not add input smoothing or animations that delay pointer/key transmission. Use shared tokens, preserve reduced-motion/transparency fallbacks, and test light/dark themes and compact/large sizes. Report performance measurements with hardware, scenario and their limits.

Do not commit signing keys, keystores, local preferences, pairing data, build outputs or vendor archives. The original audio-driver research is MS-PL and remains unsigned; do not enable production gates based on compile-only tests. Application source is MIT; preserve upstream component licenses and attribution.

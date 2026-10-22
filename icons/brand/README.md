# Yourself Copilot companion mark

Original application brand asset, separate from the Microsoft Fluent UI System Icons in `../svg` and their MIT manifest/license. Existing bundled font licenses also remain separate.

The companion direction was selected from an OpenAI ImageGen concept on 2026-10-22. `companion.svg` is a manually authored, three-path vector interpretation of that reference: a deep sage rounded square, a warm ivory rising companion curve and an inclined oval. The generated concept sheet is not bundled or cropped into the application.

`companion.ico` is rendered directly from the same SVG drawing by `qa/verify-brand.ps1 -RegenerateIcon` at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels, using WPF and PNG frames with alpha. The SVG is used by the desktop card and workbench. The ICO is used by the tray, windows and executable resource. If an asset is missing or invalid, the application retains this same vector as an embedded fallback.

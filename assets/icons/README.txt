Yourself Copilot offline icons

Source: Microsoft Fluent UI System Icons
Repository: https://github.com/microsoft/fluentui-system-icons
License: MIT; the unmodified upstream license is included as LICENSE.

svg/ contains original unmodified upstream 24 regular SVG files, plus the 24 filled Checkmark Circle for completed history status.
manifest.json records the pinned upstream revision, exact source URL and SHA256 of each file.
No fonts are installed and no network access is needed while the app runs.

Application names:
  tasks = Checkbox Checked (a task checkbox)
  note = Note
  history = History
  settings = Settings
  images = Image Multiple
  plus = Add
  more = More Horizontal
  expand = Arrow Expand
  calendar = Calendar LTR
  remove = Dismiss
  trash = Delete
  history-done = Checkmark Circle 24 filled (static completed history status)
  history-pending = Circle (static unfinished history status)
  copy = Copy

AppVisuals reads these SVG path geometries and applies the caller's foreground brush.
If an asset is missing, malformed or unsupported, it logs a diagnostic once per name
and returns a readable Chinese text label in the requested icon box.

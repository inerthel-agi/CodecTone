# ADR 011: Sidebar shell and design system

## Status

Accepted on 2026-10-29. Phase 1 (shell and controls) and phase 2 (motion,
waveform, remix layout, report lists) implemented.

## Context

At the 900×650 minimum size, the header and the wrapping tab row used about
180 px of height. The active tab rendered light text on the bright OLED accent
because the implicit `TextBlock` style overrode the button's foreground. Labels
were all caps, checkboxes and sliders kept the default WPF look, keyboard focus
was not visible, and dropping files gave no visual feedback.

## Decision

- Use a fixed 184 px sidebar for navigation, theme switch, and the local-only
  notice. Each workspace starts with a title and a one-line description.
- Remove the implicit `TextBlock` foreground style. Text inherits the foreground
  from the root grid, so content inside buttons follows the button foreground.
- Add `OnAccentBrush` (black on the OLED accent, white on the WHITE accent) and
  `HoverBrush`. Darken the WHITE accent from `#008A6A` to `#007A5E`.
- Icons use `Segoe Fluent Icons` with `Segoe MDL2 Assets` as the Windows 10
  fallback. Glyph code points are defined once in `Themes/Controls.xaml`.
- Checkboxes render as switches. Format pickers use a `ListBox` styled as a
  segmented control, so the view models are unchanged.
- Empty sources show a drop zone. Loaded sources show a file card. Cut and Remix
  add the probed format, rate, depth, duration, and size.
- The bottom bar shows progress only while busy. Cancel replaces the primary
  action while busy. Open folder appears after a successful operation in the
  current workspace.

## Phase 2 decisions

- Keep the accent for small signals. Selected items use the neutral
  `SelectedBrush`; info boxes use `PanelRaisedBrush`.
- Transitions use `MotionFast` (140 ms) and `MotionMedium` (220 ms) in
  `Themes/Controls.xaml`. `ThemeService.DisableMotion` zeroes them before the
  window loads when `SystemParameters.ClientAreaAnimation` is false. The values
  must live in the same dictionary as the styles: a `StaticResource` inside a
  merged dictionary does not resolve keys declared in `App.xaml`.
- State animations play `From` a start value with `FillBehavior="Stop"` while a
  trigger setter holds the end state. If an animation never runs, the control
  still shows the correct state.
- The playhead is estimated from wall-clock time, because `PlaySound` reports
  no position.
- `RemixRackValidator.Bounds` is the single source for parameter ranges. The
  validator and the rack sliders both read it.
- Report rows come from the existing `OK`, `FAIL`, `SKIP` and `CANCELLED`
  prefixes. The raw text is unchanged and remains what Save report writes.

## Glass theme

- `Themes/Glass.xaml` is a third palette. OLED and WHITE keep their look; they
  only gained the shared keys `BackdropBrush`, `PopupBrush`, `CardRadius` and
  `ControlRadius`, set to their previous values.
- `TitleBarTheme.Apply` requests `DWMSBT_TRANSIENTWINDOW` (acrylic), clears the
  WPF composition background, and extends the frame over the client area. It
  returns `true` only when Windows accepts the backdrop.
- No live backdrop on Windows 10, on Windows 11 before 22H2, or when
  Transparency effects is off (`EnableTransparency = 0`). `ThemeService` then
  swaps `BackdropBrush` for the opaque `GlassFallbackBrush` gradient, so text
  never sits on an unknown, possibly light, background.
- Popups and tooltips use the opaque `PopupBrush`, because they are separate
  windows without the backdrop.
- Apple-style refraction is out of scope. WPF has no backdrop sampling short of
  custom shaders.

## Contrast

| Pair | Ratio |
| --- | --- |
| `#000000` on OLED accent `#10D6A0` | 11.1:1 |
| `#F6F6F6` on OLED accent (previous active tab) | 1.7:1 |
| `#FFFFFF` on WHITE accent `#007A5E` | 5.3:1 |
| `#FFFFFF` on previous WHITE accent `#008A6A` | 4.3:1 |
| WHITE accent `#007A5E` on `#DDF4EC` | 4.6:1 |
| OLED accent on `#073E32` (active sidebar item) | 6.4:1 |

## Consequences

- Convert and Compress no longer accept a typed source path. File pickers and
  drag and drop remain.
- The sidebar does not collapse. At 900 px, the content column is about 716 px.
- `WorkspaceTests.WorkspaceLayouts_RenderBothThemesAtMinimumSize` renders all
  six workspaces in both themes at 900×650 and 1180×820.

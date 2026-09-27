# Stage 5CC pinned-driver compatibility finding (2026-09-27)

Task 10 Step 4 is **Failed**. The local `winappcli-x64.zip` matched the pinned v0.5.0 manifest byte count and SHA-256. The Release `win-x64` product was published from `8544c68b72ae99ecd34ebdff03c2c640494836f1`. The final clean gate run is `.superpowers/stage5cc-uia-gate-8544c68-r11`; its report records `startup_layout.driver_schema_incompatible` and `cleanup.status=Passed`. No viewport screenshot or UIA acceptance result was produced.

## Exact driver observations

The pinned driver's `ui inspect -a <launched PID> --depth 8 --json` returns `windows[].elements[]` with nested `children`. Nodes use `type`, `isEnabled`, `isOffscreen`, and direct `x/y/width/height` fields. The runner's fake driver instead returns a flat `tree[]` with `controlType`, `enabled`, `offscreen`, `bounds`, and `patterns`. The production scenario therefore cannot evaluate its manifest assertions against the native response. A regression test now fails safely with the stable schema code before any UI mutation when it receives this native shape.

Two read-only native queries against the published window returned:

| Element | Manifest control type | Native UIA control type | Required pattern | Native pattern evidence |
|---|---|---|---|---|
| `TraceFoldMainWindow` | `Grid` | `Group` (`NamedContainerAutomationPeer`) | none | not applicable |
| `NavInbox` | `NavigationViewItem` | `ListItem` (`Microsoft.UI.Xaml.Controls.NavigationViewItem`) | `SelectionItem` | undetermined: the driver's `get-property --property IsSelectionItemPatternAvailable` returned `null` |

The driver's `get-property NavInbox --json` returned `AutomationId=NavInbox`, `ControlType=ListItem`, `IsEnabled=True`, `IsOffscreen=False`, and `IsSelected=False`; it did not report supported patterns. `ui inspect NavInbox --verbose --json` returned `Cannot specify both --verbose and --json options together.` Without JSON, targeted verbose inspection listed the item and bounds but no pattern list. `null` does **not** prove the product lacks `SelectionItem`; it proves this driver query did not establish it.

Relevant driver response fields, with process IDs and screen coordinates omitted:

```json
{"matches":[{"type":"Group","name":"TraceFold 主窗口","automationId":"TraceFoldMainWindow","className":"NamedContainerAutomationPeer","isEnabled":true,"isOffscreen":false}]}
{"matches":[{"type":"ListItem","name":"待入库导航","automationId":"NavInbox","className":"Microsoft.UI.Xaml.Controls.NavigationViewItem","isEnabled":true,"isOffscreen":false,"isInvokable":true}]}
{"properties":{"IsSelectionItemPatternAvailable":null}}
```

## Design review boundary

The static manifest currently uses XAML element types where the gate expects native UIA control types. Adapting the nested response and recording native types is straightforward, but the current pinned driver has not supplied evidence for the required patterns. Decide how the native pattern check will be obtained and reviewed before changing the contract or adding an AutomationPeer. Do not treat `isInvokable` or `IsSelected` as proof of `Invoke` or `SelectionItem`, and do not substitute coordinates for selectors.

The owned-root creation check and cleanup invocation were also corrected during this gate attempt. The PowerShell 7 and Windows PowerShell 5.1 toolkit suites each pass 36/36; the final real run cleaned up its owned root and launched product process. Task 11 remains gated on a passed Task 10 Step 4.

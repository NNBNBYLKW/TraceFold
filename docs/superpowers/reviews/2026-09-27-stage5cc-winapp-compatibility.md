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

## Follow-up native probe and design decision

The read-only Support probe added in `8d22b11` established `NavInbox` `SelectionItem=true`. The pinned driver's `null` was a query limitation. A separate owned-root diagnostic on the published `8fb66d1` product entered the empty Inbox and queried five exact IDs. It found one `InboxSearchBox` with native type `Group`, class `AutoSuggestBox`, but **`Value=false`**. In the same observation, `InboxStateFilter` `ExpandCollapse=true`, `InboxGrid` `Selection=true`, and both `InboxRefreshButton` and `InboxInspectorOpenButton` `Invoke=true`. The run intentionally stopped after these read-only queries; its report records `startup_layout.diagnostic_stop` and `cleanup.status=Passed`. The temporary diagnostic code was removed without leaving a worktree change.

The pinned driver's depth-20 tree shows `InboxSearchBox` has exactly one nested `Edit` element, class `TextBox`, with the same Chinese accessible name and generic AutomationId `TextBox`. This is a composite control. The design decision is to keep the stable `InboxSearchBox` ID on the outer AutoSuggestBox and declare that its `Value` requirement is verified on a **unique `Edit` descendant scoped to that exact outer ID**, including the name and native Value pattern. A null, missing, duplicate, wrong-name, or Value=false descendant must fail or block as appropriate. Do not mark the outer Group as Value-capable, remove the Value requirement, or add an unreviewed AutomationPeer. Apply the same scoped-descendant rule only to other AutoSuggestBox entries after separately checking their native trees.

The same native tree shows `NavInbox` is 168 physical pixels wide in a 1280-DIP window, while its containing `PaneRoot` (`SplitViewPane`) is exactly 176 pixels wide. The viewport check must measure the pane boundary, not the navigation item. `PaneRoot` is a WinUI template AutomationId used only as a layout observation target; it is not a workflow action selector. Verify its class and unique containment under `Navigation` before relying on it, and repeat at 1024 and 1600 DIP. No expected 48/176 values should be changed based on the item width.

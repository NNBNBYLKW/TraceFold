from apps.desktop.app.shell.tray import TrayIntegrationSkeleton


def test_tray_menu_items_stay_minimal_and_shell_oriented():
    tray = TrayIntegrationSkeleton()

    hidden_items = tray.menu_items(window_visible=False)
    visible_items = tray.menu_items(window_visible=True)

    assert [item.label for item in hidden_items] == [
        "Open TraceFold",
        "Show Window",
        "Quit",
    ]
    assert [item.label for item in visible_items] == [
        "Open TraceFold",
        "Hide Window",
        "Quit",
    ]

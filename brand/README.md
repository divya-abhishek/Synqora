# Synqora identity

The original [paired-loop mark](synqora-mark.svg) represents a phone and computer connected directly. Two rounded open loops form one compact silhouette. The app icon uses pearl on graphite; in-app vectors follow the current theme. This replaces the earlier S identity.

[tools/export_brand.py](../tools/export_brand.py) renders the transparent mark, square preview, Windows ICO and Android adaptive/legacy launchers from the same vector geometry. Safe-area checks keep it away from masks and edges. In-app marks follow light/dark themes.

Android uses native vector geometry in the app and notification, with separate padded launcher exports. The 108-unit launcher canvas keeps the mark inside adaptive masks. Windows uses the same geometry for the title bar and multi-resolution ICO. No Apple assets are used.


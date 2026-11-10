#!/bin/bash
# Install the selected upstream application without a user profile or fixtures.
set -euo pipefail
if [[ $# != 1 || ! -f "$1" ]]; then
    echo 'usage: install_joplin.sh /path/to/Joplin.AppImage' >&2
    exit 2
fi
if [[ -e /opt/joplin ]]; then
    echo '/opt/joplin already exists; refusing to replace an existing application' >&2
    exit 1
fi
stage=$(mktemp -d /tmp/hcu-joplin-install.XXXXXX)
trap 'rm -rf "$stage"' EXIT
install -m 755 "$1" "$stage/application.AppImage"
cd "$stage"
./application.AppImage --appimage-extract >/dev/null
mv squashfs-root /opt/joplin
chown -R root:root /opt/joplin
chmod -R a+rX /opt/joplin
chmod 4755 /opt/joplin/chrome-sandbox
install -d /usr/local/bin
cat > /usr/local/bin/joplin <<'LAUNCHER'
#!/bin/sh
export APPDIR=/opt/joplin
exec /opt/joplin/joplin "$@"
LAUNCHER
chmod 755 /usr/local/bin/joplin
cat > /usr/share/applications/joplin.desktop <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Joplin
Exec=/usr/local/bin/joplin %U
Icon=/opt/joplin/joplin.png
Terminal=false
Categories=Office;
DESKTOP
chmod 644 /usr/share/applications/joplin.desktop

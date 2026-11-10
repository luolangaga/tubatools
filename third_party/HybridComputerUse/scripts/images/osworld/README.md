# Desktop image setup

The default installation adds ordinary desktop applications to a fresh Ubuntu
OSWorld guest. It preserves the existing LibreOffice, browser and benchmark
software versions. Package dependencies can require upgrades; review the apt
plan before installation. The scripts do not launch a benchmark or install HCU.

Use a disposable copy of the base image. Run the scripts inside that guest,
then remove the staging directory before exporting the image. Keep these
operator scripts and software archives outside the evaluated agent's workspace.

## Prepare software archives

`manifest.json` selects the application archives and desktop configuration.
`freecad-packages.json` selects Ubuntu's FreeCAD and OCCT packages. These files
record versions, filenames and package metadata. They do not attest archive
contents. Use software archives from the corresponding upstream distributions.

1. Create an asset directory readable by the guest desktop account.
2. Download Joplin and Slicer from the URLs in `manifest.json`.
3. Place the GeoGebra portable archive and KiCad offline bundle in that directory.
4. In Ubuntu with matching apt repositories, build the private FreeCAD archive:

```bash
python3 prepare_freecad.py --output /tmp/desktop-assets/freecad-private.tar
```

The filenames must match `manifest.json`. Copy only software packages from an
OSWorld asset distribution. Do not copy task documents, evaluator materials or
credentials. No binary archives belong in this repository.

The `qt` phase also installs Ubuntu's Java ATK wrapper and native JNI library.
These packages let Java desktop applications expose controls through AT-SPI.
A portable application with its own Java runtime must enable the wrapper in its
launcher. Installing the packages alone does not configure a bundled runtime.
Keep that configuration local to the application's Java command; do not export
accessibility options globally to unrelated Java processes.

## Install in a fresh guest

The examples assume that the desktop account is `user`. Replace that account
with the guest's actual account.

1. Copy this directory and the software archives to a temporary guest directory.
2. Configure independent binary package caches, then update apt's package index
   and inspect its proposed dependency changes:

```bash
sudo python3 configure_apt.py
```

APT reads the configuration on its next invocation. An existing PackageKit
daemon reads it on its next start; use a fresh boot for final image validation.
3. Check the guest platform and archive filenames:

```bash
python3 install.py --user user --assets /tmp/desktop-assets --check
```

4. Install the applications and preferences as root:

```bash
sudo python3 install.py --user user --assets /tmp/desktop-assets
```

Use repeated `--phase` options to select `gtk`, `qt`, `joplin`, `special`,
`preferences` or `support`. Installation stops when a command fails. Joplin
refuses to replace an existing installation. Use a fresh base copy when changing
the selected software rather than rerunning over an unknown partial image.

5. Hold automatic Snap refreshes before the final application checks:

```bash
sudo snap refresh --hold=forever
snap refresh --time
snap list
```

Confirm that the refresh schedule reports `hold: forever`. Record the installed
Snap revisions with the image. The hold prevents automatic refreshes; an explicit
manual refresh that names a Snap can still update that application.

The preferences phase also runs `configure_updates.py` as the desktop user.
It disables Software's automatic update downloads and pop-up notification
banners. A hidden desktop-entry alias lets GNOME associate Snap Store's
notifications with its application policy. Notifications remain enabled in
the notification center; Software's manual update controls and other
applications' notification settings remain unchanged. Restart Software or
start a fresh desktop session after applying these preferences.

6. Start a fresh desktop session and verify application startup and input.
7. Remove the copied scripts, archives and extracted staging files.
8. Shut down the guest and export a new image without replacing the base.

`--check` validates the operating system, architecture and required filenames.
It does not run apt, verify archive contents or prove application behavior.

## Validate the guest filesystem before export

`qemu-img check` validates the QCOW container structure. It does not establish
that the guest filesystem is healthy. After a clean guest shutdown, expose the
image's filesystem partition through an offline image tool and run
`e2fsck -fn /dev/<guest-filesystem-partition>` for an ext filesystem. Confirm the
partition belongs to the offline guest image, not the host or a mounted guest.

Keep the original image unchanged. If the filesystem check finds errors, preserve
the output and repair a separate writable image copy. Run the read-only check
again after repair. Export a new image, then cold-boot it and verify affected
applications. A successful QCOW check alone is not a release readiness check.

## Chrome repository keys

If apt reports a missing Chrome signing key, run
`sudo python3 repair_chrome_repository.py` inside the guest before installing
packages. The script downloads Google's current Linux signing keys over HTTPS,
writes a dedicated keyring and binds the active Chrome source with `signed-by`.
It refreshes only that repository's index and rejects signature or download
errors. It checks that Chrome's installed version stays unchanged. It does not
upgrade Chrome or disable signature verification. Other repository entries and
legacy key files remain unchanged.

## Optional VS Code upgrade

This separate procedure selects an official Microsoft package. It does not run
as part of `install.py`. Preserve the existing Code settings and profile.

1. Close Code in the guest.
2. Download the selected release and check its package metadata:

```bash
code_version=1.143.0-1792533444
code_deb="/tmp/code_${code_version}_amd64.deb"
curl --fail --location --proto '=https' --proto-redir '=https' \
  "https://update.code.visualstudio.com/${code_version%%-*}/linux-deb-x64/stable" \
  --output "$code_deb"
test "$(dpkg-deb -f "$code_deb" Package)" = code
test "$(dpkg-deb -f "$code_deb" Version)" = "$code_version"
test "$(dpkg-deb -f "$code_deb" Architecture)" = amd64
```

Stop if any metadata check fails. Keep the variables in the same shell for the
remaining commands.

3. Inspect the dependency plan:

```bash
sudo apt-get --simulate --no-remove --reinstall install "$code_deb"
```

Proceed only if `code` is the only package to change, with no added or removed
packages. `--reinstall` also restores package files when this version is already
installed. Repair filesystem errors offline before reinstalling packages.

4. Install the package and verify its recorded version and package state:

```bash
sudo apt-get --no-remove --reinstall -y install "$code_deb"
test "$(dpkg-query -W -f='${Version}' code)" = "$code_version"
sudo dpkg --audit
```

5. Restart Code as the desktop user with the existing profile, then verify
   editor accessibility, Unicode input, newlines, tabs and saving a file.

Do not pass a diagnostic `--user-data-dir` or copy a test profile into the
image. This package update does not establish final image readiness; complete
the filesystem and cold-boot checks before selecting the image for evaluation.

## Optional LibreOffice installation

`install_libreoffice.py` installs the official release selected in
`libreoffice.json` beside the distribution version. This separate command does
not run as part of `install.py`. It validates the exact package set, versions
and architecture before installing. It creates an isolated profile and sets
ordinary preferences to suppress welcome, tip and template dialogs.
For a profile without Impress Navigator preferences, it initializes the complete
docking record so the first Navigator window has usable dimensions. Existing
window preferences remain unchanged. This changes application configuration;
it does not patch LibreOffice's source or executable.

Download the archive from the manifest's source, then run the installer as root
with `--archive /path/to/the/archive.tar.gz --desktop-user user`. The archive's
basename must match the manifest. The default preserves the existing commands
and user profile. Add `--switch-default` only when selecting this release for
the image. That option adds `/usr/local/bin/libreoffice` and `soffice` symlinks;
the distribution commands remain in `/usr/bin`. Close candidate applications
before installation, then verify Writer, Calc and Impress in a fresh session.

## Optional WPS installation

`install_wps.py` installs the upstream package selected in `wps.json`. It does
not run as part of `install.py`. The download URL can advance to another
release, so the installer rejects a different package name, version or
architecture. Run `python3 install_wps.py --package /path/to/wps-office.deb
--check` to inspect the dependency plan. Remove `--check` and run as root to
install. The installer refuses package removals and verifies the installed
version and dpkg state. WPS's package scripts change system and user MIME
defaults. The installer preserves those preference files, their permissions and
their ownership, even when installation fails. WPS remains available through
its desktop entry and the file manager's Open With menu.

The installer enables `QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1` in the vendor's
`wps`, `wpp` and `et` startup scripts. Both command-line and desktop launches
inherit the setting without a login shell. The original argument parsing and
exit behavior remain unchanged. This enables the bundled Qt accessibility
bridge; it does not add semantics to custom-drawn document content.

For an existing installation, run `sudo python3 configure_wps_accessibility.py`
inside the guest. Close all WPS processes before checking a fresh launch.
Package upgrades can replace these launchers; rerun the configuration after
upgrades through other installers. Unknown launcher formats or existing
conflicting settings stop configuration before any launcher changes.

Close WPS before installation. The script does not copy profiles, accept license
terms or change input-method settings. Confirm the license through WPS's normal
first-launch interface, then verify Writer, Spreadsheets and Presentation.

## Repairs and configuration

| Component | Installed behavior |
| --- | --- |
| APT and PackageKit | Build binary package caches in memory, avoiding deletion of another reader's temporary cache during concurrent refreshes. |
| FreeCAD | Uses private OCCT libraries so KiCad can install its incompatible system OCCT packages. System Qt preserves accessibility. |
| Spyder | Passes integer image dimensions to Qt, preventing startup failures with stricter Python bindings. Unknown source patterns fail. |
| GeoGebra | Restores executable permissions on its launcher, Java executable and Java process helper. |
| VS Code | Enables editor accessibility for the next application launch while preserving other JSON settings. |
| Joplin | Installs the upstream AppImage contents with the Chromium sandbox enabled. |
| REAPER | Selects the guest's PulseAudio backend. |
| Thunderbird | Disables unsolicited donation, data-reporting and welcome pages through application defaults. Mail accounts, stored messages and network settings remain unchanged. |
| WPS, MuseScore and Slicer | Save desktop preferences that suppress first-launch prompts and update checks. |
| OpenBoard | Installs the selected Snap channel. The channel can advance independently of this manifest. |
| Support | Installs NSS certificate tools and IBus bindings. Certificate import and the active desktop input source remain the deployment's responsibility. |

FreeCAD's private package prefix avoids global library replacement. Its launcher
sets library paths only for FreeCAD. The installer links `/usr/share/freecad`
to the private package's resources because the Ubuntu binary requires that
absolute path for its Start page and workbench icons. It rejects an occupied
resource path instead of replacing another installation. The scripts neither
force conflicting packages into dpkg nor copy benchmark solutions into the guest.

The package sources remain Ubuntu repositories and upstream release archives.
Apt's package indexes, downloaded archives, package choices and normal package
management locks remain available. Disabling `pkgcache` and `srcpkgcache` follows
APT's documented empty-path configuration and can increase package-query startup
time. Manual apt and PackageKit operations remain available; the installer does
not stop services or suppress package errors.
Apt dependency versions and Snap revisions are not frozen by this manifest.
Record the installed package list and Snap revision alongside the exported image.
Restart applications after setting preferences. GUI and accessibility checks
must run against the exported image before the operator selects it for evaluation.

Run the focused script tests without installing packages:

```bash
python3 -m unittest discover -s scripts/images/osworld -p 'test_*.py'
```

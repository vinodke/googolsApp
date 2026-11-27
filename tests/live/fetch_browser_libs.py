#!/usr/bin/env python3
"""Cloud Shell has no root, so Playwright's Chromium can't get its system libraries from tdnf.
This downloads the Azure Linux 3.0 RPMs for the missing libraries and unpacks them into ~/browser-libs,
then loops on ldd until nothing is missing. Use with LD_LIBRARY_PATH=~/browser-libs/usr/lib."""
import glob, os, re, subprocess, urllib.request

REPO = "https://packages.microsoft.com/azurelinux/3.0/prod/base/x86_64/Packages/"
DEST = os.path.expanduser("~/browser-libs")
# soname prefix -> package name
PKG = {"libnspr4": "nspr", "libplc4": "nspr", "libplds4": "nspr", "libnss3": "nss-libs", "libnssutil3": "nss-util", "libsmime3": "nss-libs",
       "libsoftokn3": "nss-softokn", "libfreebl": "nss-softokn-freebl", "libatk-1.0": "atk", "libatk-bridge-2.0": "at-spi2-atk", "libatspi": "at-spi2-core",
       "libXcomposite": "libXcomposite", "libXdamage": "libXdamage", "libXfixes": "libXfixes", "libXrandr": "libXrandr", "libgbm": "mesa-libgbm",
       "libxkbcommon": "libxkbcommon", "libdrm": "libdrm", "libwayland-server": "wayland", "libasound": "alsa-lib", "libXext": "libXext",
       "libXi": "libXi", "libxshmfence": "libxshmfence", "libXrender": "libXrender", "libexpat": "expat", "libdbus-1": "dbus-libs",
       "libsqlite3": "sqlite-libs", "libXtst": "libXtst", "libcups": "cups-libs", "libpango": "pango", "libcairo": "cairo", "libX11": "libX11",
       "libxcb": "libxcb", "libXau": "libXau", "libglapi": "mesa-libglapi", "libffi": "libffi", "libgobject": "glib2", "libglib": "glib2", "libgio": "glib2"}
listings = {}

def latest(pkg):
    letter = pkg[0].lower()
    if letter not in listings:
        listings[letter] = urllib.request.urlopen(REPO + letter + "/", timeout=60).read().decode()
    names = re.findall(r'href="(?:\./)?(' + re.escape(pkg) + r'-\d[^"/]*\.x86_64\.rpm)"', listings[letter])
    return sorted(names)[-1] if names else None

def missing(binary):
    env = dict(os.environ, LD_LIBRARY_PATH=f"{DEST}/usr/lib:{DEST}/usr/lib64")
    out = subprocess.run(["ldd", binary], capture_output=True, text=True, env=env).stdout
    return sorted({l.split()[0] for l in out.splitlines() if "not found" in l})

binary = glob.glob(os.path.expanduser("~/.cache/ms-playwright/chromium_headless_shell-*/chrome-headless-shell-linux64/chrome-headless-shell"))[0]
os.makedirs(DEST, exist_ok=True)
done = set()
for round_ in range(6):
    need = missing(binary)
    print("round", round_, "missing:", need)
    if not need:
        print("ALL-LIBS-OK"); break
    for so in need:
        pkg = next((p for prefix, p in PKG.items() if so.startswith(prefix)), None)
        if not pkg or pkg in done:
            print("  no package known for", so); continue
        name = latest(pkg)
        if not name:
            print("  not in repo:", pkg); done.add(pkg); continue
        path = os.path.join(DEST, name)
        urllib.request.urlretrieve(REPO + pkg[0].lower() + "/" + name, path)
        subprocess.run(f"cd {DEST} && rpm2cpio {name} | cpio -idm --quiet", shell=True, check=True)
        done.add(pkg)
        print("  unpacked", name)

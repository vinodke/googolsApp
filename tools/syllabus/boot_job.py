# Bootstrap for the container job: installs packages, downloads gen_images.py and runs it for $GRADE.
# Packages come from a wheel bundle in blob storage (WHEELS_URL) when given, because the container's
# connection to PyPI keeps dropping; otherwise from PyPI with retries.
import os, subprocess, sys, time, urllib.request
PKGS = ["certifi", "pymssql", "azure-storage-blob", "pillow"]
pip = [sys.executable, "-m", "pip", "install", "-q", "--root-user-action=ignore"]
if os.environ.get("WHEELS_URL"):
    urllib.request.urlretrieve(os.environ["WHEELS_URL"], "/w.tgz")
    os.makedirs("/w", exist_ok=True)
    subprocess.check_call(["tar", "xzf", "/w.tgz", "-C", "/w"])
    subprocess.check_call(pip + ["--no-index", "--find-links", "/w"] + PKGS)
else:
    for _i in range(6):
        if subprocess.call(pip + ["--timeout", "120", "--retries", "10"] + PKGS) == 0: break
        time.sleep(30)
    else:
        sys.exit("pip install failed")
import certifi
os.environ.setdefault("SSL_CERT_FILE", certifi.where())
os.environ.setdefault("REQUESTS_CA_BUNDLE", certifi.where())
urllib.request.urlretrieve(os.environ["SCRIPT_URL"], "/g.py")
os.execv(sys.executable, [sys.executable, "-u", "/g.py", "--grade", os.environ["GRADE"], "--workers", os.environ.get("WORKERS", "2")])

# Bootstrap for the container job: installs CA certificates and packages, downloads gen_images.py and runs it for $GRADE.
import os, subprocess, sys, urllib.request
subprocess.call(["tdnf", "install", "-y", "-q", "ca-certificates"])  # the slim Azure Linux image ships without a CA bundle
subprocess.check_call([sys.executable, "-m", "pip", "install", "-q", "--root-user-action=ignore", "certifi", "pymssql", "azure-storage-blob", "pillow"])
import certifi
os.environ.setdefault("SSL_CERT_FILE", certifi.where())
os.environ.setdefault("REQUESTS_CA_BUNDLE", certifi.where())
urllib.request.urlretrieve(os.environ["SCRIPT_URL"], "/g.py")
os.execv(sys.executable, [sys.executable, "-u", "/g.py", "--grade", os.environ["GRADE"], "--workers", os.environ.get("WORKERS", "2")])

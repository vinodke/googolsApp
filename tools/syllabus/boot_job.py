# Bootstrap for the container job: installs packages, downloads gen_images.py and runs it for $GRADE.
import os, subprocess, sys, urllib.request
subprocess.check_call([sys.executable, "-m", "pip", "install", "-q", "--root-user-action=ignore", "pymssql", "azure-storage-blob", "pillow"])
urllib.request.urlretrieve(os.environ["SCRIPT_URL"], "/g.py")
os.execv(sys.executable, [sys.executable, "-u", "/g.py", "--grade", os.environ["GRADE"], "--workers", os.environ.get("WORKERS", "2")])

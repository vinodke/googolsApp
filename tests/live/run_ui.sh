#!/usr/bin/env bash
# Runs the live page-by-page UI check from Cloud Shell: opens a temporary SQL firewall rule for this shell,
# passes the connection string to the script in memory only, and always removes the rule again.
#   bash tests/live/run.sh
set -uo pipefail
: "${APP:=googols-app}"; : "${RG:=googols-rg}"; : "${SQLSRV:=googols-sql-10298}"
CS=$(az webapp config connection-string list -g "$RG" -n "$APP" --query "[?name=='Googols'].value | [0]" -o tsv 2>/dev/null || true)
[ -z "$CS" ] && CS=$(az webapp config appsettings list -g "$RG" -n "$APP" --query "[?name=='ConnectionStrings__Googols'].value | [0]" -o tsv)
MYIP=$(curl -fsS https://api.ipify.org)
cleanup() { az sql server firewall-rule delete -g "$RG" -s "$SQLSRV" -n CloudShellTemp -o none 2>/dev/null && echo RULE-REMOVED; unset CS; }
trap cleanup EXIT
az sql server firewall-rule create -g "$RG" -s "$SQLSRV" -n CloudShellTemp --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none
LD_LIBRARY_PATH="$HOME/browser-libs/usr/lib:$HOME/browser-libs/usr/lib64" CS="$CS" python3 "$(dirname "$0")/ui_pages.py"

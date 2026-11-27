#!/usr/bin/env bash
# Runs gen_images.py from Cloud Shell with the app's settings (kept in memory only). The SQL server already
# allows Azure services; Cloud Shell needs the temporary firewall rule, which is always removed.
set -uo pipefail
: "${APP:=googols-app}"; : "${RG:=googols-rg}"; : "${SQLSRV:=googols-sql-10298}"
pip install --user -q pymssql azure-storage-blob 2>/dev/null
CSL=$(az webapp config connection-string list -g "$RG" -n "$APP" -o json)
S=$(az webapp config appsettings list -g "$RG" -n "$APP" -o json)
pick() { local v; v=$(jq -r --arg n "$1" '.[]|select(.name==$n).value.value // empty' <<<"$CSL"); [ -z "$v" ] && v=$(jq -r --arg n "ConnectionStrings__$1" '.[]|select(.name==$n).value // empty' <<<"$S"); echo "$v"; }
MYIP=$(curl -fsS https://api.ipify.org)
cleanup() { az sql server firewall-rule delete -g "$RG" -s "$SQLSRV" -n CloudShellTemp -o none 2>/dev/null && echo RULE-REMOVED; }
trap cleanup EXIT
az sql server firewall-rule create -g "$RG" -s "$SQLSRV" -n CloudShellTemp --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none
CS="$(pick Googols)" BLOB_CS="$(pick Blob)" \
AOAI_ENDPOINT=$(jq -r '.[]|select(.name=="AzureOpenAI__Endpoint").value' <<<"$S") \
AOAI_KEY=$(jq -r '.[]|select(.name=="AzureOpenAI__ApiKey").value' <<<"$S") \
python3 "$(dirname "$0")/gen_images.py" "$@"

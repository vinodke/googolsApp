#!/usr/bin/env bash
# Runs a syllabus script that needs the database (and the AI settings) from Cloud Shell:
# opens a temporary SQL firewall rule for this shell, passes secrets in memory only, and always removes the rule.
#   bash run_db.sh build_syllabus.py --apply
#   bash run_db.sh draft_content.py
set -uo pipefail
: "${APP:=googols-app}"; : "${RG:=googols-rg}"; : "${SQLSRV:=googols-sql-10298}"
pip install --user -q pymssql 2>/dev/null
CS=$(az webapp config connection-string list -g "$RG" -n "$APP" --query "[?name=='Googols'].value | [0]" -o tsv 2>/dev/null || true)
[ -z "$CS" ] && CS=$(az webapp config appsettings list -g "$RG" -n "$APP" --query "[?name=='ConnectionStrings__Googols'].value | [0]" -o tsv)
S=$(az webapp config appsettings list -g "$RG" -n "$APP" -o json)
MYIP=$(curl -fsS https://api.ipify.org)
cleanup() { az sql server firewall-rule delete -g "$RG" -s "$SQLSRV" -n CloudShellTemp -o none 2>/dev/null && echo RULE-REMOVED; unset CS S; }
trap cleanup EXIT
az sql server firewall-rule create -g "$RG" -s "$SQLSRV" -n CloudShellTemp --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none
CS="$CS" \
AOAI_ENDPOINT=$(jq -r '.[]|select(.name=="AzureOpenAI__Endpoint").value' <<<"$S") \
AOAI_DEPLOYMENT=$(jq -r '.[]|select(.name=="AzureOpenAI__Deployment").value' <<<"$S") \
AOAI_KEY=$(jq -r '.[]|select(.name=="AzureOpenAI__ApiKey").value' <<<"$S") \
python3 "$(dirname "$0")/$1" "${@:2}"

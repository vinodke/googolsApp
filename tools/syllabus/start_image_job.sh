#!/usr/bin/env bash
# Starts the lesson-infographic job for one class as an Azure Container Instance, so it keeps running
# after Cloud Shell closes. Secrets go in as secure environment variables (not shown in the portal).
#   bash start_image_job.sh 1          # class 1, both mediums
#   az container logs -g googols-rg -n googols-images-1 --follow     # watch it
#   az container delete -g googols-rg -n googols-images-1 --yes      # clean up when done (it stops by itself)
set -euo pipefail
GRADE=${1:?class number}; WORKERS=${WORKERS:-2}
: "${APP:=googols-app}"; : "${RG:=googols-rg}"; : "${ST:=googolsst10298}"; : "${LOC:=centralindia}"
NAME="googols-images-$GRADE"
CSL=$(az webapp config connection-string list -g "$RG" -n "$APP" -o json)
S=$(az webapp config appsettings list -g "$RG" -n "$APP" -o json)
pick() { local v; v=$(jq -r --arg n "$1" '.[]|select(.name==$n).value.value // empty' <<<"$CSL"); [ -z "$v" ] && v=$(jq -r --arg n "ConnectionStrings__$1" '.[]|select(.name==$n).value // empty' <<<"$S"); echo "$v"; }
az provider register -n Microsoft.ContainerInstance -o none
sas() { az storage blob generate-sas --account-name "$ST" -c deploy -n "$1" --permissions r --expiry "$(date -u -d '+7 days' +%Y-%m-%dT%H:%MZ)" --full-uri -o tsv --only-show-errors; }
for f in gen_images.py boot_job.py; do
  az storage blob upload --account-name "$ST" -c deploy -n "jobs/$f" -f "$(dirname "$0")/$f" --overwrite -o none --only-show-errors
done
URL=$(sas jobs/gen_images.py); BOOT=$(sas jobs/boot_job.py)
read -r -d '' CMD <<'X' || true
python3 -u -c "exec(__import__('urllib.request').request.urlopen(__import__('os').environ['BOOT_URL']).read())"
X
az container delete -g "$RG" -n "$NAME" --yes -o none 2>/dev/null || true
az container create -g "$RG" -n "$NAME" -l "$LOC" --image mcr.microsoft.com/azurelinux/base/python:3.12 --os-type Linux --cpu 1 --memory 1.5 --restart-policy Never \
  --command-line "$CMD" --environment-variables "GRADE=$GRADE" "WORKERS=$WORKERS" \
  --secure-environment-variables "CS=$(pick Googols)" "BLOB_CS=$(pick Blob)" "SCRIPT_URL=$URL" "BOOT_URL=$BOOT" \
    "AOAI_ENDPOINT=$(jq -r '.[]|select(.name=="AzureOpenAI__Endpoint").value' <<<"$S")" \
    "AOAI_KEY=$(jq -r '.[]|select(.name=="AzureOpenAI__ApiKey").value' <<<"$S")" \
  -o none
unset S CSL URL BOOT
echo "Started $NAME. Logs: az container logs -g $RG -n $NAME"

#!/usr/bin/env bash
# Reads the AI settings from the web app (kept in this shell's memory only) and runs the extractor.
set -uo pipefail
: "${APP:=googols-app}"; : "${RG:=googols-rg}"
pip install --user -q pypdf pypdfium2 pillow 2>/dev/null
S=$(az webapp config appsettings list -g "$RG" -n "$APP" -o json)
AOAI_ENDPOINT=$(jq -r '.[]|select(.name=="AzureOpenAI__Endpoint").value' <<<"$S") \
AOAI_DEPLOYMENT=$(jq -r '.[]|select(.name=="AzureOpenAI__Deployment").value' <<<"$S") \
AOAI_KEY=$(jq -r '.[]|select(.name=="AzureOpenAI__ApiKey").value' <<<"$S") \
python3 "$(dirname "$0")/${SCRIPT:-extract_samagra.py}" "$@"
unset S

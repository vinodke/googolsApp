#!/usr/bin/env bash
# Builds Googols and deploys it to the Linux App Service using "run from package":
# the zip goes to Blob Storage and the app reads it via a read-only SAS URL.
# (az webapp deploy failed from Cloud Shell with a credential error on 26 Nov 2026; this path avoids Kudu.)
#
# Run from the repo root in Azure Cloud Shell (needs .NET 10 SDK, Node 22, az):
#   APP=googols-app RG=googols-rg ST=googolsst10298 bash infrastructure/azure/deploy.sh
set -euo pipefail
: "${APP:=googols-app}"; : "${RG:=googols-rg}"; : "${ST:=googolsst10298}"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

( cd frontend/googols-web && npm install --no-audit --no-fund && npm run build && npm test )
dotnet test Googols.slnx -c Release
rm -rf out && dotnet publish src/Googols.WebApi -c Release -o out
mkdir -p out/wwwroot && cp -r frontend/googols-web/dist/. out/wwwroot/
( cd out && rm -f ../app.zip && zip -qr ../app.zip . )

KEY=$(az storage account keys list -g "$RG" -n "$ST" --query [0].value -o tsv)
BLOB="app-$(date -u +%Y%m%d%H%M).zip"
az storage blob upload --account-name "$ST" --account-key "$KEY" -c deploy -n "$BLOB" -f app.zip --overwrite -o none
SAS=$(az storage blob generate-sas --account-name "$ST" --account-key "$KEY" -c deploy -n "$BLOB" \
  --permissions r --expiry "$(date -u -d '+1 year' +%Y-%m-%dT%H:%MZ)" --https-only -o tsv)
az webapp config appsettings set -g "$RG" -n "$APP" -o none \
  --settings "WEBSITE_RUN_FROM_PACKAGE=https://$ST.blob.core.windows.net/deploy/$BLOB?$SAS"
az webapp restart -g "$RG" -n "$APP"
sleep 45
curl -fsS "https://$APP.azurewebsites.net/health" && echo " <- https://$APP.azurewebsites.net is live"

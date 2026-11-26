#!/usr/bin/env bash
# Builds Googols and deploys it to a Linux App Service. Run from the repo root in Azure Cloud Shell.
# Usage: APP=<webapp-name> RG=googols-rg bash infrastructure/azure/deploy.sh
set -euo pipefail
: "${APP:?set APP}"; : "${RG:=googols-rg}"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

( cd frontend/googols-web && npm ci --no-audit --no-fund && npm run build )
rm -rf out && dotnet publish src/Googols.WebApi -c Release -o out
mkdir -p out/wwwroot && cp -r frontend/googols-web/dist/. out/wwwroot/
( cd out && rm -f ../app.zip && zip -qr ../app.zip . )
az webapp deploy -g "$RG" -n "$APP" --src-path app.zip --type zip
echo "Deployed: https://$APP.azurewebsites.net"

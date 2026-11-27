#!/usr/bin/env bash
# Adds (optional) and applies EF Core migrations to the Azure SQL database from Cloud Shell.
# Opens a temporary firewall rule for this shell's IP and always removes it again.
#   NAME=MyChange bash infrastructure/azure/migrate.sh   # add migration MyChange, check it, apply it
#   bash infrastructure/azure/migrate.sh                 # apply pending migrations only
# Refuses to apply a migration whose Up() drops tables or columns unless ALLOW_DROPS=1.
set -euo pipefail
: "${APP:=googols-app}"; : "${RG:=googols-rg}"; : "${SQLSRV:=googols-sql-10298}"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
EF=(-p src/Googols.Infrastructure -s src/Googols.WebApi -c GoogolsDbContext)

if [ -n "${NAME:-}" ]; then
  dotnet ef migrations add "$NAME" "${EF[@]}"
  FILE=$(ls src/Googols.Infrastructure/Persistence/Migrations/*_"$NAME".cs)
  UP=$(awk '/protected override void Up/{f=1} /protected override void Down/{f=0} f' "$FILE")
  echo "Migration $NAME: CreateTable=$(grep -c CreateTable <<<"$UP" || true) AddColumn=$(grep -c AddColumn <<<"$UP" || true) Drops=$(grep -cE 'DropTable|DropColumn' <<<"$UP" || true)"
  if grep -qE 'DropTable|DropColumn' <<<"$UP" && [ "${ALLOW_DROPS:-0}" != 1 ]; then
    echo "Up() drops data. Review $FILE, then rerun with ALLOW_DROPS=1." >&2; exit 1
  fi
fi

CS=$(az webapp config connection-string list -g "$RG" -n "$APP" --query "[?name=='Googols'].value | [0]" -o tsv 2>/dev/null || true)
if [ -z "$CS" ]; then
  CS=$(az webapp config appsettings list -g "$RG" -n "$APP" --query "[?name=='ConnectionStrings__Googols'].value | [0]" -o tsv)
fi
MYIP=$(curl -fsS https://api.ipify.org)
cleanup() { az sql server firewall-rule delete -g "$RG" -s "$SQLSRV" -n CloudShellTemp -o none 2>/dev/null && echo RULE-REMOVED; unset CS; }
trap cleanup EXIT
az sql server firewall-rule create -g "$RG" -s "$SQLSRV" -n CloudShellTemp --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none
dotnet ef database update "${EF[@]}" --connection "$CS"

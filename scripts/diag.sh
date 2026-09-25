#!/usr/bin/env bash
DIR=$(ls -d "/d/document/WorkJira/福建数据迁移/"*Migrate_Desk)
LOGDIR="$APPDATA/com.fjyxhr.migrate.desk/logs"
rm -f "$LOGDIR"/*.log
cd "$DIR/src-tauri/target/debug"
rm -f host.log
( ./fjyxhr-migrate-desk.exe > host.log 2>&1 ) &
sleep 10
echo "=== host ==="
tasklist //FI "IMAGENAME eq fjyxhr-migrate-desk.exe" //FO CSV //NH 2>/dev/null | tr -d '"'
echo "=== sidecar 进程数 ==="
tasklist //FI "IMAGENAME eq fjyxhr-migrate.exe" //FO CSV //NH 2>/dev/null | tr -d '"'
echo "=== host.log ==="
cat host.log
echo "=== sidecar 日志 ==="
cat "$LOGDIR"/*.log 2>/dev/null
taskkill //F //IM fjyxhr-migrate-desk.exe >/dev/null 2>&1
taskkill //F //IM fjyxhr-migrate.exe >/dev/null 2>&1
echo "cleaned"

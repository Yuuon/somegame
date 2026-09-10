#!/usr/bin/env bash
# SomeGame 远端 Linux 部署脚本（幂等，可重复执行）
# 用法: bash install-somegame.sh --repo <源码路径> [--rid linux-x64] [--port 8080]
set -euo pipefail

REPO=""
RID="linux-x64"
PORT="8080"
USER="somegame"
APP_DIR="/opt/somegame/app"
ENV_DIR="/etc/somegame"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo) REPO="$2"; shift 2;;
    --rid)  RID="$2";  shift 2;;
    --port) PORT="$2"; shift 2;;
    *) echo "未知参数: $1"; exit 1;;
  esac
done

[[ -z "$REPO" ]] && { echo "[FAIL] 缺少 --repo 源码路径"; exit 1; }
[[ -f "$REPO/src/SomeGame.Server/SomeGame.Server.csproj" ]] || { echo "[FAIL] $REPO 不是 SomeGame 源码目录"; exit 1; }

echo "[1/6] 检查 .NET SDK"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "[FAIL] 未安装 dotnet SDK，请先按 docs/deploy-remote-linux.md §2 安装（含 10.0）"
  exit 1
fi
dotnet --list-sdks | grep -q '^10\.' || { echo "[FAIL] 缺少 .NET 10 SDK"; exit 1; }

echo "[2/6] 发布（self-contained, $RID）"
dotnet publish "$REPO/src/SomeGame.Server/SomeGame.Server.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=false \
  -o "$APP_DIR"

echo "[3/6] 创建用户与目录"
if ! id "$USER" >/dev/null 2>&1; then
  useradd --system --no-create-home --shell /usr/sbin/nologin "$USER"
fi
install -d -o "$USER" -g "$USER" "$APP_DIR" "$ENV_DIR"
chown -R "$USER:$USER" "$APP_DIR"

echo "[4/6] 写入环境变量"
cat > "$ENV_DIR/somegame.env" <<EOF
ASPNETCORE_ENVIRONMENT=Production
SOMEGAME_PORT=$PORT
SOMEGAME_DECISION_MS=60000
EOF
chown "$USER:$USER" "$ENV_DIR/somegame.env"
chmod 640 "$ENV_DIR/somegame.env"

echo "[5/6] 安装 systemd 服务"
cat > /etc/systemd/system/somegame.service <<EOF
[Unit]
Description=SomeGame authoritative server
After=network.target

[Service]
Type=simple
User=$USER
Group=$USER
WorkingDirectory=$APP_DIR
EnvironmentFile=$ENV_DIR/somegame.env
ExecStart=$APP_DIR/SomeGame.Server
Restart=on-failure
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now somegame
sleep 1

echo "[6/6] 验证"
if curl -fsS "http://127.0.0.1:$PORT/api/health" >/dev/null 2>&1; then
  echo "[OK] health 通过: http://127.0.0.1:$PORT/api/health"
else
  echo "[FAIL] health 未通过，日志如下："
  journalctl -u somegame -n 30 --no-pager || true
  exit 1
fi

echo
echo "完成。请确认防火墙放行端口 $PORT，随后浏览器访问 http://<服务器IP>:$PORT"
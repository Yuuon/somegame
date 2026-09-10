# 远端 Linux 服务器部署文档（供 LLM agent 自主执行）

> 目标：在一台干净的 Linux 服务器（Ubuntu/Debian 系优先，也兼容其他发行版）上部署
> SomeGame 服务器，使其可通过 `http://<服务器IP>:<端口>` 供玩家浏览器访问。
> 本文面向自动化的 LLM agent：每条命令都给出**预期输出**与**验证步骤**，执行任何一步失败即停止排查。

## 0. 适用环境与假设

| 项 | 说明 |
| --- | --- |
| 系统 | Linux x86_64 或 aarch64（Ubuntu 20.04+/Debian 11+ 已验证路径） |
| 运行方式 | **自包含发布**（self-contained，无需服务器装 .NET 运行时）；构建仍需 .NET SDK |
| 端口 | 默认 `8080`（可通过环境变量改） |
| 进程管理 | systemd（Ubuntu/Debian 默认） |
| 用户 | 专用系统用户 `somegame`，服务以最小权限运行 |

## 1. 前置检查

```bash
# 系统与架构
uname -m            # 期望: x86_64 或 aarch64

# .NET SDK 是否可用（构建需要）
dotnet --list-sdks || echo "NO_SDK"
```
- 若输出 `NO_SDK`，按 §2 安装 SDK。
- 若 `uname -m` 非 x86_64/aarch64，请在发布阶段改用对应 RID。

## 2. 安装 .NET SDK（如缺失）

方式 A（推荐，官方脚本，可装任意版本到 `$HOME/.dotnet`）：

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/share/dotnet
ln -s /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
dotnet --list-sdks   # 期望包含 10.0.x
```

方式 B（Ubuntu 24.04 官方源）：
```bash
apt-get update
apt-get install -y dotnet-sdk-10.0
dotnet --list-sdks
```
> 非 Ubuntu 时优先用方式 A。若公司内网无外网，需预置 SDK 或改投框架依赖发布（见 §6 备注）。

## 3. 获取代码并发布

```bash
git clone <仓库地址> /opt/src/somegame        # 或 scp/rsync 上传
cd /opt/src/somegame

# 发布自包含产物（RID 按 uname -m：x86_64 用 linux-x64，aarch64 用 linux-arm64）
dotnet publish src/SomeGame.Server/SomeGame.Server.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o /opt/somegame/app

ls /opt/somegame/app/SomeGame.Server   # 期望存在可执行文件
ls /opt/somegame/app/wwwroot/index.html # 期望存在前端静态页
```

## 4. 目录与权限

```bash
# 专用用户
useradd --system --no-create-home --shell /usr/sbin/nologin somegame 2>/dev/null || true
install -d -o somegame -g somegame /opt/somegame/app
chown -R somegame:somegame /opt/somegame/app

# 配置目录
install -d -o somegame -g somegame /etc/somegame
```

## 5. 环境变量与配置

服务通过 `/etc/somegame/somegame.env` 注入配置：

```bash
cat > /etc/somegame/somegame.env <<'EOF'
ASPNETCORE_ENVIRONMENT=Production
SOMEGAME_PORT=8080
SOMEGAME_DECISION_MS=60000
EOF
chown somegame:somegame /etc/somegame/somegame.env
chmod 640 /etc/somegame/somegame.env
```

| 变量 | 默认 | 说明 |
| --- | --- | --- |
| `SOMEGAME_PORT` | `5123` | 监听端口（务必与防火墙一致） |
| `SOMEGAME_DECISION_MS` | `60000` | 真人决策超时（毫秒） |

## 6. systemd 服务

```bash
cat > /etc/systemd/system/somegame.service <<'EOF'
[Unit]
Description=SomeGame authoritative server
After=network.target

[Service]
Type=simple
User=somegame
Group=somegame
WorkingDirectory=/opt/somegame/app
EnvironmentFile=/etc/somegame/somegame.env
ExecStart=/opt/somegame/app/SomeGame.Server
Restart=on-failure
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now somegame
systemctl status somegame --no-pager   # 期望 Active: active (running)
```

## 7. 防火墙

```bash
# ufw（Ubuntu）
ufw allow 8080/tcp
ufw status verbose   # 期望 8080/tcp ALLOW

# 或 firewalld（CentOS/Rocky）
# firewall-cmd --permanent --add-port=8080/tcp && firewall-cmd --reload
```

## 8. 验证

```bash
# 健康检查（预期: {"ok":true,"rooms":0}）
curl -fsS http://127.0.0.1:8080/api/health

# 静态页面（预期返回 200 且含 "SomeGame"）
curl -fsS -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8080/

# WebSocket 端点（预期: HTTP 426 升级失败提示，说明 /ws 已被正确映射）
curl -fsS -o /dev/null -w '%{http_code}\n' -H "Connection: Upgrade" -H "Upgrade: websocket" http://127.0.0.1:8080/ws

# 外网可达（在服务器上确认监听地址为 0.0.0.0）
ss -ltnp | grep 8080   # 期望 0.0.0.0:8080 处于 LISTEN
```

**浏览器最终验收**：访问 `http://<服务器IP>:8080`，创建 4 人房间并“开始游戏”（空位自动补机器人），能看到对局画面与日志，即部署成功。

## 9. 可选：Nginx 反向代理 + HTTPS

```bash
apt-get install -y nginx
cat > /etc/nginx/sites-available/somegame <<'EOF'
server {
    listen 80;
    server_name _;
    client_max_body_size 1m;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;      # WebSocket 必需
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 300s;
    }
}
EOF
ln -s /etc/nginx/sites-available/somegame /etc/nginx/sites-enabled/somegame
nginx -t && systemctl reload nginx
# 之后把 §7 防火墙改为仅放行 80/443
```

## 10. 更新与回滚

```bash
# 更新
cd /opt/src/somegame && git pull
dotnet publish ...（同 §3，覆盖 /opt/somegame/app）
systemctl restart somegame
curl -fsS http://127.0.0.1:8080/api/health   # 确认 OK

# 回滚：保留旧版本目录
mv /opt/somegame/app /opt/somegame/app.old   # 若需回退
# 或利用 git 切回旧 commit 重新 publish
```

## 11. 故障排查

| 症状 | 排查 |
| --- | --- |
| 服务起不来 | `journalctl -u somegame -n 50 --no-pager`；确认端口未被占用 `ss -ltnp \| grep <端口>` |
| 健康检查不通 | `curl -v http://127.0.0.1:8080/api/health`；确认 systemd 状态、env 文件端口一致 |
| 外网访问不了 | `ss -ltnp` 看监听是否 `0.0.0.0`；防火墙是否放行；云厂商安全组是否放行 |
| 浏览器 WS 断开 | Nginx 场景需确认 `Upgrade/Connection` 头已透传（见 §9） |

## 12. 安全注意事项

- 服务无鉴权：**端口不要直接暴露公网**，建议置于 Nginx 反代 + 防火墙白名单/内网访问，或后续自行加鉴权。
- 以专用低权限用户 `somegame` 运行，不要用 root。
- `/etc/somegame/somegame.env` 权限 `640` 且属主为 `somegame`。
- 定期 `apt-get upgrade` 与 `dotnet` 补丁更新；如不信任代码，可在 clone 后 `git verify-tag` 或校验提交签名。

## 13. 一键脚本

仓库内提供 `deploy/install-somegame.sh`，可替代 §3-§8 手动步骤：

```bash
bash deploy/install-somegame.sh --repo /opt/src/somegame --rid linux-x64 --port 8080
# 参数：--repo 源码路径（必填），--rid 运行标识（默认 linux-x64），--port 端口（默认 8080）
```

脚本为幂等设计，可重复执行；每步失败即退出并打印原因。
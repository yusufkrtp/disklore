#!/usr/bin/env bash
# Prepares a cloud session. Local sessions exit immediately.
[ "${CLAUDE_CODE_REMOTE:-}" = "true" ] || exit 0
cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0
exec >>/tmp/cloud-setup.log 2>&1
git config user.name "Yusuf"
git config user.email "208495344+yusufkrtp@users.noreply.github.com"
# --- project steps go here. Each must be idempotent and must not abort the session. ---
if ! command -v dotnet >/dev/null 2>&1; then
  if [ ! -x "$HOME/.dotnet/dotnet" ]; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet" || echo "dotnet install failed"
  fi
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
  if [ -n "${CLAUDE_ENV_FILE:-}" ] && ! grep -qs 'DOTNET_ROOT' "$CLAUDE_ENV_FILE"; then
    echo 'export DOTNET_ROOT=$HOME/.dotnet' >> "$CLAUDE_ENV_FILE"
    echo 'export PATH=$HOME/.dotnet:$PATH' >> "$CLAUDE_ENV_FILE"
  fi
fi
if command -v dotnet >/dev/null 2>&1; then
  dotnet restore src/DiskLore.Core/DiskLore.Core.csproj || echo "core restore failed"
  dotnet restore tests/DiskLore.Tests/DiskLore.Tests.csproj || echo "tests restore failed"
fi
exit 0

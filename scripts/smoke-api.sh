#!/usr/bin/env bash
set -euo pipefail

url="http://127.0.0.1:5086"
export ASPNETCORE_URLS="$url"
log_file="$(mktemp)"
response_file="$(mktemp)"

cleanup() {
  kill "$app_pid" 2>/dev/null || true
  wait "$app_pid" 2>/dev/null || true
  rm -f "$log_file" "$response_file"
}
trap cleanup EXIT

dotnet run --project src/Arcanum.Demo.Api --configuration Release --no-build --no-launch-profile >"$log_file" 2>&1 &
app_pid=$!

ready=0
for attempt in $(seq 1 30); do
  if curl --silent --fail "$url/health" >/dev/null; then
    ready=1
    break
  fi
  sleep 1
done

if [ "$ready" -ne 1 ]; then
  cat "$log_file"
  echo "Demo API did not become ready" >&2
  exit 1
fi

curl --silent --show-error --fail \
  --header 'Content-Type: application/json' \
  --data '{"value":"  hello  "}' \
  "$url/capabilities/demo.publish/execute" > "$response_file"

python3 - "$response_file" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as stream:
    result = json.load(stream)
assert result["terminal"] == "@complete", result
assert result["steps"] == 3, result
assert result["data"]["text"] == "HELLO", result
assert result["data"]["sameScopedResource"] is True, result
PY

status="$(curl --silent --show-error \
  --output "$response_file" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"value":""}' \
  "$url/capabilities/demo.publish/execute")"
[ "$status" = "422" ] || { cat "$response_file"; exit 1; }

curl --silent --show-error --fail \
  --header 'Content-Type: application/json' \
  --data '{"value":"ok","iterations":4}' \
  "$url/capabilities/demo.loop/execute" > "$response_file"

python3 - "$response_file" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as stream:
    result = json.load(stream)
assert result["terminal"] == "@complete", result
assert result["steps"] == 4, result
assert result["data"]["count"] == 4, result
PY

curl --silent --show-error --fail \
  --header 'Content-Type: application/json' \
  --data '{"value":"custom"}' \
  "$url/capabilities/demo.activity/execute" > "$response_file"

python3 - "$response_file" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as stream:
    result = json.load(stream)
assert result["terminal"] == "@complete", result
assert result["data"]["value"] == "custom", result
PY

curl --silent --show-error --fail \
  --header 'Content-Type: application/json' \
  --data '{"payload":{"value":"  nested  "}}' \
  "$url/capabilities/demo.nested/execute" > "$response_file"

python3 - "$response_file" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as stream:
    result = json.load(stream)
assert result["terminal"] == "@complete", result
assert result["data"]["text"] == "NESTED", result
PY

status="$(curl --silent --show-error \
  --output "$response_file" --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data '{"value":"missing nested payload"}' \
  "$url/capabilities/demo.nested/execute")"
[ "$status" = "400" ] || { cat "$response_file"; exit 1; }

echo "HTTP smoke succeeded: valid, invalid, loop, arbitrary Process name, nested contract, invalid contract."


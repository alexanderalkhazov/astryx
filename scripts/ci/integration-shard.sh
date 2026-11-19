#!/usr/bin/env bash
# Runs one shard of the integration tests (TASKS.md T-001): test classes, sorted by name, dealt round-robin into
# <count> shards. Each class starts its own Postgres container, so whole classes are the unit of work.
#   scripts/ci/integration-shard.sh <index> <count> [extra dotnet test args…]     (index is 0-based; needs a Release build)
set -euo pipefail

index=$1
count=$2
shift 2
project=tests/Astryx.IntegrationTests

classes=$(dotnet test "$project" -c Release --no-build --list-tests 2>/dev/null \
  | grep -E '^[[:space:]]+Astryx\.IntegrationTests\.' \
  | sed -E 's/^[[:space:]]+//; s/\(.*//' \
  | awk -F. 'BEGIN { OFS = "." } { NF--; print }' \
  | sort -u)

if [ -z "$classes" ]; then
  echo "No integration test classes found: build $project in Release first." >&2
  exit 1
fi

# The trailing dot keeps ProposalTests from also matching ProposalSignalTurnoverTests.
filter=$(echo "$classes" | awk -v i="$index" -v n="$count" '(NR - 1) % n == i { printf "%sFullyQualifiedName~%s.", (c++ ? "|" : ""), $0 }')
echo "Shard $((index + 1))/$count: $(echo "$filter" | tr '|' '\n' | wc -l | tr -d ' ') of $(echo "$classes" | wc -l | tr -d ' ') classes"
dotnet test "$project" -c Release --no-build --filter "$filter" "$@"

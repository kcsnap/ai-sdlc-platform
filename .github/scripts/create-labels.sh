#!/usr/bin/env bash
# Creates all AI SDLC labels in a GitHub repository.
# Usage: GITHUB_TOKEN=<pat> ./create-labels.sh <owner/repo>
#
# Requires: gh CLI (authenticated), or set GITHUB_TOKEN env var.
# Safe to run multiple times — existing labels are updated in place.

set -euo pipefail

REPO="${1:-}"
if [[ -z "$REPO" ]]; then
  echo "Usage: $0 <owner/repo>" >&2
  exit 1
fi

create_or_update_label() {
  local name="$1"
  local color="$2"
  local description="$3"

  if gh label list --repo "$REPO" --json name --jq '.[].name' | grep -qxF "$name"; then
    gh label edit "$name" --repo "$REPO" --color "$color" --description "$description"
    echo "Updated: $name"
  else
    gh label create "$name" --repo "$REPO" --color "$color" --description "$description"
    echo "Created: $name"
  fi
}

# ── Workflow lifecycle labels ────────────────────────────────────────────────
create_or_update_label "ai-sdlc:triage"                  "e4e669" "New AI SDLC request, not yet processed"
create_or_update_label "ai-sdlc:awaiting-brief-approval" "f9d0c4" "Brief posted — waiting for /approve-brief or /request-changes"
create_or_update_label "ai-sdlc:brief-approved"          "c2e0c6" "Brief approved, business analysis in progress"
create_or_update_label "ai-sdlc:analysing"               "bfd4f2" "Business Analyst is analysing the change"
create_or_update_label "ai-sdlc:implementing"            "d4c5f9" "Coder is implementing the change"
create_or_update_label "ai-sdlc:awaiting-human-review"   "fef2c0" "Awaiting human review before proceeding"
create_or_update_label "ai-sdlc:ready-to-release"        "0e8a16" "Approved for production release"
create_or_update_label "ai-sdlc:released"                "006b75" "Change released to production"
create_or_update_label "ai-sdlc:stopped"                 "b60205" "Workflow stopped — human intervention required"
create_or_update_label "ai-sdlc:failed"                  "ee0701" "Workflow failed — see run logs"

# ── Risk labels ──────────────────────────────────────────────────────────────
create_or_update_label "ai-sdlc:risk-low"    "c2e0c6" "Risk assessment: Low — eligible for auto-deploy"
create_or_update_label "ai-sdlc:risk-medium" "fef2c0" "Risk assessment: Medium — human review recommended"
create_or_update_label "ai-sdlc:risk-high"   "b60205" "Risk assessment: High — human review required"

echo ""
echo "All AI SDLC labels applied to $REPO."

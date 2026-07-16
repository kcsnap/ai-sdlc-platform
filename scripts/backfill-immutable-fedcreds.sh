#!/usr/bin/env bash
# F5 backfill: add the IMMUTABLE-subject federated credential to every existing user-app deploy SP.
#
# GitHub's OIDC sub claim switched to repo:{owner}@{ownerId}/{repo}@{repoId}:ref:... for the
# yorrixx-apps org (observed 2026-07-16, AADSTS700213 on fresh-w1-bikeshop). New provisions pin
# both formats (#244); every EXISTING app's SP has only the classic subject and will fail its next
# deploy until this backfill runs.
#
# Requirements: az login as an identity that can write the sp-userapp-* app registrations
# (Kenneth interactive, or the provisioner UAMI), gh auth with repo read on yorrixx-apps.
# Idempotent: skips SPs that already have the immutable credential; skips SPs whose repo is gone.
set -euo pipefail

ORG="yorrixx-apps"
ORG_ID=$(gh api "orgs/$ORG" --jq '.id')
echo "org $ORG id=$ORG_ID"

added=0; skipped=0; norepo=0
# Every user-app deploy identity follows the sp-userapp-{id8} convention.
az ad app list --filter "startswith(displayName,'sp-userapp-')" --query "[].{id:id,name:displayName}" -o tsv |
while IFS=$'\t' read -r APP_OBJ NAME; do
  ID8="${NAME#sp-userapp-}"
  REPO="user-app-$ID8"

  REPO_ID=$(gh api "repos/$ORG/$REPO" --jq '.id' 2>/dev/null) || { echo "$NAME: repo $REPO missing — skip"; norepo=$((norepo+1)); continue; }
  SUBJECT="repo:$ORG@$ORG_ID/$REPO@$REPO_ID:ref:refs/heads/main"

  if az ad app federated-credential list --id "$APP_OBJ" --query "[?subject=='$SUBJECT'] | length(@)" -o tsv | grep -qv '^0$'; then
    echo "$NAME: immutable cred already present — skip"; skipped=$((skipped+1)); continue
  fi

  az ad app federated-credential create --id "$APP_OBJ" --parameters "{
    \"name\": \"gh-$REPO-main-i\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"$SUBJECT\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }" --query name -o tsv
  echo "$NAME: immutable cred ADDED ($SUBJECT)"
  added=$((added+1))
done

echo "done: added=$added skipped=$skipped repo-missing=$norepo"

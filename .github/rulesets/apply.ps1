#!/usr/bin/env pwsh
# Applies (or updates) every ruleset JSON in this directory on kcsnap/ai-sdlc-platform.
# Each ruleset is identified by its top-level "name" field; existing rulesets with a
# matching name are updated in place, new ones are created.
# Requires: gh CLI authenticated with admin:repo scope.

$ErrorActionPreference = 'Stop'

$repo     = 'kcsnap/ai-sdlc-platform'
$existing = gh api "repos/$repo/rulesets" | ConvertFrom-Json

Get-ChildItem -Path $PSScriptRoot -Filter '*.json' | ForEach-Object {
    $path = $_.FullName
    $name = (Get-Content $path -Raw | ConvertFrom-Json).name

    if (-not $name) {
        throw "Ruleset $($_.Name) is missing a top-level 'name' field"
    }

    $match = $existing | Where-Object { $_.name -eq $name }

    if ($match) {
        Write-Host "Updating ruleset '$name' (id=$($match.id))..."
        gh api "repos/$repo/rulesets/$($match.id)" --method PUT --input $path
    } else {
        Write-Host "Creating ruleset '$name'..."
        gh api "repos/$repo/rulesets" --method POST --input $path
    }
}

Write-Host "Done."

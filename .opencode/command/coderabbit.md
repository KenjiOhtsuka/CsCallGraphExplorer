---
description: Process CodeRabbit PR review comments — evaluate, fix if reasonable, reply to each.
---

## Task

Process CodeRabbit review comments on PR `$ARGUMENTS` from the repository `KenjiOhtsuka/CsCallGraphExplorer`.

## Steps

### 1. Fetch PR and review data

```powershell
gh pr view $ARGUMENTS --json title,body,additions,deletions,files,reviews,comments
gh api repos/KenjiOhtsuka/CsCallGraphExplorer/pulls/$ARGUMENTS/comments
```

Review the output to identify all individual inline comments from `coderabbitai[bot]`.

### 2. For each inline comment, evaluate and act

For each CodeRabbit review comment:

1. **Read the current code** at the file and line referenced
2. **Evaluate reasonability** — is the issue real? Does the fix align with project conventions?
3. **If reasonable**: fix the code, run `dotnet build`, then `dotnet test --no-build` to verify
4. **If not reasonable**: prepare a clear explanation (design choice, performance tradeoff, false positive)

### 3. Reply to every comment

Post a GitHub reply using:

```powershell
$j = @{body = "<reply-text>"; in_reply_to = <comment-id>} | ConvertTo-Json -Compress
$tf = [System.IO.Path]::GetTempFileName()
Set-Content -LiteralPath $tf -Value $j -Encoding Ascii -NoNewline
gh api repos/KenjiOhtsuka/CsCallGraphExplorer/pulls/$ARGUMENTS/comments --input $tf
Remove-Item $tf
```

Each reply must explicitly say:
- **If fixed**: "Addressed in commit `<sha>`." + summary of what changed
- **If skipped**: explanation of why (design choice, false positive, etc.)

### 4. Commit and push

```powershell
git add -A
git commit -m "fix: address CodeRabbit review comments on PR #$ARGUMENTS"
git push
```

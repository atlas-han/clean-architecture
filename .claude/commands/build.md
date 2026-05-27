---
description: Run dotnet build with compact output and highlight errors / warnings.
argument-hint: [optional: project path]
---

Run `dotnet build` and report the result.

Argument: `$ARGUMENTS` (optional project path; default is the solution).

Use:
```bash
dotnet build $ARGUMENTS --nologo --verbosity minimal
```

Then:
- On success: report `build: PASS` with elapsed time and any warning count (don't dump warning bodies unless count > 0).
- On failure: list each error as `file:line — <message>`. Group by project. Suggest the most likely cause for the top error (especially: C# 9 syntax violations, missing ProjectReference, missing `using`).

Don't try to fix build errors here — this command is for fast feedback. The user (or the next agent in the cycle) decides on fixes.

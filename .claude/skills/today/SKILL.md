---
name: today
description: Daily TL;DR for the pulse project - where we are, how to start the lab, and what to do next. Use when René types /today, starts the day, or asks where things stand.
---

# Today

Give René a short start-of-day brief. Gather the facts first, then write. Never guess a fact you can check; never start work he hasn't asked for.

## Gather

Run these from the repo root (in parallel where you can):

- `git status --short --branch` and `git log --oneline -8`: branch, unpushed or uncommitted work, what landed last.
- `gh issue list --state all --limit 30 --json number,title,state,labels`: what's open and what's ready (`ready-for-agent`).
- `gh issue view 1 --comments`: the parent issue; its latest comment is the last handover.
- The newest tour is the first link in `docs/tours/index.html` (same-day names don't sort by time). Read its "Decisions for you" section: decisions still waiting on René.
- `git worktree list`: other branches in progress (e.g. a worktree for one issue), and `git -C <path> status --short --branch` for each.
- `scripts/pulse-status`: whether the lab is running, when it last synced. If Docker isn't reachable, say so; don't try to fix it.
- Project memory (if this machine has any) for notes such as an open question.

## Write

Keep it to one screen. Three headed parts, in this order:

1. **TL;DR**: two or three sentences. What phase we're in, what was finished last (with dates from git), what's open.
2. **Start up**: only the steps needed *right now*, from the real state you saw. If everything is already up, say so in one line and give the watch command. Otherwise the exact commands, e.g. `docker compose up -d` then `scripts/pulse-watch`. Mention once-per-machine steps (`.env`, reseeding AX) only if something shows they're missing.
3. **Next steps**: two or three concrete suggestions, best first, each one line with why. Prefer the next `ready-for-agent` issue and any decision René must make before it can start; put that decision first as a question he can answer in a word.

Plain words, no tables. End by asking which next step to take.

# NCode.Coordination

Coordination for the nodes of a cluster, for .NET: who is alive, who owns what, what happened once, what changed, and
how much was used. One programming model over interchangeable providers, each speaking a standard protocol, each
declaring the guarantees it keeps and proving them against one conformance kit.

NCode.Coordination is not a database, a cache or a message broker. It holds the small, often short-lived state that
the nodes of one deployment must agree on, and leaves durable data to a database and durable messaging to a broker.

| Primitive             | Answers                                                          | Familiar from                               |
| --------------------- | ---------------------------------------------------------------- | ------------------------------------------- |
| Sessions & membership | Which nodes are alive, where they are, and what dies with a node | ZooKeeper sessions, Consul catalog, Orleans |
| Partition ownership   | Which node owns each partition of a workload                     | Akka cluster sharding, rendezvous hashing   |
| Leases & semaphores   | Who owns a resource now, proven by a fencing token               | Chubby locks, etcd leases                   |
| Elections & claims    | Which node leads a role, or runs this interval's pass            | Akka cluster singleton, ZooKeeper recipes   |
| Records               | A small versioned value, changed by compare-and-swap             | etcd revisions, Consul modify index         |
| Transactions          | Several records changed together, on conditions                  | etcd transactions, ZooKeeper multi          |
| Sequences             | Unique, increasing numbers across the cluster                    | ZooKeeper sequential nodes                  |
| Occurrences           | Whether something already happened, at most once                 | Replay caches                               |
| Sets                  | Which members belong to a key, each with its own expiry          | Secondary indexes                           |
| Counters              | How much was used in a window, checked against limits            | Rate limiters                               |
| Notifications         | What changed, with an explicit signal when a node missed it      | etcd watches, ZooKeeper watches             |

The design is in [docs/designs/coordination.md](docs/designs/coordination.md). NCode.Coordination is an original
work, derived from no other project; [ORIGINALITY.md](ORIGINALITY.md) says how it stays one.

[//]: # "Badges: to show the CI status, add this line below, with the repository's owner and name:"
[//]: # "[![CI](https://github.com/OWNER/REPO/actions/workflows/ci.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/ci.yml)"
[//]: # "Badges: to show the OpenSSF Scorecard, add this line below, with the repository's owner and name:"
[//]: # "[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/OWNER/REPO/badge)](https://scorecard.dev/viewer/?uri=github.com/OWNER/REPO)"
[//]: # "Badges: once the project is registered at bestpractices.dev, record its number as docs/best-practices-criteria.md says, then add the OpenSSF Best Practices badge that page shows."

## Development

Run from the repository root in PowerShell 7:

| Task | Command |
|---|---|
| Build | `dotnet build` |
| Test | `dotnet test` |

## AI-Assisted Workflow

This repository is set up for plan-then-execute coding: Claude Opus plans each change into `PLAN.md`, a cheaper model executes it one verified step at a time, and any step that needs a decision goes back to Opus. [AGENTS.md](AGENTS.md) is the index of the instructions every agent follows.

Work in Claude Code or GitHub Copilot, and switch whenever you like: both have the same commands, and `PLAN.md` is the only state they share, so one can plan and the other execute. Each bills its own subscription.

| Tool | Plan | Execute | Re-plan one step | Review | Land, then finish |
|---|---|---|---|---|---|
| Claude Code | `/plan-feature <what to build>` (or `/plan-bugfix`, `/plan-refactor`, `/plan-tests`, …), or `/plan-issue #42` for an issue, then review `PLAN.md` | `/orchestrate-plan` | `/replan-step N` | `/review-plan-diff` | `/ship-plan`, then `/finish-plan` |
| Copilot Chat in VS Code | The Copilot Planner agent on Opus, or `/plan-feature` or `/plan-issue #42` | Run all steps with subagents, or Execute next step | Re-plan with the planner | Review the work | `/ship-plan`, then `/finish-plan` |
| Copilot CLI | `copilot --model claude-opus-5.5`, then `/plan-feature` or `/plan-issue #42` | `/orchestrate-plan` | `/replan-step N` | `/review-plan-diff` | `/ship-plan`, then `/finish-plan` |

In Claude Code, plan in Accept edits mode and execute in Auto.

Each plan gets its own git worktree under `.claude/worktrees/`, in every tool, so several sessions can work on different plans at once.

The workflow files come from a Copier template, which owns them: never edit `.template/`, `.template-version`, `.githooks/pre-push`, `.github/instructions/project-template.instructions.md`, `.copier-answers.yml`, `CLAUDE.md` or the commands, agents and skills the template generates. To change how agents work here, edit `AGENTS.md` or a file it lists. To pull in template improvements, run `.\.template\Update-Project.ps1`: it updates itself, replaces the template's files, merges template changes into the shared settings files, and never touches `AGENTS.md` or this README. To plan the update first, with a trial run, the project files that need changing and the checks, run `/plan-template-update`. The weekly `template-update` workflow opens an issue, labeled `template-update`, when a newer template version is out, and closes it once the project has updated; `/plan-issue` plans it.

## Project

- [CONTRIBUTING.md](CONTRIBUTING.md): how to set up, build, test and propose a change.
- [SECURITY.md](SECURITY.md): how to report a vulnerability privately.
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md), [GOVERNANCE.md](GOVERNANCE.md) and [ROADMAP.md](ROADMAP.md): how we work together, who decides, and where the project is going.
- [docs/architecture.md](docs/architecture.md) and [docs/assurance-case.md](docs/assurance-case.md): how the project is built, and why it meets its security requirements.
- [docs/decisions/](docs/decisions/README.md): the decisions behind the design, the project's and the template's.
- `CHANGELOG.md`: in the Keep a Changelog format, what the product does until its first stable release, while `AGENTS.md`'s Landing section says `pre-release: untracked`, and what changed in each release from then on. The first plan creates it.
- [LICENSE.txt](LICENSE.txt): the license.

## License

NCode.Coordination is licensed under the [Apache License 2.0](LICENSE.txt).

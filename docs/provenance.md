# Provenance

Sources consulted outside a decision record, as [`CLEAN-ROOM.md`](../CLEAN-ROOM.md) requires: what was read, when, and
what it informed. Each entry is documentation; no source file was opened.

| Date       | Source                                                                                  | Read                                                                    | Informed                                                     |
| ---------- | --------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------ |
| 2026-11-24 | [Stalwart: Keep backend](https://stalw.art/docs/storage/backends/keep/)                 | The page                                                                | The split between an in-memory store and a cluster coordinator |
| 2026-11-24 | [Stalwart: Keep architecture](https://stalw.art/docs/cluster/keep/architecture/)        | The first part of the page                                              | Placement without consensus; fencing on partition            |
| 2026-11-24 | [stalwartlabs/keep](https://github.com/stalwartlabs/keep)                               | `README.md` and `docs/DESIGN.md`, documentation files only              | Per-key guarantee statements; lock quarantine; gap detection |
| 2026-11-24 | [microsoft/garnet](https://github.com/microsoft/garnet)                                 | `README.md`, documentation only                                         | The RESP provider's Garnet profile                           |
| 2026-11-24 | [Garnet documentation](https://microsoft.github.io/garnet/docs)                         | API compatibility, scripting, cluster, replication, extensions, security | The RESP provider's Garnet profile                           |
| 2026-11-24 | NCode.Identity decision records 1164, 1205, 1218, 1223, 1246, 1291, 1404                | The records, a product of this organization                             | The needs inventory in the design                           |

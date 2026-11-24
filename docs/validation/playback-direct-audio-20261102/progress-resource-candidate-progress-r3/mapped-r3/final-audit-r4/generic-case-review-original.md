# Case review

- status: FAIL
- case_root: D:\riz-build-cache-next\playback-progress-resource-continuity-r1-20261102\native-agent-finish-review-r6-20261103
- generated_at: 2026-11-03T00:40:21.617951+00:00

## Summary

| Metric | Value |
|---|---:|
| errors | 7 |
| warnings | 2 |
| evidence | 0 |
| workitems | 0 |
| timeline_events | 0 |
| findings | 0 |
| paths | 0 |

## Checks

| Level | Code | Location | Detail |
|---|---|---|---|
| error | workitems.missing | workitems.md | workitems.md is missing |
| warning | timeline.empty | timeline.md | no append-only timeline events were found |
| error | scope.auth_missing | scope.md | auth.status is missing |
| error | scope.network_missing | scope.md | network_profile.mode is missing |
| error | scope.ready_missing | scope.md | signoff.ready_for_act is missing |
| error | scope.assets_missing | scope.md | in_scope.assets is empty for a network case |
| error | scope.not_ready | scope.md | scope is not ready for target ACT |
| error | evidence.dir_missing | evidence | evidence directory is missing |
| warning | report.missing | report | no Markdown report was found |

## Traceability

| Evidence | Work items | Timeline | Reports |
|---|---:|---:|---:|
| n/a | 0 | 0 | 0 |
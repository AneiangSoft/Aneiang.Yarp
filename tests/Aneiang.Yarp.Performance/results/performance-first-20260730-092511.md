# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:26:51
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,128 | 0.61 | 0.67 | 0.00 | 2129 | PASS |
| Aneiang full | concurrency-16 | 16 | 19,920 | 1.05 | 1.37 | 0.00 | 19941 | PASS |
| Aneiang full | concurrency-64 | 64 | 22,604 | 4.12 | 6.40 | 0.00 | 22657 | PASS |
| Aneiang full | concurrency-128 | 128 | 22,810 | 8.93 | 12.06 | 0.00 | 22893 | PASS |
| Aneiang full | response-1024 | 32 | 20,897 | 2.17 | 3.02 | 0.00 | 20907 | PASS |
| Aneiang full | request-1024 | 32 | 17,472 | 2.54 | 3.68 | 0.00 | 17486 | PASS |
| Aneiang full | response-65536 | 32 | 9,819 | 7.34 | 20.65 | 0.00 | 9827 | PASS |
| Aneiang full | request-65536 | 32 | 5,697 | 13.08 | 23.57 | 0.00 | 5722 | PASS |
| Aneiang full | response-1048576 | 32 | 934 | 57.80 | 72.43 | 0.00 | 970 | PASS |
| Aneiang full | request-1048576 | 32 | 537 | 91.31 | 103.20 | 0.00 | 562 | PASS |
| log-meta | plain | 32 | 20,392 | 2.25 | 3.06 | 0.00 | 20421 | PASS |
| log-request | post-64kb | 32 | 3,289 | 24.81 | 36.17 | 0.00 | 3350 | PASS |
| log-response | response-64kb | 32 | 8,383 | 14.07 | 22.66 | 0.00 | 8400 | PASS |
| log-sqlite | post-64kb | 32 | 2,474 | 29.22 | 38.85 | 0.00 | 2489 | PASS |
| waf-normal | normal | 32 | 20,670 | 2.17 | 3.20 | 0.00 | 20692 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 48.60 | 48.60 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 659 | 227.13 | 1,422.60 | 6.00 | 1268 | PASS |
| rate-sliding | plain | 64 | 396 | 1,137.72 | 2,166.12 | 5.66 | 1000 | PASS |
| rate-token | plain | 64 | 986 | 435.07 | 885.68 | 4.31 | 2000 | PASS |
| rate-concurrency | plain | 64 | 1,551 | 62.80 | 508.71 | 2.32 | 2738 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 65.69 | 65.69 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 17,207 | 6.69 | 11.37 | 0.00 | 51651 | PASS |

## Functional Assertions

- Aneiang full/concurrency-1: PASS - All responses successful; observed `load`.
- Aneiang full/concurrency-16: PASS - All responses successful; observed `load`.
- Aneiang full/concurrency-64: PASS - All responses successful; observed `load`.
- Aneiang full/concurrency-128: PASS - All responses successful; observed `load`.
- Aneiang full/response-1024: PASS - All responses successful; observed `load`.
- Aneiang full/request-1024: PASS - All responses successful; observed `load`.
- Aneiang full/response-65536: PASS - All responses successful; observed `load`.
- Aneiang full/request-65536: PASS - All responses successful; observed `load`.
- Aneiang full/response-1048576: PASS - All responses successful; observed `load`.
- Aneiang full/request-1048576: PASS - All responses successful; observed `load`.
- log-meta/plain: PASS - All responses successful; observed `load`.
- log-request/post-64kb: PASS - All responses successful; observed `load`.
- log-response/response-64kb: PASS - All responses successful; observed `load`.
- log-sqlite/post-64kb: PASS - All responses successful; observed `load`.
- waf-normal/normal: PASS - All responses successful; observed `load`.
- waf-attack//api/perf/plain?q=%27%20OR%201%3D1--: PASS - Expected status 403, backend calls 0; observed `Forbidden`.
- rate-fixed/plain: PASS - Expected policy rejections included; observed `load`.
- rate-sliding/plain: PASS - Expected policy rejections included; observed `load`.
- rate-token/plain: PASS - Expected policy rejections included; observed `load`.
- rate-concurrency/plain: PASS - Expected policy rejections included; observed `load`.
- retry//api/perf/flaky/2: PASS - Expected status 200, backend calls 3; observed `OK`.
- circuit/open-half-open-closed: PASS - 3 backend failures, 3 open rejections, successful half-open probe; observed `503/503/503/503/503/503/200`.
- soak/plain-3s: PASS - All responses successful; observed `load`.

> Same-machine closed-loop benchmark. Policy rejection percentages are expected for rate-limit and WAF scenarios and are not transport failures.

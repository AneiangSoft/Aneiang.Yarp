# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 08:59:50
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 1,839 | 0.75 | 1.00 | 0.00 | 0 | PASS |
| Aneiang full | concurrency-16 | 16 | 19,641 | 1.07 | 1.43 | 0.00 | 0 | PASS |
| Aneiang full | concurrency-64 | 64 | 22,085 | 4.05 | 5.03 | 0.00 | 0 | PASS |
| Aneiang full | concurrency-128 | 128 | 21,976 | 8.86 | 11.21 | 0.00 | 0 | PASS |
| Aneiang full | response-1024 | 32 | 20,478 | 2.21 | 3.17 | 0.00 | 0 | PASS |
| Aneiang full | request-1024 | 32 | 17,566 | 2.46 | 3.37 | 0.00 | 0 | PASS |
| Aneiang full | response-65536 | 32 | 8,831 | 14.86 | 23.53 | 0.00 | 0 | PASS |
| Aneiang full | request-65536 | 32 | 6,017 | 11.43 | 22.06 | 0.00 | 0 | PASS |
| Aneiang full | response-1048576 | 32 | 971 | 53.95 | 72.69 | 0.00 | 0 | PASS |
| Aneiang full | request-1048576 | 32 | 547 | 90.96 | 113.40 | 0.00 | 0 | PASS |
| log-meta | plain | 32 | 20,638 | 2.14 | 2.67 | 0.00 | 0 | PASS |
| log-request | post-64kb | 32 | 17,132 | 3.22 | 4.46 | 100.00 | 0 | FAIL |
| log-response | response-64kb | 32 | 7,816 | 17.83 | 23.49 | 0.00 | 0 | PASS |
| log-sqlite | post-64kb | 32 | 16,696 | 3.28 | 4.24 | 100.00 | 0 | FAIL |
| waf-normal | normal | 32 | 20,275 | 2.08 | 2.77 | 0.00 | 0 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 46.67 | 46.67 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 997 | 409.20 | 940.86 | 5.36 | 0 | PASS |
| rate-sliding | plain | 64 | 654 | 535.98 | 1,304.61 | 9.13 | 0 | PASS |
| rate-token | plain | 64 | 1,247 | 300.98 | 521.24 | 4.40 | 0 | PASS |
| rate-concurrency | plain | 64 | 2,484 | 9.15 | 608.17 | 1.12 | 0 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 80.58 | 80.58 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 21,937 | 4.16 | 5.34 | 0.00 | 0 | PASS |

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
- log-request/post-64kb: FAIL - All responses successful; observed `load`.
- log-response/response-64kb: PASS - All responses successful; observed `load`.
- log-sqlite/post-64kb: FAIL - All responses successful; observed `load`.
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

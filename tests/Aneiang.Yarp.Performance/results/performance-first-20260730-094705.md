# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:48:41
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,621 | 0.48 | 0.56 | 0.00 | 2622 | PASS |
| Aneiang full | concurrency-16 | 16 | 20,181 | 1.03 | 1.41 | 0.00 | 20189 | PASS |
| Aneiang full | concurrency-64 | 64 | 22,757 | 3.99 | 4.92 | 0.00 | 22779 | PASS |
| Aneiang full | concurrency-128 | 128 | 23,270 | 8.05 | 10.59 | 0.00 | 23312 | PASS |
| Aneiang full | response-1024 | 32 | 20,954 | 2.05 | 2.65 | 0.00 | 20968 | PASS |
| Aneiang full | request-1024 | 32 | 18,477 | 2.34 | 3.16 | 0.00 | 18489 | PASS |
| Aneiang full | response-65536 | 32 | 8,805 | 13.86 | 20.89 | 0.00 | 8864 | PASS |
| Aneiang full | request-65536 | 32 | 6,352 | 9.29 | 21.90 | 0.00 | 6397 | PASS |
| Aneiang full | response-1048576 | 32 | 977 | 52.48 | 73.20 | 0.00 | 985 | PASS |
| Aneiang full | request-1048576 | 32 | 557 | 92.91 | 112.96 | 0.00 | 568 | PASS |
| log-meta | plain | 32 | 20,497 | 2.12 | 2.83 | 0.00 | 20516 | PASS |
| log-request | post-64kb | 32 | 8,874 | 7.82 | 19.25 | 100.00 | 0 | FAIL |
| log-response | response-64kb | 32 | 8,681 | 12.70 | 19.39 | 0.00 | 8779 | PASS |
| log-sqlite | post-64kb | 32 | 7,541 | 7.73 | 11.13 | 100.00 | 0 | FAIL |
| waf-normal | normal | 32 | 20,560 | 2.17 | 2.89 | 0.00 | 20583 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 47.99 | 47.99 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 1,061 | 290.18 | 841.58 | 5.06 | 1971 | PASS |
| rate-sliding | plain | 64 | 507 | 1,208.18 | 1,605.89 | 7.83 | 1000 | PASS |
| rate-token | plain | 64 | 613 | 1,095.14 | 1,313.22 | 3.53 | 1012 | PASS |
| rate-concurrency | plain | 64 | 957 | 10.89 | 1,313.64 | 1.74 | 1971 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 69.40 | 69.40 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 22,092 | 4.06 | 5.45 | 0.00 | 66349 | PASS |

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

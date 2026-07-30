# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:44:58
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,010 | 0.63 | 0.72 | 0.00 | 2011 | PASS |
| Aneiang full | concurrency-16 | 16 | 19,563 | 1.06 | 1.33 | 0.00 | 19575 | PASS |
| Aneiang full | concurrency-64 | 64 | 22,138 | 4.14 | 6.86 | 0.00 | 22180 | PASS |
| Aneiang full | concurrency-128 | 128 | 21,939 | 9.69 | 14.13 | 0.00 | 22028 | PASS |
| Aneiang full | response-1024 | 32 | 20,232 | 2.31 | 3.68 | 0.00 | 20261 | PASS |
| Aneiang full | request-1024 | 32 | 17,712 | 2.57 | 4.02 | 0.00 | 17732 | PASS |
| Aneiang full | response-65536 | 32 | 10,410 | 5.96 | 22.14 | 0.00 | 10419 | PASS |
| Aneiang full | request-65536 | 32 | 5,971 | 11.07 | 22.29 | 0.00 | 6018 | PASS |
| Aneiang full | response-1048576 | 32 | 970 | 64.20 | 85.59 | 0.00 | 982 | PASS |
| Aneiang full | request-1048576 | 32 | 553 | 84.63 | 110.70 | 0.00 | 569 | PASS |
| log-meta | plain | 32 | 19,450 | 2.30 | 3.47 | 0.00 | 19474 | PASS |
| log-request | post-64kb | 32 | 3,504 | 20.34 | 29.21 | 0.00 | 3525 | PASS |
| log-response | response-64kb | 32 | 8,344 | 15.29 | 21.50 | 0.00 | 8394 | PASS |
| log-sqlite | post-64kb | 32 | 3,099 | 19.31 | 30.77 | 0.00 | 3115 | PASS |
| waf-normal | normal | 32 | 21,073 | 2.01 | 2.51 | 0.00 | 21087 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 39.32 | 39.32 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 804 | 25.61 | 1,791.47 | 4.58 | 2000 | PASS |
| rate-sliding | plain | 64 | 480 | 859.81 | 1,405.49 | 6.54 | 1000 | PASS |
| rate-token | plain | 64 | 1,021 | 325.62 | 1,088.86 | 6.54 | 2000 | PASS |
| rate-concurrency | plain | 64 | 2,157 | 14.24 | 541.02 | 1.59 | 4077 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 69.39 | 69.39 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 22,715 | 3.94 | 4.72 | 0.00 | 68181 | PASS |

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

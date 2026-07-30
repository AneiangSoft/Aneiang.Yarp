# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 10:07:23
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,560 | 0.50 | 0.61 | 0.00 | 2561 | PASS |
| Aneiang full | concurrency-16 | 16 | 19,886 | 1.07 | 1.46 | 0.00 | 19895 | PASS |
| Aneiang full | concurrency-64 | 64 | 18,035 | 5.78 | 8.83 | 0.00 | 18087 | PASS |
| Aneiang full | concurrency-128 | 128 | 21,971 | 9.94 | 14.22 | 0.00 | 22061 | PASS |
| Aneiang full | response-1024 | 32 | 20,538 | 2.18 | 3.57 | 0.00 | 20557 | PASS |
| Aneiang full | request-1024 | 32 | 17,404 | 2.60 | 3.98 | 0.00 | 17436 | PASS |
| Aneiang full | response-65536 | 32 | 9,228 | 9.13 | 20.23 | 0.00 | 9246 | PASS |
| Aneiang full | request-65536 | 32 | 6,309 | 13.67 | 24.85 | 0.00 | 6347 | PASS |
| Aneiang full | response-1048576 | 32 | 1,017 | 53.95 | 61.59 | 0.00 | 1060 | PASS |
| Aneiang full | request-1048576 | 32 | 528 | 103.54 | 116.58 | 0.00 | 556 | PASS |
| log-meta | plain | 32 | 21,551 | 1.95 | 2.37 | 0.00 | 21561 | PASS |
| log-request | post-64kb | 32 | 4,927 | 12.96 | 24.20 | 0.00 | 5014 | PASS |
| log-response | response-64kb | 32 | 8,137 | 17.21 | 24.37 | 0.00 | 8155 | PASS |
| log-sqlite | post-64kb | 32 | 3,374 | 17.17 | 32.22 | 0.00 | 3392 | PASS |
| waf-normal | normal | 32 | 20,481 | 2.13 | 2.74 | 0.00 | 20493 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 45.85 | 45.85 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 874 | 274.33 | 1,308.11 | 6.25 | 1964 | PASS |
| rate-sliding | plain | 64 | 381 | 654.04 | 2,363.56 | 9.90 | 1037 | PASS |
| rate-token | plain | 64 | 470 | 490.08 | 1,407.89 | 11.27 | 1000 | PASS |
| rate-concurrency | plain | 64 | 2,955 | 9.38 | 520.90 | 0.97 | 4712 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 70.47 | 70.47 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 23,056 | 3.87 | 4.86 | 0.00 | 69188 | PASS |

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

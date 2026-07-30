# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:12:34
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,276 | 0.57 | 0.69 | 0.00 | 0 | PASS |
| Aneiang full | concurrency-16 | 16 | 19,575 | 1.07 | 1.45 | 0.00 | 0 | PASS |
| Aneiang full | concurrency-64 | 64 | 23,080 | 3.87 | 4.77 | 0.00 | 0 | PASS |
| Aneiang full | concurrency-128 | 128 | 22,881 | 8.30 | 11.85 | 0.00 | 0 | PASS |
| Aneiang full | response-1024 | 32 | 20,948 | 2.16 | 2.89 | 0.00 | 0 | PASS |
| Aneiang full | request-1024 | 32 | 18,312 | 2.36 | 3.07 | 0.00 | 0 | PASS |
| Aneiang full | response-65536 | 32 | 9,340 | 9.62 | 22.39 | 0.00 | 0 | PASS |
| Aneiang full | request-65536 | 32 | 5,322 | 13.59 | 25.15 | 0.00 | 0 | PASS |
| Aneiang full | response-1048576 | 32 | 862 | 70.37 | 101.71 | 0.00 | 0 | PASS |
| Aneiang full | request-1048576 | 32 | 556 | 93.48 | 116.87 | 0.00 | 0 | PASS |
| log-meta | plain | 32 | 21,322 | 1.96 | 2.42 | 0.00 | 0 | PASS |
| log-request | post-64kb | 32 | 1,278 | 33.27 | 42.32 | 0.00 | 0 | PASS |
| log-response | response-64kb | 32 | 8,270 | 15.57 | 22.36 | 0.00 | 0 | PASS |
| log-sqlite | post-64kb | 32 | 1,041 | 46.45 | 57.42 | 0.00 | 0 | PASS |
| waf-normal | normal | 32 | 20,664 | 2.10 | 2.69 | 0.00 | 0 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 46.51 | 46.51 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 1,054 | 267.40 | 1,031.56 | 5.96 | 0 | PASS |
| rate-sliding | plain | 64 | 591 | 358.56 | 1,374.76 | 10.21 | 0 | PASS |
| rate-token | plain | 64 | 923 | 281.45 | 1,221.77 | 5.53 | 0 | PASS |
| rate-concurrency | plain | 64 | 1,170 | 22.96 | 689.93 | 2.30 | 0 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 71.30 | 71.30 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 23,122 | 3.87 | 4.74 | 0.00 | 0 | PASS |

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

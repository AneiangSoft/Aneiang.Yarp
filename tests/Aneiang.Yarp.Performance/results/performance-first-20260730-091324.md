# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:17:05
- Runtime: 8.0.28; processors: 16
- Regular duration: 5s; soak duration: 30s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 3,487 | 0.38 | 0.46 | 0.00 | 17438 | PASS |
| Aneiang full | concurrency-16 | 16 | 22,924 | 0.91 | 1.17 | 0.00 | 114626 | PASS |
| Aneiang full | concurrency-64 | 64 | 24,834 | 3.56 | 4.27 | 0.00 | 124207 | PASS |
| Aneiang full | concurrency-128 | 128 | 24,580 | 7.41 | 8.83 | 0.00 | 122926 | PASS |
| Aneiang full | response-1024 | 32 | 23,333 | 1.86 | 2.34 | 0.00 | 116680 | PASS |
| Aneiang full | request-1024 | 32 | 19,655 | 2.20 | 2.76 | 0.00 | 98285 | PASS |
| Aneiang full | response-65536 | 32 | 9,801 | 12.90 | 23.38 | 0.00 | 49077 | PASS |
| Aneiang full | request-65536 | 32 | 6,500 | 10.05 | 22.76 | 0.00 | 32520 | PASS |
| Aneiang full | response-1048576 | 32 | 976 | 51.83 | 67.79 | 0.00 | 4917 | PASS |
| Aneiang full | request-1048576 | 32 | 578 | 83.15 | 108.39 | 0.00 | 2920 | PASS |
| log-meta | plain | 32 | 23,885 | 1.84 | 2.36 | 0.00 | 119441 | PASS |
| log-request | post-64kb | 32 | 1,223 | 35.11 | 49.46 | 0.00 | 6137 | PASS |
| log-response | response-64kb | 32 | 9,117 | 14.95 | 22.80 | 0.00 | 45597 | PASS |
| log-sqlite | post-64kb | 32 | 1,011 | 48.87 | 69.70 | 0.00 | 5076 | PASS |
| waf-normal | normal | 32 | 23,760 | 1.84 | 2.28 | 0.00 | 118811 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 48.76 | 48.76 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 924 | 367.34 | 1,374.78 | 5.64 | 5090 | PASS |
| rate-sliding | plain | 64 | 694 | 379.54 | 1,422.17 | 8.70 | 4000 | PASS |
| rate-token | plain | 64 | 974 | 371.16 | 1,196.14 | 5.83 | 5964 | PASS |
| rate-concurrency | plain | 64 | 4,498 | 6.88 | 384.17 | 0.87 | 27347 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 69.82 | 69.82 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-30s | 64 | 27,575 | 3.27 | 3.94 | 0.00 | 827253 | PASS |

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
- soak/plain-30s: PASS - All responses successful; observed `load`.

> Same-machine closed-loop benchmark. Policy rejection percentages are expected for rate-limit and WAF scenarios and are not transport failures.

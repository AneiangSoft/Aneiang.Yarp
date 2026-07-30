# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:34:40
- Runtime: 8.0.28; processors: 16
- Regular duration: 5s; soak duration: 30s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 3,340 | 0.41 | 0.49 | 0.00 | 16699 | PASS |
| Aneiang full | concurrency-16 | 16 | 20,362 | 1.05 | 1.37 | 0.00 | 101821 | PASS |
| Aneiang full | concurrency-64 | 64 | 22,206 | 4.14 | 5.35 | 0.00 | 111061 | PASS |
| Aneiang full | concurrency-128 | 128 | 23,277 | 7.92 | 10.12 | 0.00 | 116399 | PASS |
| Aneiang full | response-1024 | 32 | 20,021 | 2.15 | 2.87 | 0.00 | 100120 | PASS |
| Aneiang full | request-1024 | 32 | 17,057 | 2.53 | 3.18 | 0.00 | 85305 | PASS |
| Aneiang full | response-65536 | 32 | 8,859 | 14.10 | 22.73 | 0.00 | 44323 | PASS |
| Aneiang full | request-65536 | 32 | 5,983 | 10.50 | 24.13 | 0.00 | 29932 | PASS |
| Aneiang full | response-1048576 | 32 | 808 | 70.31 | 117.92 | 0.00 | 4061 | PASS |
| Aneiang full | request-1048576 | 32 | 467 | 119.27 | 151.01 | 0.00 | 2346 | PASS |
| log-meta | plain | 32 | 21,719 | 2.05 | 4.24 | 0.00 | 108605 | PASS |
| log-request | post-64kb | 32 | 4,147 | 19.40 | 27.67 | 0.00 | 20745 | PASS |
| log-response | response-64kb | 32 | 8,988 | 17.48 | 23.13 | 0.00 | 44951 | PASS |
| log-sqlite | post-64kb | 32 | 3,131 | 19.95 | 32.85 | 0.00 | 15661 | PASS |
| waf-normal | normal | 32 | 23,312 | 1.86 | 2.26 | 0.00 | 116565 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 37.73 | 37.73 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 950 | 398.39 | 1,255.88 | 6.17 | 5049 | PASS |
| rate-sliding | plain | 64 | 674 | 451.65 | 1,779.79 | 8.40 | 4012 | PASS |
| rate-token | plain | 64 | 1,028 | 349.22 | 1,161.33 | 5.58 | 5973 | PASS |
| rate-concurrency | plain | 64 | 3,941 | 11.70 | 354.73 | 0.91 | 21650 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 68.30 | 68.30 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-30s | 64 | 22,541 | 4.74 | 6.57 | 0.00 | 676262 | PASS |

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

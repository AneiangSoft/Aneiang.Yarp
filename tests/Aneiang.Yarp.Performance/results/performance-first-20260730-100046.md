# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 10:02:22
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,658 | 0.47 | 0.57 | 0.00 | 2659 | PASS |
| Aneiang full | concurrency-16 | 16 | 20,032 | 1.02 | 1.40 | 0.00 | 20090 | PASS |
| Aneiang full | concurrency-64 | 64 | 23,858 | 3.67 | 4.80 | 0.00 | 23876 | PASS |
| Aneiang full | concurrency-128 | 128 | 23,651 | 7.80 | 10.23 | 0.00 | 23758 | PASS |
| Aneiang full | response-1024 | 32 | 20,762 | 2.10 | 2.71 | 0.00 | 20782 | PASS |
| Aneiang full | request-1024 | 32 | 18,749 | 2.24 | 2.87 | 0.00 | 18760 | PASS |
| Aneiang full | response-65536 | 32 | 9,504 | 12.82 | 23.77 | 0.00 | 9515 | PASS |
| Aneiang full | request-65536 | 32 | 6,230 | 9.17 | 21.63 | 0.00 | 6267 | PASS |
| Aneiang full | response-1048576 | 32 | 990 | 52.49 | 57.87 | 0.00 | 1009 | PASS |
| Aneiang full | request-1048576 | 32 | 599 | 82.59 | 116.16 | 0.00 | 604 | PASS |
| log-meta | plain | 32 | 21,588 | 2.00 | 2.64 | 0.00 | 21599 | PASS |
| log-request | post-64kb | 32 | 4,731 | 15.13 | 25.69 | 0.00 | 4806 | PASS |
| log-response | response-64kb | 32 | 8,701 | 16.01 | 21.42 | 0.00 | 8722 | PASS |
| log-sqlite | post-64kb | 32 | 3,400 | 18.01 | 31.42 | 0.00 | 3419 | PASS |
| waf-normal | normal | 32 | 17,285 | 2.87 | 4.91 | 0.00 | 17324 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 47.03 | 47.03 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 981 | 375.00 | 859.06 | 5.91 | 1973 | PASS |
| rate-sliding | plain | 64 | 623 | 809.52 | 1,200.72 | 8.34 | 1000 | PASS |
| rate-token | plain | 64 | 1,017 | 368.78 | 934.52 | 5.36 | 1979 | PASS |
| rate-concurrency | plain | 64 | 2,088 | 11.53 | 534.73 | 1.22 | 3388 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 69.62 | 69.62 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 23,176 | 3.81 | 4.74 | 0.00 | 69561 | PASS |

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

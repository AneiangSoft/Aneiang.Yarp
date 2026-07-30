# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:30:52
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,573 | 0.49 | 0.60 | 0.00 | 2573 | PASS |
| Aneiang full | concurrency-16 | 16 | 19,021 | 1.12 | 1.42 | 0.00 | 19034 | PASS |
| Aneiang full | concurrency-64 | 64 | 20,468 | 4.68 | 6.83 | 0.00 | 20499 | PASS |
| Aneiang full | concurrency-128 | 128 | 20,434 | 11.00 | 17.49 | 0.00 | 20498 | PASS |
| Aneiang full | response-1024 | 32 | 15,423 | 3.43 | 5.96 | 0.00 | 15456 | PASS |
| Aneiang full | request-1024 | 32 | 13,833 | 3.65 | 5.57 | 0.00 | 13852 | PASS |
| Aneiang full | response-65536 | 32 | 7,100 | 14.36 | 20.92 | 0.00 | 7194 | PASS |
| Aneiang full | request-65536 | 32 | 4,759 | 18.22 | 25.63 | 0.00 | 4789 | PASS |
| Aneiang full | response-1048576 | 32 | 788 | 81.96 | 92.42 | 0.00 | 796 | PASS |
| Aneiang full | request-1048576 | 32 | 508 | 91.31 | 107.83 | 0.00 | 530 | PASS |
| log-meta | plain | 32 | 9,044 | 2.29 | 3.08 | 0.00 | 12831 | PASS |
| log-request | post-64kb | 32 | 2,244 | 45.96 | 78.62 | 0.00 | 2271 | PASS |
| log-response | response-64kb | 32 | 4,057 | 25.34 | 88.31 | 0.00 | 4116 | PASS |
| log-sqlite | post-64kb | 32 | 977 | 164.74 | 210.21 | 0.00 | 1073 | PASS |
| waf-normal | normal | 32 | 18,683 | 2.24 | 3.19 | 0.00 | 18699 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 47.20 | 47.20 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 878 | 335.45 | 1,204.79 | 4.64 | 1972 | PASS |
| rate-sliding | plain | 64 | 681 | 1,010.00 | 1,171.24 | 5.39 | 1000 | PASS |
| rate-token | plain | 64 | 707 | 521.91 | 1,060.92 | 8.24 | 1125 | PASS |
| rate-concurrency | plain | 64 | 1,038 | 354.59 | 690.39 | 2.63 | 2114 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 68.48 | 68.48 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 20,934 | 4.18 | 5.20 | 0.00 | 62824 | PASS |

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

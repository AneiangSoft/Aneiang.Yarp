# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 10:11:18
- Runtime: 8.0.28; processors: 16
- Regular duration: 5s; soak duration: 30s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 3,485 | 0.38 | 0.47 | 0.00 | 17426 | PASS |
| Aneiang full | concurrency-16 | 16 | 22,467 | 0.94 | 1.30 | 0.00 | 112346 | PASS |
| Aneiang full | concurrency-64 | 64 | 22,790 | 4.05 | 5.19 | 0.00 | 113984 | PASS |
| Aneiang full | concurrency-128 | 128 | 24,700 | 7.58 | 9.22 | 0.00 | 123545 | PASS |
| Aneiang full | response-1024 | 32 | 23,685 | 1.86 | 2.31 | 0.00 | 118444 | PASS |
| Aneiang full | request-1024 | 32 | 18,013 | 2.52 | 3.63 | 0.00 | 90086 | PASS |
| Aneiang full | response-65536 | 32 | 8,297 | 11.91 | 23.04 | 0.00 | 41495 | PASS |
| Aneiang full | request-65536 | 32 | 5,146 | 15.99 | 27.39 | 0.00 | 25741 | PASS |
| Aneiang full | response-1048576 | 32 | 928 | 65.87 | 91.40 | 0.00 | 4652 | PASS |
| Aneiang full | request-1048576 | 32 | 524 | 95.45 | 117.20 | 0.00 | 2656 | PASS |
| log-meta | plain | 32 | 19,204 | 2.31 | 3.36 | 0.00 | 96032 | PASS |
| log-request | post-64kb | 32 | 3,999 | 17.96 | 29.98 | 0.00 | 20026 | PASS |
| log-response | response-64kb | 32 | 8,851 | 7.67 | 22.63 | 0.00 | 44421 | PASS |
| log-sqlite | post-64kb | 32 | 3,501 | 18.88 | 26.91 | 0.00 | 17541 | PASS |
| waf-normal | normal | 32 | 19,243 | 2.32 | 3.36 | 0.00 | 96232 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 47.64 | 47.64 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 1,000 | 321.11 | 1,328.19 | 4.60 | 5987 | PASS |
| rate-sliding | plain | 64 | 499 | 774.44 | 1,859.54 | 8.48 | 3011 | PASS |
| rate-token | plain | 64 | 887 | 342.96 | 1,482.35 | 4.56 | 5990 | PASS |
| rate-concurrency | plain | 64 | 3,454 | 7.41 | 550.99 | 1.08 | 20419 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 72.10 | 72.10 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-30s | 64 | 27,810 | 3.21 | 3.90 | 0.00 | 834327 | PASS |

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

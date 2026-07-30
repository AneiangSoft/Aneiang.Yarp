# Aneiang.Yarp First Batch Performance Results

- Generated: 2026-07-30 09:51:49
- Runtime: 8.0.28; processors: 16
- Regular duration: 1s; soak duration: 3s

| Group | Scenario | C | RPS | P95 ms | P99 ms | Non-success % | Backend calls | Check |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Aneiang full | concurrency-1 | 1 | 2,474 | 0.53 | 0.67 | 0.00 | 2474 | PASS |
| Aneiang full | concurrency-16 | 16 | 18,971 | 1.11 | 1.57 | 0.00 | 18982 | PASS |
| Aneiang full | concurrency-64 | 64 | 22,011 | 4.04 | 5.15 | 0.00 | 22046 | PASS |
| Aneiang full | concurrency-128 | 128 | 21,337 | 8.87 | 11.91 | 0.00 | 21392 | PASS |
| Aneiang full | response-1024 | 32 | 19,633 | 2.26 | 3.08 | 0.00 | 19658 | PASS |
| Aneiang full | request-1024 | 32 | 16,662 | 2.62 | 4.05 | 0.00 | 16680 | PASS |
| Aneiang full | response-65536 | 32 | 8,281 | 15.60 | 21.33 | 0.00 | 8311 | PASS |
| Aneiang full | request-65536 | 32 | 5,974 | 10.97 | 20.24 | 0.00 | 6013 | PASS |
| Aneiang full | response-1048576 | 32 | 940 | 50.96 | 80.51 | 0.00 | 947 | PASS |
| Aneiang full | request-1048576 | 32 | 581 | 84.65 | 127.86 | 0.00 | 597 | PASS |
| log-meta | plain | 32 | 21,209 | 1.97 | 2.59 | 0.00 | 21226 | PASS |
| log-request | post-64kb | 32 | 4,757 | 13.39 | 23.48 | 0.00 | 4771 | PASS |
| log-response | response-64kb | 32 | 8,618 | 14.52 | 22.28 | 0.00 | 8639 | PASS |
| log-sqlite | post-64kb | 32 | 3,489 | 16.65 | 32.32 | 0.00 | 3506 | PASS |
| waf-normal | normal | 32 | 21,303 | 2.01 | 2.56 | 0.00 | 21326 | PASS |
| waf-attack | /api/perf/plain?q=%27%20OR%201%3D1-- | 1 | 1 | 50.20 | 50.20 | 100.00 | 0 | PASS |
| rate-fixed | plain | 64 | 827 | 534.23 | 1,127.56 | 5.12 | 2000 | PASS |
| rate-sliding | plain | 64 | 420 | 1,102.26 | 2,104.68 | 5.21 | 1000 | PASS |
| rate-token | plain | 64 | 1,181 | 152.16 | 680.52 | 4.90 | 2000 | PASS |
| rate-concurrency | plain | 64 | 3,628 | 7.37 | 465.80 | 0.83 | 5978 | PASS |
| retry | /api/perf/flaky/2 | 1 | 1 | 68.25 | 68.25 | 0.00 | 3 | PASS |
| circuit | open-half-open-closed | 1 | 7 | 0.00 | 0.00 | 0.00 | 4 | PASS |
| soak | plain-3s | 64 | 19,895 | 5.33 | 7.71 | 0.00 | 59715 | PASS |

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

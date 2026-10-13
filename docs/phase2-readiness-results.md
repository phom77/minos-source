# Phase 2 — đo tải và chuẩn bị ranh giới dịch vụ

Ngày: 2026-10-13. Phạm vi: profile Docker development hiện tại, một Worker và năm production projects. Phase 2 tạo phép đo, kiểm chứng diagnostics và thiết kế contract; chưa triển khai extraction, broker, distributed Worker hoặc M8. Development-MVP acceptance vẫn là quyết định riêng của owner.

## Kết luận đề xuất

**Giữ kiến trúc hiện tại và tuning single-host khi có nhu cầu latency cụ thể. Chưa có bằng chứng đủ để tách microservice.** Trên workload nhẹ đã đo, admission/claim/result commit và API đều nhỏ hơn đáng kể so với chi phí sandbox. Một interface mạng mới không loại bỏ chi phí tạo/dọn sandbox cho từng test.

Không tăng quota hoặc đổi runner trên stack đang dùng chỉ vì benchmark có lợi. Mức bốn jobs trong workload này còn tăng cả compile/runtime quotas từ một lên hai, tổng slots từ hai lên bốn; đây là so sánh các **profile cấu hình**, không cô lập tác động của riêng số jobs. Profile mặc định hiện tại vẫn có quotas một/một. Native profile đã có bằng chứng riêng nhưng máy hiện tại chưa đáp ứng chính sách live native; không dùng bypass để chạy benchmark hoặc suy ra tỷ lệ tăng tốc giữa các môi trường khác nhau.

## Những gì đã triển khai

| Hạng mục | Thay đổi và bằng chứng |
| --- | --- |
| P2.1 workload/targets | [Manifest](../scripts/fixtures/readiness-workload.json) chốt trước run: 9 jobs / 2 owners / 3 tests; mức jobs 1, 2, 4; Accepted, WrongAnswer, RuntimeError, compile failure, CPU work, `bits/stdc++.h`, memory/output failure; thêm 1 job / 100 tests. |
| P2.2 harness tái lập | [Controller](../scripts/check-readiness.py) tạo project/credentials/database/volume riêng, kiểm tra không có Worker/sandbox sống, giữ image identities, chạy HTTP/PostgreSQL/actual Worker và xuất JSON/TRX; dọn fixture và xóa file credentials. [Sampler](../scripts/collect-readiness-resources.py) chạy non-root trong checks host, dùng bounded Engine reads, chỉ lấy container thuộc project hoặc sandbox mount đúng volume; không đọc nội dung file. |
| P2.3 khoảng trống measurement | Infrastructure thêm phase `persistence`: `practice-admission`, `contest-admission`, `claim`, `renew`, `complete`, `fail`. Duration gồm transaction/lock/commit/disposal và mọi disposition, không tự chứng minh một request được chấp nhận. API/Worker/executor logs nối opaque submission/job/attempt IDs và HTTP trace ID, giữ keys/tokens/source/exception details ngoài logs. |
| P2.3 regression | `JudgingObservabilityIntegrationTests` đi qua admission → infrastructure failure → retry → commit, kiểm tra correlation và không lộ source/request key/lease token/exception detail; `ReliabilityTests.Operations_track_stuck_queue_expired_lease_exhaustion_and_a_fresh_recovery_submission` kiểm tra aggregate queue age, expired lease, retries/SystemFailed rồi một submission mới sau recovery. |
| P2.4 ownership | [Ownership matrix](judging-extraction-contract.md#current-data-authority) ghi writer/reader/authority, atomic boundaries, authorization, artifacts, retention và migration cho accounts/problems/contests/submissions/jobs/results. |
| P2.5 candidate contract | [Contract v1 design](judging-extraction-contract.md#candidate-contract-v1-design-only) giữ Core admission/lease/result authority; execution host giữ sandbox/scheduling/checking/cleanup. Có identity/version/bounds/artifact scope, claim/renew/complete/fail/disposition và error matrix. Wire adapters chưa tồn tại; bounds transport đề xuất chưa được nghiệm thu. |
| P2.6 decision | Bảng lựa chọn, giới hạn bằng chứng và triggers bên dưới; không coi việc tách service là điều kiện để hoàn thành readiness. |

Không thêm package/project, database migration, grant, production exporter/server, dịch vụ hoặc topology mới. Tests và harness nằm trong test project/scripts hiện có. Meters vẫn có fixed stage/phase labels, opaque IDs chỉ ở trusted logs/evidence.

## Phương pháp và môi trường

- Docker Desktop Engine 29.8.2, kernel `6.18.54.1-microsoft-standard-WSL2`, cgroup v2, 16 logical CPUs, 12,175,966,208 bytes VM RAM. Checks chạy UID/GID 1654; sandbox là siblings. GCC/C++20 image và compiler/runtime bounds giữ nguyên.
- Images đã cache; database/volume mới. Mỗi case khởi tạo executor mới, timer initialization tách riêng. Case đầu JIT-cold, các case tiếp theo process-warm; đây không phải benchmark cold image pull/startup hoặc host hoàn toàn không có tải nền.
- Admission đo HTTP POST sau CSRF/setup đến response; queue lấy persisted UTC creation → first attempt start. Completion dùng monotonic clock sau admission response đến owner poll thấy terminal, poll khoảng 100 ms. Queue/completion khác clocks/windows, không trừ trực tiếp để tính execution.
- API probes gồm owner detail và catalog qua ASP.NET in-process host khi Worker hoạt động, có PostgreSQL thật; **không gồm HTTPS/network hoặc browser latency**. Không coi p95 này là SLA của deployment.
- Percentiles dùng nearest rank `ceil(N*p)`; N=9 ở mỗi mixed profile, nên p95 chính là mẫu lớn nhất. Bài 100 tests có N=1 submission; p50/p95 của submission là cùng một quan sát, không phải phân phối dân số. Histograms per-test có N=100.
- Throughput = jobs / thời gian chạy Worker workload sau burst admission; accounts/problem/publication/init nằm ngoài timer. Request kinds dùng chung admission pool; rejected sixth submission của account đầy quota không tạo work. Global capacity contention có regression PostgreSQL riêng trong `ReliabilityTests`/contest tests.
- CPU sampler dùng delta của cgroup CPU giữa hai observations, quy đổi 100%=một core; sandbox ngắn có thể chỉ xuất hiện một lần và không có delta. RAM là sampled cgroup usage gồm cache, không phải RSS-only. Host memory availability lấy VM `/proc/meminfo`; workspace disk free lấy filesystem của volume. Sampled peaks là lower bounds; không biến CPU chưa quan sát thành peak bằng không.
- Disk workspace bytes không gồm PostgreSQL, private-artifact lifetime storage, Docker layers hoặc backup; workspace headroom không thay thế disk alert của private storage. Stage-memory/slots peaks đo bằng meters chính xác cho reservation; reservations là upper-bound allocation policy, không phải RAM đã tiêu thụ.

## Số liệu đã có

Run giữ image identities cuối cùng: ignored `artifacts/minos-readiness-20261013-160145-a30564/`, gồm `manifest.json`, `summary.json`, `resources.json`, `resources-stream.jsonl`, `tests.log` và TRX. Chạy từ revision `3d1818da9e75f68d8c55112eff41f25c0d5094c9`; identities được pin trong Compose override và lưu trong manifest. [Compact evidence](performance/phase2-20261013.json) được giữ trong Git; raw logs/samples giữ ở artifacts. Windows build/Git operations và Docker build chuẩn bị image có xảy ra trong các lượt thử nghiệm trước; lượt cuối không chạy build cùng workload. Không dùng các lượt này làm một paired optimization claim hoàn toàn cô lập khỏi tải nền của host.

| Workload / active jobs | Elapsed (s) | Queue p50 / p95 (s) | Completion p50 / p95 (s) | API p95 (ms) / N | Admission p95 (ms) | Jobs/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Mixed / 1 | 37.485 | 13.179 / 33.398 | 17.909 / 37.531 | 18.916 / 1194 | 344.657 | 0.240 |
| Mixed / 2 | 28.282 | 8.714 / 22.450 | 13.800 / 28.297 | 14.230 / 1018 | 50.590 | 0.318 |
| Mixed / 4 | 15.246 | 3.045 / 9.388 | 9.548 / 15.309 | 17.611 / 545 | 31.969 | 0.590 |
| 100 short tests / 4 | 82.962 | 0.039 / 0.039 | 82.961 / 82.961 | 11.033 / 1428 | 51.622 | 0.012 |

Targets chốt trước run: mixed queue p95≤120s, completion≤180s, API p95≤300ms, admission≤500ms; 100-test queue≤5s/completion≤180s; zero retries/SystemFailed và đúng một expected account rejection mỗi mixed profile. Tất cả case trong run trên đạt; complete ordered test evidence, expected verdicts, FIFO claims, shared contest links và zero leaked reservations đều được assertion. Những ngưỡng mới này là development workload targets, không mở rộng 120s alert của fixture M7 sang mọi bài.

Lượt cuối có 85 samples, zero complete sampling gaps; vẫn có thể thiếu sandbox transient trong một sample. Sampled fixture peak CPU 240.35% (khoảng 2.40 cores), cgroup memory 748,478,464 bytes (~714 MiB), workspace payload 32,470 bytes. Minimum VM MemAvailable khoảng 83.01%, workspace filesystem free khoảng 93.62%, trên targets 25%/20%. Peak reservations của mixed 1/2/4 jobs là 576/648/1296 MiB và 1/2/4 slots; mỗi case trả reservations về zero sau shutdown. Management CPU/reserve validation giữ nguyên.

Lượt collector mới trước đó (`artifacts/minos-readiness-20261013-155608-0af243/`) cũng passed all five tests, 88 samples/zero complete gaps; mixed elapsed 39.791/28.650/15.171s và 100-test elapsed 85.026s. Với N nhỏ và thứ tự JIT/cache này, sự lặp lại hỗ trợ phạm vi development fixture, không chứng minh confidence interval của production p95.

Run đầu dùng Docker CLI sampler: `artifacts/minos-readiness-20261013-153319-156b4a/`, all five selected tests passed, mixed elapsed 38.425/26.971/15.937s và 100-test elapsed 79.572s. Chỉ có 18 complete resource samples/27 gaps, nên không dùng peak tài nguyên của lượt này để kết luận headroom.

Một lượt giữa hai run (`artifacts/minos-readiness-20261013-154202-053361/`) failed: mixed four-job case có một expected-success submission trả CompilationError; thời điểm đó harness chưa lưu chi tiết failure evidence, nên **chưa xác định nguyên nhân của outlier**. Controller đã từ chối tạo complete readiness summary. Harness mới lưu bounded owner result của trusted fixture khi verdict lệch để lần tái hiện sau có compiler diagnostics/evidence. Không nới compiler/runtime limits hoặc đổi verdict để làm benchmark pass. Kết quả này là giới hạn của bằng chứng ổn định, cần điều tra nếu tái diễn; không gọi các run nhỏ là chứng nhận public capacity.

## Bottleneck và lựa chọn

Ở case 100 tests, compile 1.703s, `run/total` 81.086s. Trong runtime totals, provisioning 31.073s, cleanup 14.757s, guard 9.648s, baseline/final evidence 7.439/7.462s; execution/control stopwatch 10.592s. Artifact preflight 25.46ms, verified reads của 100 tests tổng 37.22ms, checking tổng 0.43ms; claim 7.80ms và result commit 30.35ms. Per-test runtime stage p50/p95 là 814.78/837.77ms. Đây là chi phí operational, không phải contestant CPU time. Các total/phase histograms overlap; không cộng `submission/total` với compile/run totals hoặc budget-wait để tạo latency mới.

| Lựa chọn | Bằng chứng / lợi ích | Chi phí và giới hạn | Khuyến nghị |
| --- | --- | --- | --- |
| Giữ hiện tại | Đã có process Worker độc lập, bounded scheduling, shared atomic admission, complete evidence, safe auth và recovery. API/DB không phải bottleneck chính trong workload này. | Docker per-test overhead vẫn cao; cần định nghĩa latency thực tế trước khi tuning. Outlier chưa được giải thích giới hạn confidence. | Chọn cho development hiện tại. |
| Tuning single-host | Các profile 1/2/4 trên cùng mixed workload cho thấy overlap có ích. Native adapter đã có bằng chứng riêng về giảm control overhead. | Quota lớn hơn dùng nhiều stage resources; không cải thiện một bài serial 100 tests theo cùng tỷ lệ. Live native phải đáp ứng các gates đã chấp nhận. | Ưu tiên nếu owner cần latency/throughput khác; benchmark cùng policy trước thay cấu hình. |
| Extract trusted judging/execution | Có thể giúp independent release/isolation of trusted host khi nhu cầu vận hành đó xuất hiện. Contract đã có candidate shape. | Chưa có measured service benefit. Thêm channel auth, versioning, reply loss, timeouts, artifact transfer, deploy/rollback; không loại bỏ sandbox overhead. Chưa có implementation để so sánh actual before/after. | No-go cho extraction ở hiện tại. Không tự thêm broker/outbox/distributed host. |

Triggers để đánh giá lại: workload đại diện lặp lại vượt targets dù tuning an toàn; API latency/availability suy giảm có quan hệ nhân quả với Worker; cần independent executor release hoặc fault boundary; requirement về public hosting/distributed capacity được owner chấp nhận. Nếu mục tiêu là học microservices, ghi rõ learning objective và budget vận hành riêng, không gán lợi ích capacity chưa đo.

## Diagnostics và alert/recovery evidence

| Tín hiệu | Đọc ở đâu | Evidence / hành động |
| --- | --- | --- |
| Pending stuck/expired leases/retry exhaustion | Admin-only `/api/operations/judging`, Application active-admin authorization | New operational-state test + existing HTTP/direct authorization tests. Dùng hai observations theo runbook; recovery bằng sole Worker/startup cleanup/bounded reclaim. |
| Admission overload | HTTP safe 429 + Retry-After | Mixed practice/contest request khi account đầy quota bị từ chối; real global-contention regression kiểm tra atomic row/job counts. Retry cùng request identity khi response bất định. |
| Claim/renew/result persistence chậm | `Minos.Judging` fixed `persistence/*` phases | Report có N/p50/p95/total. Histogram có thể gồm rejection/replay/stale/failed operation; kết hợp persisted state, không suy completion từ duration. |
| Lost lease/Worker failures | Existing `minos.worker.lost_leases`, `iteration_failures`, trusted job/attempt logs | Scheduling lease-loss/cancellation test; failed counters trong load phải bằng zero. Không đưa IDs vào metric labels. |
| Cleanup fault | Existing `minos.sandbox.cleanup_failures`, safe incomplete-evidence logs, blocked budget/delivery | Existing `ExecutionBudgetTests.Slot_is_held_until_cleanup_and_cleanup_failure_blocks_further_work`, Docker cancellation và M7 recovery. Stop/investigate; restart chỉ sau recovery thành công. |
| Admission → accepted attempt / failed attempt | HTTP trace→submission ID log; Worker claim/process job/submission/attempt IDs; executor failure IDs; durable owner/admin reads | New observability regression đi xuyên failure/retry/commit và kiểm tra không lộ secret payload. Retain historical attempts; validate recovery bằng submission mới, không sửa lịch sử. |

Report JSON là bản tối thiểu để đọc workload/phase/resource state; production dashboard/exporter/alert delivery chưa được triển khai. Local meters và admin snapshots đã đủ nguồn dữ liệu cho scope development hiện tại. Chi tiết cách tái lập và đọc signals nằm trong [runbook](operations/development-runbook.md#phase-2-readiness-measurement).

## Validation và trạng thái

Windows Release build 0 warnings/errors; 30 Domain và 19 selected architecture/scheduler/budget tests passed. Docker selected workload/observability suite passed five tests trong các run thành công; opt-in measurements không đặt latency assertions trong ordinary correctness CI. Các workload/fault fixtures mới đều dùng database/container riêng, không nâng schema/images stack development gốc.

Hosted [final full CI at `3d1818d`](https://github.com/phom77/Minos/actions/runs/38427097458) passed: Domain 30; integration 224, 15 platform/config/opt-in skips; frontend 261 và browser journeys; `m7_checks=passed`, `foundation_checks=passed`. Fifteen skips gồm 4 Phase 2 opt-in measurement cases được chạy riêng trong các lượt trên. M7 real HTTPS/long lease/workload/SIGKILL/database outage/missing artifact/paired restore checks vẫn qua. Previous [full CI at `64109a1`](https://github.com/phom77/Minos/actions/runs/38426137211) cũng xanh. Lượt CI đầu có descendant timeout trong fixture vốn có; lần kế tiếp qua fixture đó và lộ assertion timestamp precision của test mới. Assertion đã sửa theo microsecond PostgreSQL; không nới production limits/verdict policy. Không che các failed runs. Commit báo cáo cuối chỉ thay documentation/evidence, không thay code/tests/scripts đã kiểm chứng.

Stack development gốc đã dừng từ trước Phase 2; đã trả về trạng thái dừng. Không nâng schema/images, không chạy migration/grants trên database gốc. Ba edits có trước (`.dockerignore`, `.gitignore`, `docs/problem-bank-import.md`) được giữ ngoài commits của Phase 2. Các disposable fixture containers/volumes/credentials đã được dọn. Phase 3 chưa được bắt đầu. Contract/network maximum envelopes và mixed-version fault tests là prerequisites cho extraction tương lai, không phải capabilities đã có.
